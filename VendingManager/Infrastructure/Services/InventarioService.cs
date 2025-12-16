using Microsoft.EntityFrameworkCore;
using System.IO;

namespace VendingManager.Infrastructure.Services
{
    public class InventarioService : IInventarioService
    {
        private readonly ApplicationDbContext _context;
        private readonly IExcelService _excelService;

        public InventarioService(ApplicationDbContext context, IExcelService excelService)
        {
            _context = context;
            _excelService = excelService;
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

        public async Task UpdateProductoAsync(int id, Producto producto)
        {
            // Note: In Controller, it checks id != producto.Id. Here we assume validation is done or we check it.
            _context.Entry(producto).State = EntityState.Modified;
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

        public async Task AjustarStockAsync(int productoId, int nuevoStock)
        {
            var item = await _context.Productos.FindAsync(productoId);
            if (item != null)
            {
                item.StockBodega = nuevoStock;
                await _context.SaveChangesAsync();
            }
        }

        public async Task ImportarCatalogoAsync(Stream stream, string fileName)
        {
            await _excelService.ImportarCatalogoProductos(stream, fileName);
        }
    }
}
