using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface ICajaService
    {
        Task<CajaResumenDto> GetResumenAsync(int month, int year);
        Task<List<MovimientoCaja>> GetMovimientosAsync(int month, int year);
        Task RegistrarMovimientoAsync(MovimientoCaja mov);
        Task<(byte[] content, string fileName)> ExportarCajaAsync(int month, int year);
        Task<(byte[] content, string fileName)> ExportarMovimientosAsync(int month, int year);
        Task<string> UploadImageAsync(Stream fileStream, string fileName, string? webRootPath);
        bool IsMonthLocked(int month, int year);
    }
}
