using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface IVentasService
    {
        Task<List<MaquinaSimpleDto>> GetMaquinasAsync();
        Task<DashboardStats> GetDashboardStatsAsync(int maquinaId);
        Task<ReporteDto> GetReporteRangoAsync(DateTime inicio, DateTime fin, int maquinaId);
        Task<InformeFinancieroDto> GetInformeFinancieroAsync(DateTime inicio, DateTime fin, int maquinaId);
        Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId);
        Task FixDatesAsync();
        Task ImportarVentasMaquinaAsync(Stream stream, string fileName);
        Task ImportarTransbankAsync(Stream stream, string fileName);
    }
}
