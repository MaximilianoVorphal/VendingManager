using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Clients;
using VendingManager.Shared.DTOs;

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
                var utcNow = DateTime.UtcNow;
                var today = TimeZoneInfo.ConvertTimeFromUtc(utcNow, PollScheduler.ChileTimeZone);
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

        public async Task<string> SincronizarDesdePortalApi(int maquinaId, DateTime? fechaLimite = null)
        {
            try
            {
                var utcNow = DateTime.UtcNow;
                var today = TimeZoneInfo.ConvertTimeFromUtc(utcNow, PollScheduler.ChileTimeZone);
                var startDate = new DateTime(today.Year, today.Month, 1);
                var endDate = today;

                if (fechaLimite.HasValue)
                {
                    var fl = fechaLimite.Value;
                    if (fl.TimeOfDay == TimeSpan.Zero)
                    {
                        fl = fl.Date.AddDays(1).AddTicks(-1);
                        fechaLimite = fl;
                    }
                    startDate = new DateTime(fl.Year, fl.Month, 1);
                    endDate = fl;
                }

                if ((endDate - startDate).TotalDays > 32)
                {
                    startDate = endDate.AddDays(-32);
                }

                // Add 1 day to endDate to account for TZ offset (same as existing code)
                var apiEndDate = endDate.AddDays(1);

                Console.WriteLine($"[SyncAPI] Solicitando reporte via API: {startDate:yyyy-MM-dd} a {apiEndDate:yyyy-MM-dd}");

                var report = await scraperClient.GetSalesReportAsync(startDate, apiEndDate, "");

                Console.WriteLine($"[SyncAPI] Recibidas {report.Total} filas. Procesando...");

                string stats = await salesImportService.ImportarVentasDesdeJson(report.Rows, fechaLimite, "");

                Console.WriteLine($"[SyncAPI] {stats}");
                return $"Sincronización API Exitosa. {stats}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncAPI] Error: {ex.Message}");
                return $"Error API: {ex.Message}";
            }
        }

        /// <summary>
        /// Sincroniza ventas para una ventana de fechas explícita via API JSON,
        /// retornando un resultado estructurado. NO actualiza LastSyncTracker.
        /// </summary>
        public async Task<SyncResult> SincronizarDesdePortalApi(DateTime desde, DateTime hasta,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var apiEndDate = hasta.AddDays(1);

                SalesReportResponse report;
                try
                {
                    report = await scraperClient.GetSalesReportAsync(desde, apiEndDate, "",
                        cancellationToken);
                }
                catch (WafBlockedException ex)
                {
                    return new SyncResult
                    {
                        Outcome = SyncOutcome.Blocked,
                        Details = ex.BlockReason ?? "WAF blocked"
                    };
                }
                catch (OperationCanceledException)
                {
                    return new SyncResult
                    {
                        Outcome = SyncOutcome.Timeout,
                        Details = "Scraper request timed out or was cancelled"
                    };
                }

                // Classify by the scraper's explicit Status field FIRST (FIX-3).
                // Only fall back to row-count / exception-based classification when
                // Status is null or unrecognised.
                var status = report.Status?.ToLowerInvariant();
                switch (status)
                {
                    case "ok":
                        if (report.Total == 0 && (report.Rows is null || report.Rows.Count == 0))
                        {
                            return new SyncResult
                            {
                                Outcome = SyncOutcome.Empty,
                                Details = "Status 'ok' but zero rows returned"
                            };
                        }
                        break;
                    case "empty":
                        return new SyncResult
                        {
                            Outcome = SyncOutcome.Empty,
                            Details = report.Reason ?? "Scraper reported empty"
                        };
                    case "blocked":
                        return new SyncResult
                        {
                            Outcome = SyncOutcome.Blocked,
                            Details = report.Reason ?? "WAF blocked (scraper status)"
                        };
                    case "error":
                        return new SyncResult
                        {
                            Outcome = SyncOutcome.Error,
                            Details = report.Reason ?? "Scraper reported error"
                        };
                    case "timeout":
                        return new SyncResult
                        {
                            Outcome = SyncOutcome.Timeout,
                            Details = report.Reason ?? "Scraper reported timeout"
                        };
                    // Fall through to row-count classification for "ok" and unknown/null.
                }

                if (report.Total == 0 && (report.Rows is null || report.Rows.Count == 0))
                {
                    return new SyncResult
                    {
                        Outcome = SyncOutcome.Empty,
                        Details = "No new sales rows returned by scraper"
                    };
                }

                string stats = await salesImportService.ImportarVentasDesdeJson(report.Rows, hasta, "");
                return new SyncResult
                {
                    Outcome = SyncOutcome.Ok,
                    Stats = stats,
                    Details = $"Recibidas {report.Total} filas desde {desde:yyyy-MM-dd}"
                };
            }
            catch (Exception ex)
            {
                return new SyncResult
                {
                    Outcome = SyncOutcome.Error,
                    Details = $"Sync error: {ex.Message}"
                };
            }
        }
    }
}