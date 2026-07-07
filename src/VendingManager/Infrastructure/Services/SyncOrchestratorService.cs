using System;
using System.IO;
using System.Threading.Tasks;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Services
{
    public class SyncOrchestratorService(
            IScraperClient scraperClient,
            ISalesImportService salesImportService) : ISyncOrchestratorService
    {
        public async Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null)
        {
            try
            {
                // Siempre modo global (ALT usa todas las máquinas)
                string targetMachineId = "";

                Console.WriteLine("[Sync] Usando scraper alternativo (inglés, todas las máquinas, orden corregido)");

                // 1. Configurar fechas (máx 32 días para el ALT)
                var today = DateTime.Now;
                var startDate = new DateTime(today.Year, today.Month, 1);
                var endDate = today;

                if (fechaLimite.HasValue)
                {
                    // Si es una fecha sin hora (medianoche), extender a fin de día
                    // para que el scraper incluya el día completo y el filtro no descarte ventas
                    var fl = fechaLimite.Value;
                    if (fl.TimeOfDay == TimeSpan.Zero)
                    {
                        fl = fl.Date.AddDays(1).AddTicks(-1);
                        fechaLimite = fl;
                    }
                    startDate = new DateTime(fl.Year, fl.Month, 1);
                    endDate = fl;
                }

                // Limitar a 32 días
                if ((endDate - startDate).TotalDays > 32)
                {
                    startDate = endDate.AddDays(-32);
                    Console.WriteLine($"[Sync] Rango limitado a 32 días: {startDate:yyyy-MM-dd} a {endDate:yyyy-MM-dd}");
                }

                Console.WriteLine($"[Sync] Solicitando reporte (ALT) para fechas: {startDate:yyyy-MM-dd} a {endDate:yyyy-MM-dd}");
                Console.WriteLine($"[Sync] >>> SCRAPER REQUEST: machineId='{targetMachineId}', start='{startDate:yyyy-MM-dd}', end='{endDate.AddDays(1):yyyy-MM-dd}' (fechaLimite usuario + 1 día por desfase 12h TZ máquina↔Chile)");

                // 2. Llamar al Scraper ALT
                // Se le suma 1 día al endDate porque la hora de la máquina está 12h adelantada
                // respecto a la hora servidor/Chile: ventas del día X (hora máquina) caen como día X+1
                // en la Ourvend, por lo que el scraper debe consultar hasta fechaLimite+1 para incluirlas.
                var result = await scraperClient.DownloadReportAsync(targetMachineId, startDate, endDate.AddDays(1));

                // 3. Procesar el Stream
                Console.WriteLine($"[Sync] Respuesta recibida. Procesando stream...");

                using (var ms = new MemoryStream())
                {
                    await result.FileStream.CopyToAsync(ms);
                    ms.Position = 0;
                    Console.WriteLine($"[Sync] >>> SCRAPER RESPONSE: file='{result.FileName}', streamSize={ms.Length} bytes");
                    string stats = await salesImportService.ImportarVentasMaquina(ms, result.FileName, fechaLimite, "");
                    return $"Sincronización ALT Exitosa. {stats}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] Error Crítico: {ex.Message}");
                return $"Error ALT: {ex.Message}";
            }
        }
    }
}