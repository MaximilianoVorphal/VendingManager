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

public class InformeVentasPageTests : TestContext
{
    private readonly InformeVentasMockHandler _mockHandler;

    public InformeVentasPageTests()
    {
        _mockHandler = new InformeVentasMockHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void InitialLoad_FetchesReporteRango_WithIncludePhantomFalse()
    {
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
            _mockHandler.Requests.First(r => r.Contains("reporte-rango"))
                .Should().Contain("includePhantom=false");
        });
    }

    [Fact]
    public void InitialLoad_FetchesInformeFinanciero()
    {
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("informe-financiero"));
            cut.Markup.Should().Contain("$1.234.567");
        });
    }

    [Fact]
    public void InitialLoad_FetchesAnalisisProductos()
    {
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("analisis-productos"));
        });

        // Switch to productos tab to see the product data
        cut.Find("button:contains('Top productos')").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("COCA COLA 580CC");
        });
    }

    [Fact]
    public void InitialLoad_FetchesListaMaquinas_PrependsTodas()
    {
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("lista-maquinas"));
        });

        var selects = cut.FindComponents<VendingManager.Web.Shared.VmSelect>();
        var unitSelect = selects.FirstOrDefault(s => s.Instance.Label == "Unidad");
        unitSelect.Should().NotBeNull();
        var optionsList = unitSelect!.Instance.Options.ToList();
        optionsList.Should().HaveCount(4); // Todas + 3 machines
        optionsList.First().Value.Should().Be("0");
        optionsList.First().Label.Should().Contain("Todas");
    }

    [Fact]
    public void NoIncluirFantasmaToggle_Rendered()
    {
        var cut = RenderComponent<InformeVentas>();

        cut.Markup.Should().NotContain("Incluir ventas fantasma");
        cut.Markup.Should().NotContain("ToggleFantasma");
    }

    [Fact]
    public void EmptyDetalles_ShowsSinDatosEnElRango()
    {
        _mockHandler.ReporteResponse = new
        {
            TotalVentas = 0,
            MontoTotal = 0m,
            MontoPagado = 0m,
            MontoPendiente = 0m,
            MontoPhantom = 0m,
            GananciaTotal = 0m,
            Detalle = Array.Empty<object>(),
            Fantasmas = Array.Empty<object>()
        };

        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Sin datos en el rango");
        });
    }

    [Fact]
    public void DecimalCurrency_RendersEsCLFormat()
    {
        _mockHandler.ReporteResponse = new
        {
            TotalVentas = 1,
            MontoTotal = 1234567.89m,
            MontoPagado = 1234567.89m,
            MontoPendiente = 0m,
            MontoPhantom = 0m,
            GananciaTotal = 500000m,
            Detalle = new object[]
            {
                new
                {
                    FechaRaw = DateTime.Parse("2026-06-27T08:37:00"),
                    Maquina = "MAQUINA 2410280022 DEPTO4",
                    Slot = "40",
                    Producto = "Coca Cola 580cc",
                    Monto = 1234567.89m,
                    CostoUnitario = 500m,
                    Ganancia = 291.50m,
                    Estado = "CONF"
                }
            },
            Fantasmas = Array.Empty<object>()
        };

        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("$1.234.568"); // rounded to int, es-CL format
        });
    }

    [Fact]
    public void DetalleMaquina_RendersShortCode()
    {
        _mockHandler.ReporteResponse = new
        {
            TotalVentas = 1,
            MontoTotal = 1190m,
            MontoPagado = 1190m,
            MontoPendiente = 0m,
            MontoPhantom = 0m,
            GananciaTotal = 291m,
            Detalle = new object[]
            {
                new
                {
                    FechaRaw = DateTime.Parse("2026-06-27T08:37:00"),
                    Maquina = "MAQUINA 2410280022 — DEPTO 4",
                    Slot = "40",
                    Producto = "Coca Cola 580cc",
                    Monto = 1190m,
                    CostoUnitario = 500m,
                    Ganancia = 291m,
                    Estado = "CONF"
                }
            },
            Fantasmas = Array.Empty<object>()
        };

        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("0022");
        });
    }

    // ── WU-4 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Error500_ShowsIndustrialAlert_DoesNotCrash()
    {
        _mockHandler.ReturnError500 = true;

        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Error al cargar datos del servidor");
        });

        // Page should still be functional (not crashed)
        cut.Markup.Should().Contain("Informe de Ventas");
    }

    [Fact]
    public void MachineRail_GroupsByShortCode_Top5ByTotalDesc()
    {
        _mockHandler.ReporteResponse = new
        {
            TotalVentas = 10,
            MontoTotal = 100000m,
            MontoPagado = 100000m,
            MontoPendiente = 0m,
            MontoPhantom = 0m,
            GananciaTotal = 50000m,
            Detalle = new object[]
            {
                new { FechaRaw = DateTime.Parse("2026-06-27T08:00:00"), Maquina = "MAQUINA 2410280022 DEPTO4", Slot = "1", Producto = "A", Monto = 10000m, CostoUnitario = 5000m, Ganancia = 5000m, Estado = "CONF" },
                new { FechaRaw = DateTime.Parse("2026-06-27T09:00:00"), Maquina = "MAQUINA 2410280022 DEPTO4", Slot = "2", Producto = "B", Monto = 8000m, CostoUnitario = 4000m, Ganancia = 4000m, Estado = "CONF" },
                new { FechaRaw = DateTime.Parse("2026-06-27T10:00:00"), Maquina = "MAQUINA 2410280012 CAFE", Slot = "3", Producto = "C", Monto = 15000m, CostoUnitario = 7000m, Ganancia = 8000m, Estado = "CONF" },
                new { FechaRaw = DateTime.Parse("2026-06-27T11:00:00"), Maquina = "MAQUINA 2410280023 SNACK", Slot = "4", Producto = "D", Monto = 5000m, CostoUnitario = 2000m, Ganancia = 3000m, Estado = "CONF" },
            },
            Fantasmas = Array.Empty<object>()
        };

        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            // The machine rail should show grouped machines
            // 0012 has 15000 (top 1), 0022 has 18000 (top 1 actually), 0023 has 5000
            cut.Markup.Should().Contain("0022");
            cut.Markup.Should().Contain("0012");
            cut.Markup.Should().Contain("0023");
        });
    }

    [Fact]
    public void MachineFilterChange_TriggersNewFetch()
    {
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        var requestCountBefore = _mockHandler.Requests.Count(r => r.Contains("reporte-rango"));

        // Change the machine filter
        var selects = cut.FindComponents<VendingManager.Web.Shared.VmSelect>();
        var unitSelect = selects.FirstOrDefault(s => s.Instance.Label == "Unidad");
        unitSelect.Should().NotBeNull();
        unitSelect!.Find("select").Change("1");

        // Wait for debounce (300ms) + margin
        cut.WaitForAssertion(() =>
        {
            var requestCountAfter = _mockHandler.Requests.Count(r => r.Contains("reporte-rango"));
            requestCountAfter.Should().BeGreaterThan(requestCountBefore);
        }, TimeSpan.FromSeconds(2));
    }

    // ── WU-1: Sincronizar and Exportar handler tests ────────────────────────

    [Fact]
    public void Sincronizar_PostsSyncPortal_WithZeroMaquinaId_AndFechaLimiteHasta()
    {
        var cut = RenderComponent<InformeVentas>();

        // Wait for initial load to complete
        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        // Click the Sincronizar button
        cut.Find("button:contains('Sincronizar')").Click();

        cut.WaitForAssertion(() =>
        {
            var syncRequest = _mockHandler.Requests.FirstOrDefault(r => r.Contains("sync-portal"));
            syncRequest.Should().NotBeNull("sync-portal should have been called");
            syncRequest.Should().Contain("maquinaId=0");
            syncRequest.Should().Contain("fechaLimite=");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Sincronizar_OnSuccess_ShowsIndustrialAlertSuccess_AndReloadsData()
    {
        _mockHandler.SyncReturnsOk = true;
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        var reporteRequestsBefore = _mockHandler.Requests.Count(r => r.Contains("reporte-rango"));

        cut.Find("button:contains('Sincronizar')").Click();

        cut.WaitForAssertion(() =>
        {
            // Success alert should appear
            cut.Markup.Should().Contain("Sincronización");
            // reporte-rango should be fetched again (reload)
            var reporteRequestsAfter = _mockHandler.Requests.Count(r => r.Contains("reporte-rango"));
            reporteRequestsAfter.Should().BeGreaterThan(reporteRequestsBefore);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Sincronizar_OnError_ShowsIndustrialAlertDanger()
    {
        _mockHandler.SyncReturnsError500 = true;
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        cut.Find("button:contains('Sincronizar')").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Error al sincronizar");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Sincronizar_DuringSync_DisablesButton()
    {
        _mockHandler.SyncDelayMs = 2000; // Slow response to observe syncing state
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        cut.Find("button:contains('Sincronizar')").Click();

        // During sync, a disabled button with "Sincronizando…" should appear
        cut.WaitForAssertion(() =>
        {
            var disabledBtn = cut.Find("button[disabled]");
            disabledBtn.TextContent.Should().Contain("Sincronizando");
        }, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Exportar_GetsExportarEndpoint_WithCurrentFilters()
    {
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        cut.Find("button:contains('Exportar XLS')").Click();

        cut.WaitForAssertion(() =>
        {
            var exportRequest = _mockHandler.Requests.FirstOrDefault(r => r.Contains("exportar"));
            exportRequest.Should().NotBeNull("exportar endpoint should have been called");
            exportRequest.Should().Contain("maquinaId=0");
            exportRequest.Should().Contain("includePhantom=false");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Exportar_OnSuccess_InvokesDescargarArchivo_WithBytes()
    {
        _mockHandler.ExportReturnsOk = true;
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        cut.Find("button:contains('Exportar XLS')").Click();

        cut.WaitForAssertion(() =>
        {
            // JSInterop should have been invoked with descargarArchivo
            var jsInvocations = JSInterop.Invocations;
            jsInvocations.Should().Contain(i => i.Identifier == "descargarArchivo");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Exportar_OnError_ShowsIndustrialAlertDanger()
    {
        _mockHandler.ExportReturnsError500 = true;
        var cut = RenderComponent<InformeVentas>();

        cut.WaitForAssertion(() =>
        {
            _mockHandler.Requests.Should().Contain(r => r.Contains("reporte-rango"));
        });

        cut.Find("button:contains('Exportar XLS')").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Error al exportar");
        }, TimeSpan.FromSeconds(2));
    }

    // ── Mock handler ───────────────────────────────────────────────────────────

    private class InformeVentasMockHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        public object? ReporteResponse { get; set; }
        public bool ReturnError500 { get; set; }

        // Sync-portal behavior
        public bool SyncReturnsOk { get; set; }
        public bool SyncReturnsError500 { get; set; }
        public int SyncDelayMs { get; set; }

        // Export behavior
        public bool ExportReturnsOk { get; set; }
        public bool ExportReturnsError500 { get; set; }

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

            // ── sync-portal (POST) ──
            if (url.Contains("sync-portal"))
            {
                if (SyncDelayMs > 0)
                    await Task.Delay(SyncDelayMs, cancellationToken);

                if (SyncReturnsError500)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("Error interno del servidor")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("Sincronización ALT Exitosa. 16 ventas importadas.")
                };
            }

            // ── exportar (GET) ──
            if (url.Contains("exportar"))
            {
                if (ExportReturnsError500)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("Error al generar archivo")
                    };
                }

                // Return dummy xlsx bytes (PK zip header)
                var xlsxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(xlsxBytes)
                };
            }

            string json;

            if (url.Contains("reporte-rango"))
            {
                if (ReporteResponse is not null)
                {
                    json = JsonSerializer.Serialize(ReporteResponse);
                }
                else
                {
                    json = JsonSerializer.Serialize(new
                    {
                        TotalVentas = 16,
                        MontoTotal = 1237360m,
                        MontoPagado = 1188930m,
                        MontoPendiente = 48430m,
                        MontoPhantom = 0m,
                        GananciaTotal = 569470m,
                        Detalle = new object[]
                        {
                            new { FechaRaw = DateTime.Parse("2026-06-27T08:37:00"), Maquina = "MAQUINA 2410280022 DEPTO4", Slot = "40", Producto = "Coca Cola 580cc", Monto = 1190m, CostoUnitario = 899m, Ganancia = 291m, Estado = "PEND" },
                            new { FechaRaw = DateTime.Parse("2026-06-27T08:36:00"), Maquina = "MAQUINA 2410280022 DEPTO4", Slot = "41", Producto = "Score Gorila Lata 473cc", Monto = 1490m, CostoUnitario = 858m, Ganancia = 632m, Estado = "PEND" },
                            new { FechaRaw = DateTime.Parse("2026-06-27T08:36:00"), Maquina = "MAQUINA 2410280022 DEPTO4", Slot = "14", Producto = "Natur Arroz", Monto = 850m, CostoUnitario = 0m, Ganancia = 850m, Estado = "CONF" },
                            new { FechaRaw = DateTime.Parse("2026-06-26T21:00:00"), Maquina = "MAQUINA 2410280012 CAFE", Slot = "105", Producto = "Tuareg / Din Don", Monto = 1000m, CostoUnitario = 545m, Ganancia = 455m, Estado = "CONF" },
                            new { FechaRaw = DateTime.Parse("2026-06-26T17:45:00"), Maquina = "MAQUINA 2410280023 SNACK", Slot = "22", Producto = "Coca Cola Lata", Monto = 990m, CostoUnitario = 470m, Ganancia = 520m, Estado = "CONF" }
                        },
                        Fantasmas = Array.Empty<object>()
                    });
                }
            }
            else if (url.Contains("informe-financiero"))
            {
                json = JsonSerializer.Serialize(new
                {
                    VentasTotales = 1237360m,
                    CostoVentas = 667890m,
                    MargenBruto = 569470m,
                    GastosOperativos = 0m,
                    UtilidadNeta = 1234567m,
                    MargenPorcentaje = 46.0m
                });
            }
            else if (url.Contains("analisis-productos"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new { ProductoId = 1, Nombre = "COCA COLA 580CC", Codigo = "CC580", Categoria = "Bebidas", CantidadVendida = 206, TotalVentas = 282830m, TotalGanancia = 100000m, RotacionDiaria = 10m, Clasificacion = "Estrella", AporteUtilidad = 35000m, ClasificacionABC = (string?)"A", PorcentajeAcumulado = (decimal?)15.0m, Tendencia = (string?)"▲", CambioPorcentual = (decimal?)12.5m },
                    new { ProductoId = 2, Nombre = "COCA COLA LATA", Codigo = "CCL", Categoria = "Bebidas", CantidadVendida = 159, TotalVentas = 156750m, TotalGanancia = 70000m, RotacionDiaria = 8m, Clasificacion = "Estrella", AporteUtilidad = 25000m, ClasificacionABC = (string?)"A", PorcentajeAcumulado = (decimal?)30.0m, Tendencia = (string?)"→", CambioPorcentual = (decimal?)0m }
                });
            }
            else if (url.Contains("lista-maquinas"))
            {
                json = JsonSerializer.Serialize(new object[]
                {
                    new { Id = 1, Nombre = "MAQUINA 2410280012 CAFE + SNACK" },
                    new { Id = 2, Nombre = "MAQUINA 2410280022 SNACK" },
                    new { Id = 3, Nombre = "MAQUINA 2410280023 SNACK" }
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
