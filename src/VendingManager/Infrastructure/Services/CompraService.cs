using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

namespace VendingManager.Infrastructure.Services;

public class CompraService : ICompraService
{
    private readonly ApplicationDbContext _context;
    private readonly IProductMatchingService _productMatchingService;
    private readonly IUploadPathProvider _uploadPathProvider;

    public CompraService(
        ApplicationDbContext context,
        IProductMatchingService productMatchingService,
        IUploadPathProvider uploadPathProvider)
    {
        _context = context;
        _productMatchingService = productMatchingService;
        _uploadPathProvider = uploadPathProvider;
    }

    public async Task<IEnumerable<Compra>> GetComprasAsync(int? count = null)
    {
        var query = _context.Compras
            .Include(c => c.Detalles)
            .ThenInclude(d => d.Producto)
            .OrderByDescending(c => c.FechaCompra)
            .ThenByDescending(c => c.Id)
            .AsQueryable();

        if (count.HasValue)
        {
            query = query.Take(count.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<Compra?> GetCompraByIdAsync(int id)
    {
        return await _context.Compras
            .Include(c => c.Detalles)
            .ThenInclude(d => d.Producto)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Compra> RegistrarCompraAsync(Compra compra)
    {
        // 0. Desglosar packs manuales: si el usuario indicó PackSize > 1, convertir packs a unidades.
        DesglosarPacks(compra.Detalles);

        // 1. Recalcular el Costo Promedio y sumar Stock en Bodega (solo si hay producto)
        foreach (var detalle in compra.Detalles)
        {
            if (detalle.EsPendiente) continue;
            if (detalle.ProductoId.HasValue)
            {
                var producto = await _context.Productos.FindAsync(detalle.ProductoId.Value);
                if (producto != null)
                {
                    // CPP = ((Stock Actual * Costo Promedio Actual) + (Nueva Cant. * Nuevo Costo)) / (Stock Actual + Nueva Cant.)
                    decimal valorInventarioActual = producto.StockBodega * producto.CostoPromedio;
                    decimal valorNuevaTransaccion = detalle.Cantidad * detalle.CostoUnitario;
                    
                    int nuevoStockTotal = producto.StockBodega + detalle.Cantidad;
                    
                    if (nuevoStockTotal > 0)
                    {
                        producto.CostoPromedio = (valorInventarioActual + valorNuevaTransaccion) / nuevoStockTotal;
                    }
                    
                    producto.StockBodega = nuevoStockTotal;
                    _context.Productos.Update(producto);
                }
            }
        }

        // 2. Guardar la Compra (primero para obtener el ID)
        if (compra.FechaCompra == DateTime.MinValue)
            compra.FechaCompra = DateTime.Now;
            
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync(); // Genera compra.Id

        // 3. Insertar ProductoCosto rows para cada detalle
        foreach (var detalle in compra.Detalles.Where(d => d.ProductoId.HasValue && !d.EsPendiente))
        {
            // Cerrar filas abiertas anteriores para este producto
            var productoId = detalle.ProductoId!.Value;
            var openRows = await _context.ProductoCostos
                .Where(pc => pc.ProductoId == productoId && pc.FechaHasta == null)
                .ToListAsync();
            foreach (var row in openRows)
            {
                row.FechaHasta = compra.FechaCompra;
            }

            _context.ProductoCostos.Add(new ProductoCosto
            {
                ProductoId = productoId,
                Costo = detalle.CostoUnitario,
                FechaDesde = compra.FechaCompra,
                FechaHasta = null
            });
        }
        await _context.SaveChangesAsync();

        // 4. Aprendizaje EAN: para cada detalle CONFIRMADO con EAN + ProductoId, persistir el mapeo.
        //    Items pendientes no generan aprendizaje porque el producto puede cambiar o no confirmarse.
        foreach (var detalle in compra.Detalles.Where(d =>
            !string.IsNullOrEmpty(d.Ean) && d.ProductoId.HasValue && !d.EsPendiente))
        {
            await _productMatchingService.SaveMappingAsync(
                detalle.Ean!,
                detalle.ProductoId!.Value,
                detalle.PackSize);
        }

        // 4b. Aprendizaje SKU: para cada detalle CONFIRMADO con SKU + ProductoId, persistir el mapeo.
        foreach (var detalle in compra.Detalles.Where(d =>
            !string.IsNullOrEmpty(d.Sku) && d.ProductoId.HasValue && !d.EsPendiente))
        {
            await _productMatchingService.SaveSkuMappingAsync(
                detalle.Sku!,
                compra.Proveedor,
                detalle.ProductoId!.Value);
        }

        // 5. Registrar Movimiento en Caja automáticamente si la compra fue pagada
        if (compra.Estado == "PAGADA" && compra.PagadaCaja)
        {
            var movimiento = new MovimientoCaja
            {
                Fecha = compra.FechaCompra,
                Descripcion = $"Factura/Boleta Nº {compra.NumeroDocumento} - {compra.Proveedor}",
                Monto = -compra.MontoTotal, // Gasto de dinero
                Tipo = "GASTO",
                Categoria = ResolverCategoriaMovimiento(compra),
                CompraId = compra.Id // FK para trazabilidad bidireccional
            };
            _context.MovimientosCaja.Add(movimiento);
            await _context.SaveChangesAsync();
        }

        return compra;
    }

    public async Task MarcarComoPagada(int id)
    {
        var compra = await _context.Compras.FindAsync(id);
        if (compra != null && compra.Estado != "PAGADA")
        {
            compra.Estado = "PAGADA";
            compra.PagadaCaja = true;
            
            // Generar movimiento de egreso en caja
            var movimiento = new MovimientoCaja
            {
                Fecha = DateTime.Now,
                Descripcion = $"Pago Factura/Boleta Nº {compra.NumeroDocumento} - {compra.Proveedor}",
                Monto = -compra.MontoTotal,
                Tipo = "GASTO",
                Categoria = ResolverCategoriaMovimiento(compra),
                CompraId = compra.Id // FK para trazabilidad
            };
            _context.MovimientosCaja.Add(movimiento);
            
            _context.Compras.Update(compra);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<Compra> ActualizarCompraAsync(int id, VendingManager.Shared.DTOs.RegistrarCompraRequestDto request)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var compra = await _context.Compras
                .Include(c => c.Detalles)
                .FirstOrDefaultAsync(c => c.Id == id);
            
            if (compra == null) throw new Exception("Compra no encontrada.");

            // 1. Revertir el estado antiguo (Costo y Stock)
            await RevertirImpactoInventario(compra.Detalles);

            // 2. Actualizar cabecera
            compra.Proveedor = request.Proveedor;
            compra.NumeroDocumento = request.NumeroDocumento;
            compra.FechaCompra = request.FechaCompra;
            compra.Estado = request.Estado;
            compra.TipoFactura = request.TipoFactura;
            compra.PagadaCaja = request.PagadaCaja;
            compra.SubcategoriaGasto = request.SubcategoriaGasto;

            // 3. Reemplazar detalles
            _context.DetallesCompra.RemoveRange(compra.Detalles);
            compra.Detalles = request.Detalles.Select(d => new DetalleCompra
            {
                ProductoId = d.ProductoId > 0 ? d.ProductoId : null,
                DescripcionItem = d.DescripcionItem,
                Cantidad = d.Cantidad,
                CostoUnitario = d.CostoUnitario,
                Subtotal = d.Cantidad * d.CostoUnitario,
                EsPendiente = d.EsPendiente,
                Ean = d.Ean,
                Sku = d.Sku,
                PackSize = d.PackSize
            }).ToList();

            // 3b. Desglosar packs manuales antes de aplicar impacto
            DesglosarPacks(compra.Detalles);

            // 4. Aplicar el impacto nuevo y registrar ProductoCosto
            foreach (var detalle in compra.Detalles)
            {
                if (detalle.EsPendiente) continue;
                if (detalle.ProductoId.HasValue)
                {
                    var producto = await _context.Productos.FindAsync(detalle.ProductoId.Value);
                    if (producto != null)
                    {
                        decimal valorInventarioActual = producto.StockBodega * producto.CostoPromedio;
                        decimal valorNuevaTransaccion = detalle.Cantidad * detalle.CostoUnitario;
                        int nuevoStockTotal = producto.StockBodega + detalle.Cantidad;
                        if (nuevoStockTotal > 0)
                            producto.CostoPromedio = (valorInventarioActual + valorNuevaTransaccion) / nuevoStockTotal;
                        producto.StockBodega = nuevoStockTotal;
                        _context.Productos.Update(producto);
                    }
                }
            }

            // Insertar ProductoCosto rows: cerrar filas abiertas y crear nuevas
            var productoCostosToInsert = compra.Detalles
                .Where(d => d.ProductoId.HasValue && !d.EsPendiente)
                .Select(d => new { d.ProductoId, d.CostoUnitario })
                .Distinct()
                .ToList();

            foreach (var item in productoCostosToInsert)
            {
                var productoId = item.ProductoId!.Value;
                var openRows = await _context.ProductoCostos
                    .Where(pc => pc.ProductoId == productoId && pc.FechaHasta == null)
                    .ToListAsync();
                foreach (var row in openRows)
                    row.FechaHasta = compra.FechaCompra;
            }

            foreach (var item in productoCostosToInsert)
            {
                _context.ProductoCostos.Add(new ProductoCosto
                {
                    ProductoId = item.ProductoId!.Value,
                    Costo = item.CostoUnitario,
                    FechaDesde = compra.FechaCompra,
                    FechaHasta = null
                });
            }
            
            compra.MontoTotal = compra.Detalles.Where(d => !d.EsPendiente).Sum(d => d.Subtotal);

            // 6. Sincronizar Movimiento de Caja
            var movimiento = await _context.MovimientosCaja.FirstOrDefaultAsync(m => m.CompraId == id);
            if (compra.Estado == "PAGADA" && compra.PagadaCaja)
            {
                if (movimiento == null)
                {
                    movimiento = new MovimientoCaja { CompraId = id };
                    _context.MovimientosCaja.Add(movimiento);
                }
                movimiento.Fecha = compra.FechaCompra;
                movimiento.Descripcion = $"Factura/Boleta Nº {compra.NumeroDocumento} - {compra.Proveedor} (Editada)";
                movimiento.Monto = -compra.MontoTotal;
                movimiento.Tipo = "GASTO";
                movimiento.Categoria = ResolverCategoriaMovimiento(compra);
            }
            else if (movimiento != null)
            {
                _context.MovimientosCaja.Remove(movimiento);
            }

            await _context.SaveChangesAsync();

            // 7. Aprendizaje EAN: para cada detalle CONFIRMADO con EAN + ProductoId, persistir el mapeo.
            //    Items pendientes no generan aprendizaje porque el producto puede cambiar o no confirmarse.
            foreach (var detalle in compra.Detalles.Where(d =>
                !string.IsNullOrEmpty(d.Ean) && d.ProductoId.HasValue && !d.EsPendiente))
            {
                await _productMatchingService.SaveMappingAsync(
                    detalle.Ean!,
                    detalle.ProductoId!.Value,
                    detalle.PackSize);
            }

            // 7b. Aprendizaje SKU: para cada detalle CONFIRMADO con SKU + ProductoId, persistir el mapeo.
            foreach (var detalle in compra.Detalles.Where(d =>
                !string.IsNullOrEmpty(d.Sku) && d.ProductoId.HasValue && !d.EsPendiente))
            {
                await _productMatchingService.SaveSkuMappingAsync(
                    detalle.Sku!,
                    compra.Proveedor,
                    detalle.ProductoId!.Value);
            }

            await transaction.CommitAsync();
            return compra;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task EliminarCompraAsync(int id)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var compra = await _context.Compras
                .Include(c => c.Detalles)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (compra == null) return;

            // 1. Revertir impacto en inventario y CPP
            await RevertirImpactoInventario(compra.Detalles);

            // 2. Eliminar movimiento de caja
            var movimiento = await _context.MovimientosCaja.FirstOrDefaultAsync(m => m.CompraId == id);
            if (movimiento != null) _context.MovimientosCaja.Remove(movimiento);

            // 3. Eliminar la compra (detalles se eliminan por cascada o manualmente)
            _context.Compras.Remove(compra);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Convierte packs en unidades individuales: si un detalle tiene PackSize > 1,
    /// multiplica la cantidad y divide el costo unitario. El subtotal se mantiene.
    /// Solo aplica a items no pendientes.
    /// </summary>
    private static void DesglosarPacks(IEnumerable<DetalleCompra> detalles)
    {
        foreach (var detalle in detalles)
        {
            if (detalle.EsPendiente) continue;
            if (detalle.PackSize is > 1)
            {
                var subtotal = detalle.Cantidad * detalle.CostoUnitario;
                detalle.Cantidad = detalle.Cantidad * detalle.PackSize.Value;
                detalle.CostoUnitario = subtotal / detalle.Cantidad;
                detalle.Subtotal = subtotal;
            }
        }
    }

    private async Task RevertirImpactoInventario(IEnumerable<DetalleCompra> detalles)
    {
        foreach (var detalle in detalles)
        {
            if (detalle.EsPendiente) continue;
            if (detalle.ProductoId.HasValue)
            {
                var producto = await _context.Productos.FindAsync(detalle.ProductoId.Value);
                if (producto != null)
                {
                    decimal valorTotalActual = producto.StockBodega * producto.CostoPromedio;
                    decimal valorARestar = detalle.Cantidad * detalle.CostoUnitario;
                    int nuevoStock = producto.StockBodega - detalle.Cantidad;

                    if (nuevoStock > 0)
                    {
                        // Nueva valoración = (Valor Total - Valor de esta compra) / Nuevo Stock
                        producto.CostoPromedio = Math.Max(0, (valorTotalActual - valorARestar) / nuevoStock);
                        producto.StockBodega = nuevoStock;
                    }
                    else
                    {
                        producto.StockBodega = 0;
                        producto.CostoPromedio = 0;
                    }
                    _context.Productos.Update(producto);
                }
            }
        }
    }

    public async Task<string> SaveFacturaImagenAsync(int compraId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("No se proporcionó ningún archivo.");

        const long maxSize = 5 * 1024 * 1024; // 5MB
        if (file.Length > maxSize)
            throw new ArgumentException("El archivo excede el límite de 5MB.");

        var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExt.Contains(ext))
            throw new ArgumentException("Formato de archivo no permitido. Use JPG, PNG o PDF.");

        var compra = await _context.Compras.FindAsync(compraId);
        if (compra == null)
            throw new KeyNotFoundException($"Compra {compraId} no encontrada.");

        // Usar ruta de upload configurada si está seteada, sinon fallback a wwwroot
        var basePath = _uploadPathProvider.GetUploadBasePath();

        var uploadDir = Path.Combine(basePath, "uploads", "compras", "facturas");
        Directory.CreateDirectory(uploadDir);

        // Eliminar imagen anterior si existe
        if (!string.IsNullOrEmpty(compra.FacturaImagenPath))
        {
            var oldPath = Path.Combine(basePath, compra.FacturaImagenPath.TrimStart('/'));
            if (System.IO.File.Exists(oldPath))
                System.IO.File.Delete(oldPath);
        }

        var fileName = $"{Guid.NewGuid()}{ext}";
        var relativePath = $"/uploads/compras/facturas/{fileName}";
        var physicalPath = Path.Combine(uploadDir, fileName);

        await using var stream = new FileStream(physicalPath, FileMode.Create);
        await file.CopyToAsync(stream);

        compra.FacturaImagenPath = relativePath;
        _context.Compras.Update(compra);
        await _context.SaveChangesAsync();

        return relativePath;
    }

    public string ResolveFacturaPhysicalPath(string relativePath)
    {
        var basePath = _uploadPathProvider.GetUploadBasePath();
        return Path.Combine(basePath, relativePath.TrimStart('/'));
    }

    public async Task<IEnumerable<Compra>> GetComprasNoVinculadasAsync(string? proveedor = null, string? numeroDocumento = null)
    {
        var query = _context.Compras
            .Where(c => c.TransferenciaId == null);

        if (!string.IsNullOrWhiteSpace(proveedor))
            query = query.Where(c => c.Proveedor != null && c.Proveedor.Contains(proveedor));

        if (!string.IsNullOrWhiteSpace(numeroDocumento))
            query = query.Where(c => c.NumeroDocumento != null && c.NumeroDocumento.Contains(numeroDocumento));

        return await query
            .OrderByDescending(c => c.FechaCompra)
            .ThenByDescending(c => c.Id)
            .ToListAsync();
    }

    public async Task<ReconstruirCostosResult> ReconstruirProductoCostosAsync()
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Delete all ProductoCosto
            var allCostos = await _context.ProductoCostos.ToListAsync();
            _context.ProductoCostos.RemoveRange(allCostos);

            // 2. Reset all Producto to zero
            var allProductos = await _context.Productos.ToListAsync();
            foreach (var p in allProductos)
            {
                p.StockBodega = 0;
                p.CostoPromedio = 0;
            }

            // 3. Load Compras ordered by date ASC
            var compras = await _context.Compras
                .Include(c => c.Detalles)
                .OrderBy(c => c.FechaCompra)
                .ToListAsync();

            int registrosCreados = 0;
            int detallesSinProducto = 0;
            var productosAfectados = new HashSet<int>();

            // 4. Replay each compra
            foreach (var compra in compras)
            {
                foreach (var detalle in compra.Detalles)
                {
                    if (detalle.EsPendiente) continue;
                    if (detalle.ProductoId.HasValue)
                    {
                        var producto = allProductos.First(p => p.Id == detalle.ProductoId.Value);
                        productosAfectados.Add(producto.Id);

                        // CPP recalculation
                        decimal valorActual = producto.StockBodega * producto.CostoPromedio;
                        decimal valorNuevo = detalle.Cantidad * detalle.CostoUnitario;
                        int nuevoStock = producto.StockBodega + detalle.Cantidad;

                        if (nuevoStock > 0)
                            producto.CostoPromedio = (valorActual + valorNuevo) / nuevoStock;
                        producto.StockBodega = nuevoStock;

                        // Close open ProductoCosto for this product
                        var openRow = _context.ProductoCostos.Local
                            .FirstOrDefault(pc => pc.ProductoId == producto.Id && pc.FechaHasta == null);
                        if (openRow != null)
                            openRow.FechaHasta = compra.FechaCompra;

                        // Create new ProductoCosto
                        _context.ProductoCostos.Add(new ProductoCosto
                        {
                            ProductoId = producto.Id,
                            Costo = detalle.CostoUnitario,
                            FechaDesde = compra.FechaCompra,
                            FechaHasta = null
                        });
                        registrosCreados++;
                    }
                    else
                    {
                        detallesSinProducto++;
                    }
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new ReconstruirCostosResult
            {
                ProductosProcesados = productosAfectados.Count,
                ComprasReprocesadas = compras.Count,
                RegistrosProductoCostoCreados = registrosCreados,
                DetallesSinProducto = detallesSinProducto
            };
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Resuelve la categoría del movimiento de caja basado en tipo de factura y subcategoría.
    /// Prioriza SubcategoriaGasto explícita; si no hay, intenta inferir del proveedor.
    /// </summary>
    private static string ResolverCategoriaMovimiento(Compra compra)
    {
        if (compra.TipoFactura == "MERCADERIA")
            return "MERCADERIA";

        // Subcategoría explícita (elegida por el usuario en el UI)
        if (!string.IsNullOrWhiteSpace(compra.SubcategoriaGasto))
            return compra.SubcategoriaGasto;

        // Inferir del proveedor
        var proveedor = (compra.Proveedor ?? "").ToLowerInvariant();

        if (proveedor.Contains("bencina") || proveedor.Contains("copec") || proveedor.Contains("shell") ||
            proveedor.Contains("petrobras") || proveedor.Contains("petro") || proveedor.Contains("gasolin"))
            return "LOGISTICA";

        if (proveedor.Contains("peaje") || proveedor.Contains("autopista") || proveedor.Contains("tag") ||
            proveedor.Contains("costanera") || proveedor.Contains("vespucio"))
            return "PEAJES";

        return "GASTOS GENERALES";
    }
}
