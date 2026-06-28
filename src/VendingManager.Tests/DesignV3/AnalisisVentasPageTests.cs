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
    public void FilterPanel_RendersInsideVmCard_WithDarkHeader()
    {
        var cut = RenderComponent<AnalisisVentas>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("FILTROS");
            cut.Markup.Should().Contain("var(--ink-900)");
        });

        var selects = cut.FindComponents<VendingManager.Web.Shared.VmSelect>();
        selects.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void PlantillaSelector_RendersPopulatedFromService()
    {
        var cut = RenderComponent<AnalisisVentas>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("PLANTILLA ESTÁNDAR");
            cut.Markup.Should().Contain("PLANTILLA PREMIUM");
        });

        var selects = cut.FindComponents<VendingManager.Web.Shared.VmSelect>();
        selects.Any(s => s.Instance.Label == "PLANTILLA").Should().BeTrue();
    }

    [Fact]
    public void RankingTable_UsesIndustrialPattern_WithVmBadges()
    {
        var cut = RenderComponent<AnalisisVentas>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ranking de Productos"));

        cut.Markup.Should().Contain("var(--signal-success)");
        cut.Markup.Should().Contain("var(--signal-warning)");
        cut.Markup.Should().Contain("var(--signal-danger)");
    }

    [Fact]
    public void LowStockRows_ApplyTintDanger()
    {
        var cut = RenderComponent<AnalisisVentas>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ranking de Productos"));

        cut.Markup.Should().Contain("var(--tint-danger)");
    }

    [Fact]
    public void SelectingPlantilla_NarrowsMachineSelector()
    {
        var cut = RenderComponent<AnalisisVentas>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("PLANTILLA ESTÁNDAR"));

        var selects = cut.FindComponents<VendingManager.Web.Shared.VmSelect>();
        var plantillaSelect = selects.Single(s => s.Instance.Label == "PLANTILLA");

        var unidadSelectBefore = selects.Single(s => s.Instance.Label == "UNIDAD");
        var optionsBefore = CountOptionsForSelect(cut, unidadSelectBefore.Instance);

        plantillaSelect.Find("select").Change("1");

        cut.WaitForAssertion(() =>
        {
            _mockHandler.LastUrl.Should().NotBeNull();
            _mockHandler.LastUrl!.Should().Contain("plantillaId=1");
        });

        var unidadSelectAfter = cut.FindComponents<VendingManager.Web.Shared.VmSelect>().Single(s => s.Instance.Label == "UNIDAD");
        var optionsAfter = CountOptionsForSelect(cut, unidadSelectAfter.Instance);

        optionsAfter.Should().BeLessThan(optionsBefore);
    }

    private static int CountOptionsForSelect(IRenderedComponent<AnalisisVentas> cut, VendingManager.Web.Shared.VmSelect select)
    {
        var id = (string)select.GetType().GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(select)!;
        return cut.FindAll($"#{id} option").Count;
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
                        CantidadVendida = 0,
                        TotalVentas = 0m,
                        TotalGanancia = 0m,
                        RotacionDiaria = 0m,
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
