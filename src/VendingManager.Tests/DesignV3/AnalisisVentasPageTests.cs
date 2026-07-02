using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Pages;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.DesignV3;

// Cubre la página Análisis de Productos migrada al diseño v3 "Industrial Terminal"
// (port fiel del template de Claude Design, cableado al backend real).
public class AnalisisVentasPageTests : TestContext
{
    private readonly AnalisisMockHttpMessageHandler _mockHandler;

    public AnalisisVentasPageTests()
    {
        _mockHandler = new AnalisisMockHttpMessageHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        Services.AddScoped<IPlantillaService, MockPlantillaService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void CommandBar_RendersUnidadAndPlantillaSelects()
    {
        var cut = RenderComponent<AnalisisProductos>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Análisis de Productos");
            cut.Markup.Should().Contain("var(--ink-900)");
        });

        var selects = cut.FindComponents<VendingManager.Web.Shared.VmSelect>();
        selects.Count.Should().BeGreaterThanOrEqualTo(2);
        selects.Any(s => s.Instance.Label == "Unidad").Should().BeTrue();
        selects.Any(s => s.Instance.Label == "Plantilla").Should().BeTrue();
    }

    [Fact]
    public void PlantillaSelector_RendersPopulatedFromService()
    {
        var cut = RenderComponent<AnalisisProductos>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("PLANTILLA ESTÁNDAR");
            cut.Markup.Should().Contain("PLANTILLA PREMIUM");
        });
    }

    [Fact]
    public void RankingTable_UsesIndustrialPattern_WithSignalColors()
    {
        var cut = RenderComponent<AnalisisProductos>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ranking de Productos"));

        cut.Markup.Should().Contain("var(--signal-success)");
        cut.Markup.Should().Contain("var(--signal-warning)");
        cut.Markup.Should().Contain("var(--signal-danger)");
    }

    [Fact]
    public void SelectingPlantilla_NarrowsMachineSelector()
    {
        var cut = RenderComponent<AnalisisProductos>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("PLANTILLA ESTÁNDAR"));

        var selects = cut.FindComponents<VendingManager.Web.Shared.VmSelect>();
        var plantillaSelect = selects.Single(s => s.Instance.Label == "Plantilla");

        var optionsBefore = CountOptionsForSelect(cut, "Unidad");

        plantillaSelect.Find("select").Change("1");

        cut.WaitForAssertion(() =>
        {
            _mockHandler.LastUrl.Should().NotBeNull();
            _mockHandler.LastUrl!.Should().Contain("analisis-productos");
            _mockHandler.LastUrl!.Should().Contain("maquinaId=0");
        });

        var optionsAfter = CountOptionsForSelect(cut, "Unidad");

        optionsAfter.Should().BeLessThan(optionsBefore);
    }

    private static int CountOptionsForSelect(IRenderedComponent<AnalisisProductos> cut, string labelText)
    {
        var label = cut.FindAll("label").First(l => l.TextContent.Contains(labelText));
        var id = label.GetAttribute("for")!;
        return cut.FindAll($"[id='{id}'] option").Count;
    }

    private class AnalisisMockHttpMessageHandler : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            LastUrl = url;

            string json;

            if (url.Contains("lista-maquinas"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Máquina Estándar 1" },
                    new { Id = 2, Nombre = "Máquina Estándar 2" },
                    new { Id = 3, Nombre = "Máquina Premium 1" },
                    new { Id = 4, Nombre = "Máquina Compacta 1" },
                    new { Id = 5, Nombre = "Máquina Oficina 1" }
                });
            }
            else if (url.Contains("analisis-productos"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        ProductoId = 1,
                        Nombre = "Producto A",
                        Codigo = "A001",
                        Categoria = "Bebidas",
                        CantidadVendida = 40,
                        TotalVentas = 40000m,
                        TotalGanancia = 8000m,
                        RotacionDiaria = 4m,
                        Clasificacion = "Normal",
                        ClasificacionABC = "C",
                        PorcentajeAcumulado = (decimal?)95.5m,
                        Tendencia = "—",
                        CambioPorcentual = (decimal?)null
                    },
                    new
                    {
                        ProductoId = 2,
                        Nombre = "Producto B",
                        Codigo = "B002",
                        Categoria = "Snacks",
                        CantidadVendida = 100,
                        TotalVentas = 100000m,
                        TotalGanancia = 50000m,
                        RotacionDiaria = 10m,
                        Clasificacion = "Estrella",
                        ClasificacionABC = "A",
                        PorcentajeAcumulado = (decimal?)15.0m,
                        Tendencia = "▲",
                        CambioPorcentual = (decimal?)12.5m
                    }
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
