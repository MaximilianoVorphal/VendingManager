using Microsoft.EntityFrameworkCore;
using System.IO;

namespace VendingManager.Infrastructure.Services
{
    public class InventarioService : IInventarioService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICatalogExcelService _catalogService;

        public InventarioService(ApplicationDbContext context, ICatalogExcelService catalogService)
        {
            _context = context;
            _catalogService = catalogService;
        }

        public async Task<IEnumerable<Producto>> GetProductosAsync()
        {
            return await _context.Productos.OrderBy(p => p.Nombre).ToListAsync();
        }

        public async Task<Producto?> GetProductoAsync(int id)
        {
            return await _context.Productos.FindAsync(id);
        }

        public async Task<Producto> CreateProductoAsync(Producto producto)
        {
            _context.Productos.Add(producto);
            await _context.SaveChangesAsync();
            return producto;
        }

        public async Task UpdateProductoAsync(int id, Producto producto, DateTime? recalculateFrom = null, DateTime? recalculateTo = null)
        {
            // Buscar la entidad ya trackeada para evitar conflicto de instancias duplicadas
            var tracked = await _context.Productos.FindAsync(id);
            if (tracked == null)
                throw new KeyNotFoundException($"Producto {id} no encontrado.");

            // Pisar valores desde el DTO sin cambiar la instancia trackeada
            _context.Entry(tracked).CurrentValues.SetValues(producto);

            if (recalculateFrom.HasValue)
            {
                var query = _context.Ventas
                    .Where(v => v.ProductoId == id && v.FechaLocal >= recalculateFrom.Value);

                if (recalculateTo.HasValue)
                {
                    query = query.Where(v => v.FechaLocal <= recalculateTo.Value);
                }

                var ventasAfectadas = await query.ToListAsync();

                foreach (var venta in ventasAfectadas)
                {
                    venta.CostoVenta = tracked.CostoPromedio;
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task DeleteProductoAsync(int id)
        {
            var producto = await _context.Productos.FindAsync(id);
            if (producto != null)
            {
                _context.Productos.Remove(producto);
                await _context.SaveChangesAsync();
            }
        }

        public Task AjustarStockAsync(int productoId, int nuevoStock)
        {
            throw new InvalidOperationException("El ajuste manual de stock no está permitido. Utilice el Módulo de Compras para ingresos y Mermas (desde Caja) para salidas/ajustes.");
        }

        public async Task<string> ImportarCatalogoAsync(Stream stream, string fileName)
        {
            return await _catalogService.ImportarCatalogoProductos(stream, fileName);
        }

        public async Task<byte[]> ExportarCatalogoAsync()
        {
            return await _catalogService.ExportarCatalogoProductos();
        }

        public async Task<IEnumerable<Shared.DTOs.HistorialCostoViewDto>> GetHistorialCostosAsync(int productoId)
        {
            var historico = await _context.DetallesCompra
                .Join(_context.Compras, d => d.CompraId, c => c.Id, (detalle, compra) => new { detalle, compra })
                .Where(x => x.detalle.ProductoId == productoId)
                .OrderBy(x => x.compra.FechaCompra)
                .Select(x => new Shared.DTOs.HistorialCostoViewDto
                {
                    Fecha = x.compra.FechaCompra,
                    CostoUnitario = x.detalle.CostoUnitario,
                    Origen = x.compra.Proveedor
                })
                .ToListAsync();

            return historico;
        }
    }
}

