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
            _logger.LogInformation("Servicio de Reportes Automáticos: INICIADO.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Calcular cuánto falta para la próxima ejecución
                var now = DateTime.Now;
                var nextRun = now.Date.Add(_scheduledTime);
                if (now > nextRun)
                {
                    nextRun = nextRun.AddDays(1);
                }

                var delay = nextRun - now;
                _logger.LogInformation($"Próxima descarga programada en: {delay.TotalHours:F1} horas ({nextRun})");

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
            _logger.LogInformation("Iniciando proceso de sincronización automática...");

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var excelService = scope.ServiceProvider.GetRequiredService<IExcelService>();

                    // Delegamos todo el trabajo pesado al servicio compartido
                    var resultado = await excelService.SincronizarDesdePortal();

                    _logger.LogInformation($"Resultado Sincronización Automática: {resultado}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Excepción crítica en tarea automática: {ex.Message}");
            }
        }
    }
}
