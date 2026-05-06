using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Configuration;
using VendingManager.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace VendingManager.Infrastructure.Services;

public class CompraService : ICompraService
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly IOptions<VendingConfig> _config;

    public CompraService(ApplicationDbContext context, IWebHostEnvironment env, IOptions<VendingConfig> config)
    {
        _context = context;
        _env = env;
        _config = config;
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
        // 1. Recalcular el Costo Promedio y sumar Stock en Bodega (solo si hay producto)
        foreach (var detalle in compra.Detalles)
        {
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

        // 3. Registrar Movimiento en Caja automáticamente si la compra fue pagada
        if (compra.Estado == "PAGADA" && compra.PagadaCaja)
        {
            var movimiento = new MovimientoCaja
            {
                Fecha = compra.FechaCompra,
                Descripcion = $"Factura/Boleta Nº {compra.NumeroDocumento} - {compra.Proveedor}",
                Monto = -compra.MontoTotal, // Gasto de dinero
                Tipo = "GASTO",
                Categoria = compra.TipoFactura == "MERCADERIA" ? "MERCADERIA" : "GASTOS GENERALES",
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
                Categoria = compra.TipoFactura == "MERCADERIA" ? "MERCADERIA" : "GASTOS GENERALES",
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

            // 3. Reemplazar detalles
            _context.DetallesCompra.RemoveRange(compra.Detalles);
            compra.Detalles = request.Detalles.Select(d => new DetalleCompra
            {
                ProductoId = d.ProductoId,
                DescripcionItem = d.DescripcionItem,
                Cantidad = d.Cantidad,
                CostoUnitario = d.CostoUnitario,
                Subtotal = d.Cantidad * d.CostoUnitario
            }).ToList();

            // 4. Aplicar el impacto nuevo
            foreach (var detalle in compra.Detalles)
            {
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
            
            compra.MontoTotal = compra.Detalles.Sum(d => d.Subtotal);

            // 5. Sincronizar Movimiento de Caja
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
                movimiento.Categoria = compra.TipoFactura == "MERCADERIA" ? "MERCADERIA" : "GASTOS GENERALES";
            }
            else if (movimiento != null)
            {
                _context.MovimientosCaja.Remove(movimiento);
            }

            await _context.SaveChangesAsync();
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

    private async Task RevertirImpactoInventario(IEnumerable<DetalleCompra> detalles)
    {
        foreach (var detalle in detalles)
        {
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
        var basePath = GetUploadBasePath();

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

    /// <summary>
    /// Resuelve la ruta base para uploads. Usa FacturaUploadPath del config si está seteado,
    /// sino fallback a WebRootPath (o ContentRootPath/wwwroot).
    /// </summary>
    private string GetUploadBasePath()
    {
        // 1. Config explícita (para Docker/producción)
        var configuredPath = _config.Value.FacturaUploadPath;
        if (!string.IsNullOrEmpty(configuredPath))
            return configuredPath;

        // 2. Fallback: WebRootPath (cuando wwwroot existe)
        var webRoot = _env.WebRootPath;
        if (!string.IsNullOrEmpty(webRoot))
            return webRoot;

        // 3. Último fallback: ContentRootPath/wwwroot
        webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(webRoot);
        return webRoot;
    }

    public string ResolveFacturaPhysicalPath(string relativePath)
    {
        var basePath = GetUploadBasePath();
        return Path.Combine(basePath, relativePath.TrimStart('/'));
    }
}
