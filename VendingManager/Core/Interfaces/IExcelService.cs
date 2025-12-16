using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface IExcelService
    {
        Task ImportarVentasMaquina(Stream fileStream, string nombreArchivo);
        Task ImportarTransbank(Stream fileStream, string nombreArchivo);
        Task ImportarCatalogoProductos(Stream fileStream, string nombreArchivo);
    }
}
