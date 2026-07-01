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
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class ConciliacionPageTests : TestContext
{
    private readonly ConciliacionMockHandler _mockHandler;

    public ConciliacionPageTests()
    {
        _mockHandler = new ConciliacionMockHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void PeriodosCargados_SelectorTieneOpciones()
    {
        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("periodos"));
            cut.Markup.Should().Contain("<select");
            cut.Markup.Should().Contain("Jose Miguel");
        }, TimeSpan.FromSeconds(10));
    }

    // ── Task 1.3: Period detail load ───────────────────────────────────────────

    [Fact]
    public void PeriodoDetalle_RenderizaKPIs()
    {
        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Transferido");
            cut.Markup.Should().Contain("Diferencia");
            cut.Markup.Should().Contain("273.000");
        }, TimeSpan.FromSeconds(10));
    }

    // ── Task 1.5: Ledger render ────────────────────────────────────────────────

    [Fact]
    public void Ledger_RenderizaSecciones()
    {
        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        // Force a re-render to ensure latest state
        cut.Render();

        // Assert ledger content (names are uppercased in the template)
        cut.Markup.Should().Contain("Transferencias");
        cut.Markup.Should().Contain("ALVI");
        cut.Markup.Should().Contain("VICTOR ROJAS");
        cut.Markup.Should().Contain("SOCIEDAD HENRIQUEZ");
    }

    // ── Task 1.7: Filter tabs ──────────────────────────────────────────────────

    [Fact]
    public void FiltroCompras_OcultaGastosYTransferencias()
    {
        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("ALVI");
        }, TimeSpan.FromSeconds(10));

        cut.Find("button:contains('Compras')").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("ALVI");
            cut.Markup.Should().NotContain("Sociedad Henriquez");
        });
    }

    [Fact]
    public void FiltroPendientes_OcultaVerificadas()
    {
        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click "Pend." filter tab
        cut.Find("button:contains('Pend.')").Click();

        // Assert — only unverified rows visible
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("VICTOR ROJAS");
            cut.Markup.Should().NotContain("ALVI");
        });
    }

    // ── Task 1.9: Row click selection ──────────────────────────────────────────

    [Fact]
    public void ClickFila_ActualizaPanelComprobante()
    {
        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click on a compra row (Victor Rojas — unverified, uppercased in template)
        // Must target the parent .con-row div that has the onclick handler
        var rows = cut.FindAll("div.con-row");
        var victorRow = rows.First(r => r.InnerHtml.Contains("VICTOR ROJAS"));
        victorRow.Click();

        // Assert — comprobante panel updates with the clicked row's data
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("VICTOR ROJAS ALFARO");
        });
    }

    // ── Task 1.11: Error handling ──────────────────────────────────────────────

    [Fact]
    public void Error500_MuestraIndustrialAlert()
    {
        _mockHandler.ReturnError500 = true;

        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Error al cargar");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DiferenciaNoCero_UsaWarningColor()
    {
        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("signal-warning");
            cut.Markup.Should().Contain("4.966");
        }, TimeSpan.FromSeconds(10));
    }

    // ── Mock handler ───────────────────────────────────────────────────────────

    private class ConciliacionMockHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public bool ReturnError500 { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add(url);

            if (ReturnError500)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Internal Server Error")
                };
            }

            string json;

            if (url.Contains("periodos/") && request.Method == HttpMethod.Get)
            {
                // GET periodos/{id} — full detail
                json = JsonSerializer.Serialize(new
                {
                    Id = 1,
                    Name = "Junio 2026",
                    FechaInicio = DateTime.Parse("2026-06-01"),
                    FechaFin = DateTime.Parse("2026-06-30"),
                    Estado = 0, // Abierto
                    Trabajador = "Jose Miguel",
                    TotalTransferido = 273000m,
                    TotalCompras = 238034m,
                    TotalGastos = 30000m,
                    Devuelto = 0m,
                    Transferencias = new object[]
                    {
                        new
                        {
                            Id = 1,
                            Fecha = DateTime.Parse("2026-06-03"),
                            Monto = 273000m,
                            Descripcion = "Fondos junio",
                            Trabajador = "Jose Miguel",
                            Estado = 0,
                            RendicionId = (int?)1,
                            PeriodoId = (int?)1,
                            MovimientoCajaId = (int?)null,
                            Verificada = true,
                            ComprobanteImagenPath = (string?)null,
                            Compras = new object[]
                            {
                                new
                                {
                                    Id = 1,
                                    FechaCompra = DateTime.Parse("2026-06-05"),
                                    Proveedor = "ALVI-Alvi Supermercado Mayorista S.A",
                                    NumeroDocumento = "748943",
                                    MontoTotal = 72380m,
                                    Estado = "PAGADA",
                                    TipoFactura = "MERCADERIA",
                                    PagadaCaja = true,
                                    FacturaImagenPath = (string?)null,
                                    Verificada = true,
                                    TransferenciaId = 1,
                                    ProveedorCatalogId = (int?)null,
                                    ProveedorCanonical = (string?)null,
                                    Detalles = Array.Empty<object>()
                                },
                                new
                                {
                                    Id = 2,
                                    FechaCompra = DateTime.Parse("2026-06-06"),
                                    Proveedor = "Victor Rojas Alfaro",
                                    NumeroDocumento = "121713",
                                    MontoTotal = 21680m,
                                    Estado = "PAGADA",
                                    TipoFactura = "MERCADERIA",
                                    PagadaCaja = true,
                                    FacturaImagenPath = (string?)null,
                                    Verificada = false,
                                    TransferenciaId = 1,
                                    ProveedorCatalogId = (int?)null,
                                    ProveedorCanonical = (string?)null,
                                    Detalles = Array.Empty<object>()
                                }
                            }
                        }
                    },
                    Gastos = new object[]
                    {
                        new
                        {
                            Id = 10,
                            Fecha = DateTime.Parse("2026-06-10"),
                            Descripcion = "Sociedad Henriquez SPA",
                            Monto = 30000m,
                            Tipo = "GASTO_GENERAL",
                            Categoria = "Gasto general",
                            ImagenPath = (string?)null,
                            ProductoId = (int?)null,
                            Cantidad = 1,
                            OrdenCargaId = (int?)null,
                            CompraId = (int?)null,
                            GastoRecurrenteId = (int?)null
                        }
                    }
                });
            }
            else if (url.Contains("periodos") && request.Method == HttpMethod.Get)
            {
                // GET periodos — list
                json = JsonSerializer.Serialize(new object[]
                {
                    new
                    {
                        Id = 1,
                        Name = "Junio 2026",
                        FechaInicio = DateTime.Parse("2026-06-01"),
                        FechaFin = DateTime.Parse("2026-06-30"),
                        Estado = 0,
                        Trabajador = "Jose Miguel",
                        TotalTransferido = 273000m,
                        TotalCompras = 238034m,
                        TotalGastos = 30000m,
                        Devuelto = 0m
                    }
                });
            }
            else
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
        }
    }
}
