using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface IInventarioService
    {
        Task<IEnumerable<Producto>> GetProductosAsync();
        Task<Producto?> GetProductoAsync(int id);
        Task<Producto> CreateProductoAsync(Producto producto);
        Task UpdateProductoAsync(int id, Producto producto, DateTime? recalculateFrom = null, DateTime? recalculateTo = null);
        Task DeleteProductoAsync(int id);
        Task AjustarStockAsync(int productoId, int nuevoStock);
        Task<string> ImportarCatalogoAsync(Stream stream, string fileName);
        Task<byte[]> ExportarCatalogoAsync();
        Task<IEnumerable<HistorialCostoViewDto>> GetHistorialCostosAsync(int productoId);
    }
}
