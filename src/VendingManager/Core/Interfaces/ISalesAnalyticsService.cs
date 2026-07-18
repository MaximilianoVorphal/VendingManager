using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface ISalesAnalyticsService
    {
        Task<DashboardStats> GetDashboardStatsAsync(int maquinaId);
        Task<ReporteDto> GetReporteRangoAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false, int? templateId = null);
        Task<InformeFinancieroDto> GetInformeFinancieroAsync(DateTime inicio, DateTime fin, int maquinaId);
        Task<(byte[] content, string fileName)> ExportarReporteAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePhantom = false, int? templateId = null);
        Task<List<AnalisisProductoDto>> GetAnalisisProductosAsync(DateTime inicio, DateTime fin, int maquinaId, bool includePendientes = false);
        Task<List<StockoutAnalysisDto>> GetStockoutAnalysisAsync(DateTime inicio, DateTime fin, int maquinaId, double umbralHorasSilencio = 14);
        Task<StockoutDashboardAnalysisDto> GetStockoutDashboardAnalysisV2Async(DateTime inicio, DateTime fin, int maquinaId, double umbralHorasSilencio = 14);
        Task<List<VentaDiariaDto>> GetVentasDiariasAsync(int productoId, int maquinaId, DateTime inicio, DateTime fin);
        Task<List<CategoriaAnalisisDto>> GetCategoriaAnalisisAsync(DateTime inicio, DateTime fin, int maquinaId);
    }
}
