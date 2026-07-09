using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Pages;
using Xunit;

namespace VendingManager.Tests.Pages;

public class HomeMobileWrapperTests : TestContext
{
    private readonly HomeMockHandler _mockHandler;

    public HomeMobileWrapperTests()
    {
        _mockHandler = new HomeMockHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new System.Uri("http://localhost")
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_RendersWithMobileShellWrapper()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() =>
        {
            var shell = cut.Find(".vm-mobile-shell");
            shell.Should().NotBeNull("Home page should be wrapped in MobileShell");
        });
    }

    [Fact]
    public void Home_MobileShellContainsOriginalContent()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("industrial-wrapper",
                "Original industrial wrapper should be inside MobileShell");
        });
    }

    private class HomeMockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            string json;

            if (url.Contains("lista-maquinas"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Máquina 001" },
                    new { Id = 2, Nombre = "Máquina 002" }
                });
            }
            else if (url.Contains("machine-status"))
            {
                json = JsonSerializer.Serialize(new
                {
                    machines = new[]
                    {
                        new { machine_id = "2410280012", name = "Máquina 001", status = "online" },
                        new { machine_id = "2410280047", name = "Máquina 002", status = "online" }
                    }
                });
            }
            else if (url.Contains("dashboard-stats"))
            {
                json = JsonSerializer.Serialize(new
                {
                    Hoy = new { VentaTotal = 100000m, PagadoTB = 80000m, Pendiente = 20000m, CantidadVentas = 10 },
                    Semana = new { VentaTotal = 700000m, PagadoTB = 600000m, Pendiente = 100000m, CantidadVentas = 70 },
                    Mes = new { VentaTotal = 3000000m, PagadoTB = 2500000m, Pendiente = 500000m, CantidadVentas = 300 },
                    CantidadStockCritico = 5
                });
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
