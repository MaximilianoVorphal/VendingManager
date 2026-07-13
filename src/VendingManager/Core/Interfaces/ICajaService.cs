using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface ICajaService
    {
        Task<CajaResumenDto> GetResumenAsync(int month, int year, int? maquinaId = null);
        Task<List<MovimientoCaja>> GetMovimientosAsync(int month, int year);
        Task RegistrarMovimientoAsync(MovimientoCaja mov);
        Task<(byte[] content, string fileName)> ExportarCajaAsync(int month, int year);
        Task<(byte[] content, string fileName)> ExportarMovimientosAsync(int month, int year);
        Task<string> UploadComprobanteAsync(Stream fileStream, string fileName, string? webRootPath, string? category = null);
        bool IsMonthLocked(int month, int year);
        Task<ValorizacionStockDto> GetValorizacionStockAsync();
        Task<List<MovimientoCaja>> GetGastosNoVinculadosAsync(DateTime? fechaDesde = null, DateTime? fechaHasta = null);
    }
}
