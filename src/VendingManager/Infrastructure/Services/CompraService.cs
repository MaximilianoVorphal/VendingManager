using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

public class CompraService : ICompraService
{
    private readonly ApplicationDbContext _context;

    public CompraService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Compra>> GetComprasAsync(int? count = null)
    {
        var query = _context.Compras
            .Include(c => c.Detalles)
            .ThenInclude(d => d.Producto)
            .OrderByDescending(c => c.FechaCompra)
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
        // 1. Recalcular el Costo Promedio y sumar Stock en Bodega
        foreach (var detalle in compra.Detalles)
        {
            var producto = await _context.Productos.FindAsync(detalle.ProductoId);
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
                Categoria = "MERCADERIA",
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
                Categoria = "MERCADERIA",
                CompraId = compra.Id // FK para trazabilidad
            };
            _context.MovimientosCaja.Add(movimiento);
            
            _context.Compras.Update(compra);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<Compra> ActualizarCompraAsync(int id, VendingManager.Shared.DTOs.ActualizarCompraRequestDto request)
    {
        var compra = await _context.Compras.FindAsync(id);
        if (compra == null) throw new Exception("Compra no encontrada.");

        compra.Proveedor = request.Proveedor;
        compra.NumeroDocumento = request.NumeroDocumento;
        if (request.FechaCompra != DateTime.MinValue)
        {
            compra.FechaCompra = request.FechaCompra;
        }

        // Actualizar el movimiento de caja asociado usando FK (en vez de búsqueda por texto)
        var movimiento = await _context.MovimientosCaja
            .FirstOrDefaultAsync(m => m.CompraId == id);
                                      
        if (movimiento != null)
        {
            movimiento.Fecha = request.FechaCompra != DateTime.MinValue ? request.FechaCompra : movimiento.Fecha;
            movimiento.Descripcion = $"Factura/Boleta Nº {request.NumeroDocumento} - {request.Proveedor}";
            _context.MovimientosCaja.Update(movimiento);
        }

        _context.Compras.Update(compra);
        await _context.SaveChangesAsync();
        return compra;
    }
}
