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
                // 0. Validar Input (Permitir 0 para Global)
                string targetMachineId = "";

                if (maquinaId > 0)
                {
                    var maquina = await context.Maquinas.FindAsync(maquinaId);
                    if (maquina == null) return $"Error: Máquina con ID {maquinaId} no encontrada.";
                    if (string.IsNullOrEmpty(maquina.IdInternoMaquina)) return $"Error: La máquina '{maquina.Nombre}' no tiene ID Interno configurado.";
                    targetMachineId = maquina.IdInternoMaquina;
                }
                else
                {
                    Console.WriteLine("[Sync] Modo Global: Solicitando TODAS las máquinas.");
                }

                // 1. Configurar fechas
                var today = DateTime.Now;
                var startDate = new DateTime(today.Year, today.Month, 1);
                var endDate = today.AddDays(1);

                // SI HAY FECHA LÍMITE (USUARIO), USAR EL MES DE ESA FECHA
                if (fechaLimite.HasValue)
                {
                    startDate = new DateTime(fechaLimite.Value.Year, fechaLimite.Value.Month, 1);
                    endDate = new DateTime(fechaLimite.Value.Year, fechaLimite.Value.Month,
                                           DateTime.DaysInMonth(fechaLimite.Value.Year, fechaLimite.Value.Month));

                    Console.WriteLine($"[Sync] Ajustando rango de descarga por fecha límite: {startDate:yyyy-MM-dd} a {endDate:yyyy-MM-dd}");
                }

                Console.WriteLine($"[Sync] Solicitando reporte para ID: '{targetMachineId}'...");

                // 2. Llamar al Scraper (vía Client)
                var result = await scraperClient.DownloadReportAsync(targetMachineId, startDate, endDate);

                // 3. Procesar el Stream
                Console.WriteLine($"[Sync] Respuesta recibida. Procesando stream...");

                using (var ms = new MemoryStream())
                {
                    await result.FileStream.CopyToAsync(ms);
                    ms.Position = 0;
                    string stats = await salesImportService.ImportarVentasMaquina(ms, result.FileName, fechaLimite, targetMachineId);
                    return $"Sincronización Exitosa. {stats}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] Error Crítico: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> SincronizarDesdePortalAlt(int maquinaId, DateTime? fechaLimite = null)
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
                var result = await scraperClient.DownloadReportAltAsync(targetMachineId, startDate, endDate);

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