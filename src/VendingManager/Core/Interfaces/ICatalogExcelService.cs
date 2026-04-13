using System.IO;
using System.Threading.Tasks;

namespace VendingManager.Core.Interfaces
{
    public interface ICatalogExcelService
    {
        Task<string> ImportarCatalogoProductos(Stream fileStream, string nombreArchivo);
        Task<byte[]> ExportarCatalogoProductos();
    }
}
