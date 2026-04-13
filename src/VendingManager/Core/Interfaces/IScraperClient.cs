namespace VendingManager.Core.Interfaces
{
    public interface IScraperClient
    {
        /// <summary>
        /// Solicitamos al scraper que descargue el reporte y nos devuelva el archivo como Stream.
        /// </summary>
        /// <param name="machineId">ID interno de la máquina (o vacío para global)</param>
        /// <param name="startDate">Fecha inicio (yyyy-MM-dd)</param>
        /// <param name="endDate">Fecha fin (yyyy-MM-dd)</param>
        /// <returns>Tupla con el Stream del archivo y el nombre sugerido</returns>
        Task<(Stream FileStream, string FileName)> DownloadReportAsync(string machineId, DateTime startDate, DateTime endDate);
    }
}
