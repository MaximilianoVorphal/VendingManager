using System.IO;

namespace VendingManager.Core.Interfaces
{
    public interface IVentasService
    {
        Task<List<MaquinaSimpleDto>> GetMaquinasAsync();
        Task<DashboardStats> GetDashboardStatsAsync(int maquinaId);
        Task<ReporteDto> GetReporteRangoAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false);
        Task<InformeFinancieroDto> GetInformeFinancieroAsync(DateTime inicio, DateTime fin, int maquinaId);
        Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false);
        Task FixDatesAsync();
        Task<string> ImportarVentasMaquinaAsync(Stream stream, string fileName, DateTime? fechaLimite = null);
        Task ImportarTransbankAsync(Stream stream, string fileName, DateTime? fechaLimite = null);
        Task<List<StockCriticoDto>> GetStockCriticoAsync(int maquinaId);
        Task RecalcularCostosHistoricosAsync();
        Task<List<AnalisisProductoDto>> GetAnalisisProductosAsync(DateTime inicio, DateTime fin, int maquinaId);
    }
}
