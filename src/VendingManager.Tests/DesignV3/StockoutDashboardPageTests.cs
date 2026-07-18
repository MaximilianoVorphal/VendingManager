using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Pages;
using Xunit;

namespace VendingManager.Tests.DesignV3;

// Cubre la página Quiebres de Stock (/stockout-dashboard) migrada al diseño v3
// "Industrial Terminal" y cableada al backend real (adapter sobre el partial ya wired).
public class StockoutDashboardPageTests : TestContext
{
    private readonly StockoutMockHttpMessageHandler _mockHandler;

    public StockoutDashboardPageTests()
    {
        _mockHandler = new StockoutMockHttpMessageHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("tester");
    }

    private void NavigateTo(string relativeUri)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(relativeUri);
    }

    [Fact]
    public void ProductMachineRows_KeepMachinesIndependentAndDoNotShowLossBeforeDepletion()
    {
        _mockHandler.V2AnalyzePayload = ProductMachineBundleJson();
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("COCA COLA");
            cut.Markup.Should().Contain("MÁQUINA NORTE");
            cut.Markup.Should().Contain("MÁQUINA SUR");
            cut.FindAll("tr").Select(row => row.TextContent).Should().Contain(text => text.Contains("6/20"));
            cut.Markup.Should().Contain("Quiebre parcial");
            var northCocaRow = cut.FindAll("tr").Single(row => row.TextContent.Contains("MÁQUINA NORTE") && row.TextContent.Contains("COCA COLA"));
            northCocaRow.TextContent.Should().NotContain("Agotado");
            northCocaRow.TextContent.Should().NotContain("$12.000");
        });
    }

    [Fact]
    public void ProductMachineRows_ShowDepletedAndUnreliableLabelsAndKeepUnreliableSlotsLastInDetail()
    {
        _mockHandler.V2AnalyzePayload = ProductMachineBundleJson();
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("MÁQUINA SUR"));

        cut.Markup.Should().Contain("Agotado");
        cut.Markup.Should().Contain("Datos no confiables");

        cut.FindAll("button").First(button => button.TextContent.Contains("Solo quiebres")).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("Agrupar por producto")).Click();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            markup.IndexOf("SLOT ELEGIBLE", StringComparison.Ordinal)
                .Should().BeLessThan(markup.IndexOf("SLOT NO CONFIABLE", StringComparison.Ordinal));
            markup.Should().Contain("MÁQUINA NORTE · A2");
        });
    }

    [Fact]
    public void ProductMachineKpisAndCache_InvalidationFollowTheCurrentV2Bundle()
    {
        _mockHandler.V2AnalyzePayload = ProductMachineBundleJson();
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("$12.000");
            cut.Markup.Should().Contain("COCA COLA · MÁQUINA SUR");
        });

        cut.Find("input[type=number]").Change("36");

        cut.WaitForAssertion(() => _mockHandler.V2AnalyzeRequestCount.Should().Be(2));
    }

    [Fact]
    public void SelectingProductMachine_FocusesThatMachineInsteadOfWorstLossFallback()
    {
        _mockHandler.V2AnalyzePayload = ProductMachineBundleJson();
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("MÁQUINA SUR"));
        cut.FindAll("select").Last().Change("MÁQUINA NORTE · COCA COLA");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("MÁQ MÁQUINA NORTE"));
    }

    [Fact]
    public void EmptyEndpoints_ShowEmptyState_NoDataRows()
    {
        _mockHandler.AnalyzePayload = "[]";
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Todo en orden");
        });
        cut.Markup.Should().NotContain("PRODUCTO ENDPOINT TEST");
    }

    [Fact]
    public void EndpointError_ShowsErrorBanner_DoesNotThrow()
    {
        _mockHandler.FailAnalyze = true;
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Error al analizar");
        });
    }

    [Fact]
    public void NoAnalysisYet_ShowsLoadingPlaceholder_NotEmptyState()
    {
        // No templateId -> component loads maquinas/templates but never runs analysis.
        NavigateTo("stockout-dashboard");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Elegí un template");
        });
        cut.Markup.Should().NotContain("PRODUCTO ENDPOINT TEST");
    }

    private static string OneCriticalSlotJson()
    {
        var payload = new[]
        {
            new
            {
                MaquinaId = 7,
                MaquinaNombre = "MAQUINA ENDPOINT",
                ProductoId = 42,
                ProductoNombre = "PRODUCTO ENDPOINT TEST",
                NumeroSlot = "A2",
                PrimeraVenta = new DateTime(2026, 6, 18, 9, 0, 0),
                UltimaVenta = new DateTime(2026, 6, 22, 14, 20, 0),
                FechaAgotamientoEstimada = new DateTime(2026, 6, 20, 10, 0, 0),
                TieneVentasPosterioresAlAgotamiento = true,
                PrimeraVentaPosteriorAlAgotamiento = new DateTime(2026, 6, 22, 14, 20, 0),
                UltimaVentaPosteriorAlAgotamiento = new DateTime(2026, 6, 22, 14, 20, 0),
                UltimaActividadMaquina = new DateTime(2026, 7, 3, 8, 0, 0),
                FinReporte = new DateTime(2026, 7, 3, 8, 0, 0),
                FechasVentas = new[]
                {
                    new DateTime(2026, 6, 20, 10, 0, 0),
                    new DateTime(2026, 6, 22, 14, 20, 0)
                },
                PosibleQuiebre = true,
                HorasSinStock = 180.0,
                StockInicial = 8,
                StockActual = 0,
                CantidadVendida = 100,
                FillPct = 0,
                DiasHastaStockout = (decimal?)0m,
                EsDeadSlot = false,
                HorasActivas = 100.0,
                VelocidadPorHora = 0.5m,
                EstimateConfidence = 1,
                PrecioPromedioVenta = 1200m,
                GananciaPromedio = 400m,
                DineroPerdidoEstimado = 12000.5m,
                GananciaPerdidaEstimada = 5000.5m,
                UnidadesNoAtendidasEstimadas = 20m
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    // Mismo producto (id 42) en dos slots de la misma máquina. El slot A2 se agotó
    // (10 de 10 vendidas, el backend lo marca en quiebre por slot y estima pérdida),
    // el slot A3 conserva stock (3 de 10). La agrupación conserva la pérdida del slot
    // A2 aunque el producto todavía tenga unidades en A3.
    private static string ProductAcrossTwoSlotsWithLeftoverJson()
    {
        var baseFecha = new DateTime(2026, 6, 20, 8, 0, 0);
        DateTime[] Fechas(int n) =>
            Enumerable.Range(0, n).Select(i => baseFecha.AddHours(i * 3)).ToArray();

        var payload = new object[]
        {
            new
            {
                MaquinaId = 7,
                MaquinaNombre = "MAQUINA ENDPOINT",
                ProductoId = 42,
                ProductoNombre = "PRODUCTO ENDPOINT TEST",
                NumeroSlot = "A2",
                PrimeraVenta = baseFecha,
                UltimaVenta = baseFecha.AddHours(27),
                FechaAgotamientoEstimada = baseFecha.AddHours(27),
                UltimaActividadMaquina = new DateTime(2026, 7, 3, 8, 0, 0),
                FinReporte = new DateTime(2026, 7, 3, 8, 0, 0),
                FechasVentas = Fechas(10),
                PosibleQuiebre = true,
                HorasSinStock = 200.0,
                StockInicial = 10,
                StockActual = 0,
                CantidadVendida = 10,
                FillPct = 0,
                DiasHastaStockout = (decimal?)0m,
                EsDeadSlot = false,
                HorasActivas = 27.0,
                VelocidadPorHora = 0.37m,
                PrecioPromedioVenta = 1200m,
                GananciaPromedio = 400m,
                DineroPerdidoEstimado = 8000m,
                GananciaPerdidaEstimada = 3000m
            },
            new
            {
                MaquinaId = 8,
                MaquinaNombre = "MAQUINA SANA",
                ProductoId = 42,
                ProductoNombre = "PRODUCTO ENDPOINT TEST",
                NumeroSlot = "A3",
                PrimeraVenta = baseFecha,
                UltimaVenta = baseFecha.AddHours(6),
                UltimaActividadMaquina = new DateTime(2026, 7, 3, 8, 0, 0),
                FinReporte = new DateTime(2026, 7, 3, 8, 0, 0),
                FechasVentas = Fechas(3),
                PosibleQuiebre = false,
                HorasSinStock = 0.0,
                StockInicial = 10,
                StockActual = 7,
                CantidadVendida = 3,
                FillPct = 70,
                DiasHastaStockout = (decimal?)9m,
                EsDeadSlot = false,
                HorasActivas = 6.0,
                VelocidadPorHora = 0.5m,
                PrecioPromedioVenta = 1200m,
                GananciaPromedio = 400m,
                DineroPerdidoEstimado = 0m,
                GananciaPerdidaEstimada = 0m
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string ProductMachineBundleJson()
    {
        var slots = new object[]
        {
            new { MaquinaId = 7, MaquinaNombre = "MÁQUINA NORTE", ProductoId = 42, ProductoNombre = "COCA COLA", NumeroSlot = "A2", StockInicial = 10, StockActual = 0, CantidadVendida = 10, PosibleQuiebre = true, FechaAgotamientoEstimada = new DateTime(2026, 6, 20, 10, 0, 0) },
            new { MaquinaId = 7, MaquinaNombre = "MÁQUINA NORTE", ProductoId = 42, ProductoNombre = "COCA COLA", NumeroSlot = "A3", StockInicial = 10, StockActual = 6, CantidadVendida = 4, PosibleQuiebre = false },
            new { MaquinaId = 7, MaquinaNombre = "MÁQUINA NORTE", ProductoId = 99, ProductoNombre = "SLOT ELEGIBLE", NumeroSlot = "B1", StockInicial = 5, StockActual = 2, CantidadVendida = 3, PosibleQuiebre = false },
            new { MaquinaId = 7, MaquinaNombre = "MÁQUINA NORTE", ProductoId = 100, ProductoNombre = "SLOT NO CONFIABLE", NumeroSlot = "B2", StockInicial = 0, StockActual = 0, CantidadVendida = 0, EsDeadSlot = true, QualityFlags = 1 },
            new { MaquinaId = 8, MaquinaNombre = "MÁQUINA SUR", ProductoId = 42, ProductoNombre = "COCA COLA", NumeroSlot = "C1", StockInicial = 5, StockActual = 0, CantidadVendida = 5, PosibleQuiebre = true, FechaAgotamientoEstimada = new DateTime(2026, 6, 19, 10, 0, 0), GananciaPerdidaEstimada = 4000m }
        };
        var products = new object[]
        {
            new { MaquinaId = 7, MaquinaNombre = "MÁQUINA NORTE", ProductoId = 42, ProductoNombre = "COCA COLA", CantidadSlotsElegibles = 2, StockInicialTotal = 20, CantidadVendidaTotal = 14, SlotsParcialmenteAgotados = new[] { "A2" }, TieneEvidenciaCronologicaIncompleta = true },
            new { MaquinaId = 8, MaquinaNombre = "MÁQUINA SUR", ProductoId = 42, ProductoNombre = "COCA COLA", CantidadSlotsElegibles = 1, StockInicialTotal = 5, CantidadVendidaTotal = 5, FechaAgotamientoEstimada = new DateTime(2026, 6, 19, 10, 0, 0), HorasSinStock = 48.0, VelocidadPorHora = 1m, DineroPerdidoEstimado = 12000m, GananciaPerdidaEstimada = 4000m, UnidadesNoAtendidasEstimadas = 10m },
            new { MaquinaId = 7, MaquinaNombre = "MÁQUINA NORTE", ProductoId = 100, ProductoNombre = "SLOT NO CONFIABLE", TieneDatosNoConfiables = true, CantidadSlotsExcluidos = 1 }
        };
        return JsonSerializer.Serialize(new { Slots = slots, ProductosMaquina = products });
    }

    private class StockoutMockHttpMessageHandler : HttpMessageHandler
    {
        public string AnalyzePayload { get; set; } = "[]";
        public string V2AnalyzePayload { get; set; } = "{\"slots\":[],\"productosMaquina\":[]}";
        public int V2AnalyzeRequestCount { get; private set; }
        public bool FailAnalyze { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            string json;

            if (url.Contains("analyze") && FailAnalyze)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("boom")
                });
            }
            if (url.Contains("analyze-v2"))
            {
                V2AnalyzeRequestCount++;
                json = V2AnalyzePayload;
            }
            else if (url.Contains("analyze"))
            {
                json = AnalyzePayload;
            }
            else if (url.Contains("lista-maquinas"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 7, Nombre = "MAQUINA ENDPOINT" }
                });
            }
            else if (url.Contains("TemplateRecarga"))
            {
                // list of templates (OnInitializedAsync -> CargarTemplates)
                json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        Id = 1,
                        Nombre = "Template Test",
                        Descripcion = (string?)null,
                        FechaCreacion = new DateTime(2026, 6, 1),
                        Periodos = new[]
                        {
                            new
                            {
                                Id = 1,
                                MaquinaId = 7,
                                MaquinaNombre = "MAQUINA ENDPOINT",
                                FechaRecarga = new DateTime(2026, 6, 18, 8, 0, 0),
                                FechaFin = new DateTime(2026, 7, 3, 8, 0, 0)
                            }
                        }
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
