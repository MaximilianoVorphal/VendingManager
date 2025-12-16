using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VendingManager.Web;
using Microsoft.Extensions.Logging;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
// HeadOutlet is now managed by Server's App.razor


// Usa HTTPS para desarrollo seguro
// Usa la dirección base del entorno (funciona para Docker y local)
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Habilitar logging para ver excepciones en la consola del navegador
builder.Logging.SetMinimumLevel(LogLevel.Debug);

await builder.Build().RunAsync();
