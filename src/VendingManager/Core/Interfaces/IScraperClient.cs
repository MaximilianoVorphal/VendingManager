using VendingManager.Shared.DTOs;

namespace VendingManager.Core.Interfaces
{
    public interface IScraperClient
    {
        /// <summary>
        /// Descarga reporte de ventas desde el scraper Ourvend (inglés, global).
        /// </summary>
        /// <param name="machineId">ID interno de la máquina (o vacío para global)</param>
        /// <param name="startDate">Fecha inicio</param>
        /// <param name="endDate">Fecha fin</param>
        /// <returns>Tupla con el Stream del archivo y el nombre sugerido</returns>
        Task<(Stream FileStream, string FileName)> DownloadReportAsync(string machineId, DateTime startDate, DateTime endDate);

        /// <summary>
        /// Obtiene el estado en tiempo real de todas las máquinas desde Ourvend.
        /// </summary>
        Task<MachineStatusResponse> GetMachineStatusAsync();

        /// <summary>
        /// Obtiene reporte de ventas desde Ourvend via API pura (JSON, sin Playwright).
        /// </summary>
        Task<SalesReportResponse> GetSalesReportAsync(DateTime startDate, DateTime endDate, string machineId = "",
            CancellationToken cancellationToken = default);
    }
}