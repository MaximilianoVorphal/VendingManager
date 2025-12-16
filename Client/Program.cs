using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VendingManager.Web;
using Microsoft.Extensions.Logging;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Reemplaza con TU puerto real (el que copiaste)
string urlApi = "https://localhost:7089";

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(urlApi) });

// Habilitar logging para ver excepciones en la consola del navegador
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Manejadores globales para excepciones no observadas
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    Console.Error.WriteLine("[UnhandledException] " + (e.ExceptionObject?.ToString() ?? "null"));
};

TaskScheduler.UnobservedTaskException += (s, e) =>
{
    Console.Error.WriteLine("[UnobservedTaskException] " + (e.Exception?.ToString() ?? "null"));
    e.SetObserved();
};

try
{
    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine("[RunAsync error] " + ex);
    throw;
}