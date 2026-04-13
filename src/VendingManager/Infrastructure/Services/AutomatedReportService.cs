using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection; // Necesario para crear scopes

namespace VendingManager.Infrastructure.Services
{
    public class AutomatedReportService : BackgroundService
    {
        private readonly ILogger<AutomatedReportService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider; // Para obtener ExcelService scoped

        // Hora programada: 23:00 (11 PM)
        private readonly TimeSpan _scheduledTime = new TimeSpan(23, 0, 0);

        public AutomatedReportService(
            ILogger<AutomatedReportService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de Reportes AutomÃ¡ticos: INICIADO.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Calcular cuÃ¡nto falta para la prÃ³xima ejecuciÃ³n
                var now = DateTime.Now;
                var nextRun = now.Date.Add(_scheduledTime);
                if (now > nextRun)
                {
                    nextRun = nextRun.AddDays(1);
                }

                var delay = nextRun - now;
                _logger.LogInformation($"PrÃ³xima descarga programada en: {delay.TotalHours:F1} horas ({nextRun})");

                // Esperar hasta la hora programada
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                // EJECUTAR TAREA
                await RunDownloadProcessAsync();
            }
        }

        private async Task RunDownloadProcessAsync()
        {
            _logger.LogInformation("Iniciando proceso de sincronizaciÃ³n automÃ¡tica...");

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var syncService = scope.ServiceProvider.GetRequiredService<ISyncOrchestratorService>();
                    var ventasService = scope.ServiceProvider.GetRequiredService<IVentasService>(); // Necesario para obtener lista

                    // Sincronizamos TODAS las mÃ¡quinas una por una
                    var maquinas = await ventasService.GetMaquinasAsync();
                    foreach (var m in maquinas)
                    {
                        try
                        {
                            var resultado = await syncService.SincronizarDesdePortal(m.Id);
                            _logger.LogInformation($"[AutoSync] MÃ¡quina '{m.Nombre}': {resultado}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[AutoSync] Error en MÃ¡quina '{m.Nombre}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"ExcepciÃ³n crÃ­tica en tarea automÃ¡tica: {ex.Message}");
            }
        }
    }
}

