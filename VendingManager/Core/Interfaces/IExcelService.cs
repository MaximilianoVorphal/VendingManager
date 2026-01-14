using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface IExcelService
    {
        Task<string> ImportarVentasMaquina(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null);
        Task ImportarTransbank(Stream fileStream, string nombreArchivo, DateTime? fechaLimite = null);
        Task<string> ImportarCatalogoProductos(Stream fileStream, string nombreArchivo);
        Task<byte[]> ExportarCatalogoProductos();
        Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null);
        Task<byte[]> ExportarListaCarga(List<VendingManager.Core.DTOs.StockCriticoDto> items);
    }
}
