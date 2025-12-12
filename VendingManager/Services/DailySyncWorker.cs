using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VendingManager.Services
{
    public class DailySyncWorker : BackgroundService
    {
        private readonly IServiceProvider _services;

        public DailySyncWorker(IServiceProvider services)
        {
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("⏰ WORKER: Iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Esperar 8 AM (Lógica simplificada para probar ahora: espera 1 minuto y ejecuta)
                // Para producción, usa la lógica de horas que te di antes.
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                await EjecutarSincronizacion();

                // Dormir 24 horas después de ejecutar
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task EjecutarSincronizacion()
        {
            using (var scope = _services.CreateScope())
            {
                try
                {
                    var bot = scope.ServiceProvider.GetRequiredService<SeleniumSyncService>();
                    var excel = scope.ServiceProvider.GetRequiredService<ExcelService>();

                    // Definir AYER
                    var fechaAyer = DateTime.Now.AddDays(-1);

                    Console.WriteLine("🚀 WORKER: Ejecutando Selenium...");

                    // Usamos 'using' para asegurar que el navegador se cierre
                    using (bot)
                    {
                        // Ya no llamamos a Login() explícitamente, DescargarReporte lo hace.
                        var stream = await bot.DescargarReporte(fechaAyer, fechaAyer);

                        if (stream != null)
                        {
                            // CORREGIDO: Pasamos los 4 argumentos (Stream, Nombre, Inicio, Fin)
                            await excel.ImportarVentasMaquina(stream, "auto.xls");
                            Console.WriteLine("✅ WORKER: Éxito.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🔥 WORKER ERROR: {ex.Message}");
                }
            }
        }
    }
}