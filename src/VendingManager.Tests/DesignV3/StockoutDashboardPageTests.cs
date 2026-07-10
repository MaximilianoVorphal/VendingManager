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
    public void LoadedRows_ComeFromEndpoints_NotFromTemplateMock()
    {
        _mockHandler.AnalyzePayload = OneCriticalSlotJson();
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("PRODUCTO ENDPOINT TEST");
        });

        // The template mock array must be gone from the wired component.
        cut.Markup.Should().NotContain("Coca Cola 580cc");
        cut.Markup.Should().NotContain("Score Gorila");
    }

    [Fact]
    public void Kpis_ReflectMockedDtoValues()
    {
        _mockHandler.AnalyzePayload = OneCriticalSlotJson();
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            // GananciaPerdidaEstimada = 5000 -> formatted "$5.000"
            cut.Markup.Should().Contain("$5.000");
            // DineroPerdidoEstimado = 12000 -> "$12.000"
            cut.Markup.Should().Contain("$12.000");
        });
    }

    [Fact]
    public void ProductWithRemainingStock_ShowsNoLoss_NotFlaggedAsStockout()
    {
        // Un producto en dos slots: uno se vació solo, el otro todavía tiene stock.
        // Vendido total (13) < stock total (20) => el producto NO está agotado, así que
        // no debe contarse el quiebre ni arrastrar la pérdida por slot del slot vacío.
        _mockHandler.AnalyzePayload = ProductAcrossTwoSlotsWithLeftoverJson();
        NavigateTo("stockout-dashboard?templateId=1");

        var cut = RenderComponent<StockoutDashboard>();

        cut.WaitForAssertion(() =>
        {
            // Sin productos en quiebre (filtro por defecto) => estado "todo en orden".
            cut.Markup.Should().Contain("Todo en orden");
        });

        // La pérdida fantasma del slot vacío NO debe aparecer en ningún KPI/fila.
        cut.Markup.Should().NotContain("$8.000");
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
                UltimaActividadMaquina = new DateTime(2026, 7, 3, 8, 0, 0),
                FinReporte = new DateTime(2026, 7, 3, 8, 0, 0),
                FechasVentas = new[]
                {
                    new DateTime(2026, 6, 20, 10, 0, 0),
                    new DateTime(2026, 6, 22, 14, 20, 0)
                },
                PosibleQuiebre = true,
                HorasSinStock = 120.0,
                StockInicial = 8,
                StockActual = 0,
                CantidadVendida = 100,
                FillPct = 0,
                DiasHastaStockout = (decimal?)0m,
                EsDeadSlot = false,
                HorasActivas = 100.0,
                VelocidadPorHora = 0.5m,
                PrecioPromedioVenta = 1200m,
                GananciaPromedio = 400m,
                DineroPerdidoEstimado = 12000m,
                GananciaPerdidaEstimada = 5000m
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    // Mismo producto (id 42) en dos slots de la misma máquina. El slot A2 se agotó
    // (10 de 10 vendidas, el backend lo marca en quiebre por slot y estima pérdida),
    // el slot A3 conserva stock (3 de 10). A nivel producto quedan 7 unidades: NO
    // está agotado. Las FechasVentas están completas (Count == vendidas) para que el
    // dashboard reconozca el flujo template y recalcule por producto.
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
                MaquinaId = 7,
                MaquinaNombre = "MAQUINA ENDPOINT",
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

    private class StockoutMockHttpMessageHandler : HttpMessageHandler
    {
        public string AnalyzePayload { get; set; } = "[]";
        public bool FailAnalyze { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            string json;

            if (url.Contains("analyze"))
            {
                if (FailAnalyze)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("boom")
                    });
                }
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
