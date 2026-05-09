using System;
using System.IO;
using System.Threading.Tasks;
using VendingManager.Infrastructure.Data;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Services
{
    public class SyncOrchestratorService(
            ApplicationDbContext context,
            IScraperClient scraperClient,
            ISalesImportService salesImportService) : ISyncOrchestratorService
    {
        public async Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null)
        {
            try
            {
                // Siempre modo global (ALT usa todas las máquinas)
                string targetMachineId = "";

                Console.WriteLine("[Sync ALT] Usando scraper alternativo (inglés, todas las máquinas, orden corregido)");

                // 1. Configurar fechas (máx 32 días para el ALT)
                var today = DateTime.Now;
                var startDate = new DateTime(today.Year, today.Month, 1);
                var endDate = today;

                if (fechaLimite.HasValue)
                {
                    startDate = new DateTime(fechaLimite.Value.Year, fechaLimite.Value.Month, 1);
                    endDate = fechaLimite.Value;
                }

                // Limitar a 32 días
                if ((endDate - startDate).TotalDays > 32)
                {
                    startDate = endDate.AddDays(-32);
                    Console.WriteLine($"[Sync ALT] Rango limitado a 32 días: {startDate:yyyy-MM-dd} a {endDate:yyyy-MM-dd}");
                }

                Console.WriteLine($"[Sync ALT] Solicitando reporte (ALT) para fechas: {startDate:yyyy-MM-dd} a {endDate:yyyy-MM-dd}");

                // 2. Llamar al Scraper ALT
                var result = await scraperClient.DownloadReportAsync(targetMachineId, startDate, endDate);

                // 3. Procesar el Stream
                Console.WriteLine($"[Sync ALT] Respuesta recibida. Procesando stream...");

                using (var ms = new MemoryStream())
                {
                    await result.FileStream.CopyToAsync(ms);
                    ms.Position = 0;
                    string stats = await salesImportService.ImportarVentasMaquina(ms, result.FileName, fechaLimite, "");
                    return $"Sincronización ALT Exitosa. {stats}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync ALT] Error Crítico: {ex.Message}");
                return $"Error ALT: {ex.Message}";
            }
        }
    }
}