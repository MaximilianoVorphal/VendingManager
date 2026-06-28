using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Pages;
using Xunit;

using InputFileContent = Bunit.InputFileContent;

namespace VendingManager.Tests.DesignV3;

public class HomePageTests : TestContext
{
    private readonly HomeMockHttpMessageHandler _mockHandler;

    public HomePageTests()
    {
        _mockHandler = new HomeMockHttpMessageHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_RendersThreeVmKpiCards()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Reporte Diario");
            cut.Markup.Should().Contain("Reporte Semanal");
            cut.Markup.Should().Contain("Reporte Mensual");
        });

        // VmKpiCard is built on VmCard, whose container uses the industrial ink border.
        cut.Markup.Should().Contain("border:2px solid var(--ink-900)");
    }

    [Fact]
    public void Home_CriticalStockAlarm_UsesDangerVariant()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("ALERTA DE STOCK CRÍTICO"));

        cut.Markup.Should().Contain("var(--signal-danger)");
    }

    [Fact]
    public void Home_UploadModal_UsesVmInputAndVmButton()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() => cut.FindAll("input[type=\"file\"]").Count.Should().Be(2));

        var file = InputFileContent.CreateFromText("dummy", "ventas.xls");
        var inputFile = cut.FindComponent<InputFile>();
        inputFile.UploadFiles(file);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("input[type=\"datetime-local\"]").Count.Should().Be(1);
            var buttons = cut.FindAll("button");
            buttons.Any(b => b.TextContent.Contains("SUBIR AHORA")).Should().BeTrue();
            buttons.Any(b => b.TextContent.Contains("CANCELAR")).Should().BeTrue();
        });
    }

    private class HomeMockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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
