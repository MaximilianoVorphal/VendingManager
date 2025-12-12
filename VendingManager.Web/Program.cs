using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VendingManager.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Reemplaza con TU puerto real (el que copiaste)
string urlApi = "https://localhost:7089";

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(urlApi) });

await builder.Build().RunAsync();
