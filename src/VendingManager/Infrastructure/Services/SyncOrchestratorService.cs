using System;
using System.IO;
using System.Threading.Tasks;
using VendingManager.Infrastructure.Data;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Services
{
    public class SyncOrchestratorService : ISyncOrchestratorService
    {
        private readonly ApplicationDbContext _context;
        private readonly IScraperClient _scraperClient;
        private readonly ISalesImportService _salesImportService;

        public SyncOrchestratorService(ApplicationDbContext context, IScraperClient scraperClient, ISalesImportService salesImportService)
        {
            _context = context;
            _scraperClient = scraperClient;
            _salesImportService = salesImportService;
        }

        public async Task<string> SincronizarDesdePortal(int maquinaId, DateTime? fechaLimite = null)
        {
            try
            {
                // 0. Validar Input (Permitir 0 para Global)
                string targetMachineId = "";
                
                if (maquinaId > 0)
                {
                    var maquina = await _context.Maquinas.FindAsync(maquinaId);
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

                // 🔥 SI HAY FECHA LÍMITE (USUARIO), USAR EL MES DE ESA FECHA
                if (fechaLimite.HasValue)
                {
                    // Si el usuario pide hasta 31/12/2025, debemos bajar el reporte de Diciembre 2025.
                    startDate = new DateTime(fechaLimite.Value.Year, fechaLimite.Value.Month, 1);
                    // Ourvend solo permite períodos de máximo 32 días, así que usamos el último día del mes
                    endDate = new DateTime(fechaLimite.Value.Year, fechaLimite.Value.Month, 
                                           DateTime.DaysInMonth(fechaLimite.Value.Year, fechaLimite.Value.Month));
                    
                    Console.WriteLine($"[Sync] Ajustando rango de descarga por fecha límite: {startDate:yyyy-MM-dd} a {endDate:yyyy-MM-dd}");
                }

                Console.WriteLine($"[Sync] Solicitando reporte para ID: '{targetMachineId}'...");

                // 2. Llamar al Scraper (vía Client)
                var result = await _scraperClient.DownloadReportAsync(targetMachineId, startDate, endDate);

                // 3. Procesar el Stream
                Console.WriteLine($"[Sync] Respuesta recibida. Procesando stream...");

                // Si el stream no es seekable (net stream), lo copiamos a memoria para seguridad de ExcelDataReader
                using (var ms = new MemoryStream())
                {
                    await result.FileStream.CopyToAsync(ms);
                    ms.Position = 0;
                    string stats = await _salesImportService.ImportarVentasMaquina(ms, result.FileName, fechaLimite, targetMachineId);
                    return $"Sincronización Exitosa. {stats}";
                }

                return "Sincronización Exitosa. Archivo procesado en memoria.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] Error Crítico: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }
    }
}
