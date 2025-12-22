using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface IExcelService
    {
        Task ImportarVentasMaquina(Stream fileStream, string nombreArchivo);
        Task ImportarTransbank(Stream fileStream, string nombreArchivo);
        Task<string> ImportarCatalogoProductos(Stream fileStream, string nombreArchivo);
        Task<byte[]> ExportarCatalogoProductos();
        Task<string> SincronizarDesdePortal();
    }
}
