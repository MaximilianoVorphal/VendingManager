using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface IInventarioService
    {
        Task<IEnumerable<Producto>> GetProductosAsync();
        Task<Producto?> GetProductoAsync(int id);
        Task<Producto> CreateProductoAsync(Producto producto);
        Task UpdateProductoAsync(int id, Producto producto);
        Task DeleteProductoAsync(int id);
        Task AjustarStockAsync(int productoId, int nuevoStock);
        Task ImportarCatalogoAsync(Stream stream, string fileName);
    }
}
