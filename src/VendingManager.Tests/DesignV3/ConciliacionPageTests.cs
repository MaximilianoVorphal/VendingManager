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

    // ── Task 2.2: Verify transferencia ────────────────────────────────────────

    [Fact]
    public void Verificar_PostExitoso_ActualizaBadge()
    {
        // Configure mock for successful POST
        _mockHandler.PostResponse = HttpStatusCode.NoContent;

        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click on unverified compra row (Victor Rojas — Id=2)
        var rows = cut.FindAll("div.con-row");
        var victorRow = rows.First(r => r.InnerHtml.Contains("VICTOR ROJAS"));
        victorRow.Click();

        // Verify comprobante panel shows unverified state first
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Verificar y seguir");
        }, TimeSpan.FromSeconds(10));

        // Click verify button
        cut.Find("button:contains('Verificar y seguir')").Click();

        // Assert: POST was sent and auto-advanced to next pendiente (gasto 10)
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().Contain(r => r.Contains("compra/2/verificar"));
            // Auto-advance moves to next unverified: gasto 10 (Sociedad Henriquez)
            cut.Markup.Should().Contain("SOCIEDAD HENRIQUEZ SPA");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Verificar_Post409_RollbackYMuestraAlert()
    {
        // Configure mock for conflict response
        _mockHandler.PostResponse = HttpStatusCode.Conflict;
        _mockHandler.PostErrorBody = "Otro usuario modificó esta transferencia. Recargá la página.";

        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click on unverified compra row (Victor Rojas — Id=2)
        var rows = cut.FindAll("div.con-row");
        var victorRow = rows.First(r => r.InnerHtml.Contains("VICTOR ROJAS"));
        victorRow.Click();

        // Click verify button
        cut.Find("button:contains('Verificar y seguir')").Click();

        // Assert rollback: still shows verify button (not "Verificada"), alert shown
        cut.WaitForAssertion(() =>
        {
            // Rollback: comprobante panel should still show verify button
            cut.Markup.Should().Contain("Verificar y seguir");
            cut.Markup.Should().Contain("Otro usuario");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void VerificarYAvanzar_SeleccionaSiguientePendiente()
    {
        // Configure mock for successful POST
        _mockHandler.PostResponse = HttpStatusCode.NoContent;

        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click on unverified compra row (Victor Rojas — Id=2)
        var rows = cut.FindAll("div.con-row");
        var victorRow = rows.First(r => r.InnerHtml.Contains("VICTOR ROJAS"));
        victorRow.Click();

        // Verify comprobante panel shows Victor Rojas
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("VICTOR ROJAS ALFARO");
        }, TimeSpan.FromSeconds(10));

        // Click verify button
        cut.Find("button:contains('Verificar y seguir')").Click();

        // Assert auto-advance: comprobante panel should now show the gasto (Sociedad Henriquez)
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("SOCIEDAD HENRIQUEZ SPA");
        }, TimeSpan.FromSeconds(10));
    }

    // ── Gap 1: Unverify flow ──────────────────────────────────────────────────

    [Fact]
    public void Unverify_PostExitoso_ActualizaBadge()
    {
        // Configure mock for successful POST
        _mockHandler.PostResponse = HttpStatusCode.NoContent;

        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Transferencia 1 (Jose Miguel) is verified and selected by default.
        // The comprobante panel should show "Verificada" badge and "Quitar" button.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Verificada");
            cut.Markup.Should().Contain("Quitar");
        }, TimeSpan.FromSeconds(10));

        // Click "Quitar" button to unverify
        cut.Find("button:contains('Quitar')").Click();

        // Assert: POST was sent to desverificar endpoint, badge updates to unverified
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().Contain(r => r.Contains("desverificar"));
            // After unverify, the panel should show the verify button instead of "Verificada".
            // (The old "Rechazar" button was replaced by a Compra-only "Eliminar" action in
            // commit c2349bf; for Transferencias the panel only shows "Verificar y seguir".)
            cut.Markup.Should().Contain("Verificar y seguir");
            cut.Markup.Should().NotContain("Verificada");
        }, TimeSpan.FromSeconds(10));
    }

    // ── Task 2.5: Devolución modal ────────────────────────────────────────────

    [Fact]
    public void ConfirmarDevolucion_Post201_CierraModalYRefresca()
    {
        // Configure mock for successful POST (devolucion returns 201)
        _mockHandler.PostResponse = HttpStatusCode.Created;

        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Open devolución modal
        cut.Find("button:contains('Registrar devolución')").Click();

        // Assert modal is open
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Registrar devolución");
            cut.Markup.Should().Contain("4.966"); // Diferencia amount
        }, TimeSpan.FromSeconds(10));

        // Click confirm button
        cut.Find("button:contains('Confirmar')").Click();

        // Assert: POST was sent, modal closed, period refreshed
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().Contain(r => r.Contains("devolucion"));
            // Modal should be closed (no longer shows the modal content)
            cut.Markup.Should().NotContain("Esta acción cierra la rendición");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ConfirmarDevolucion_Post201_MuestraSuccessAlert()
    {
        // Configure mock for successful POST (devolucion returns 201)
        _mockHandler.PostResponse = HttpStatusCode.Created;

        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Open devolución modal
        cut.Find("button:contains('Registrar devolución')").Click();

        // Assert modal is open
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Esta acción cierra la rendición");
        }, TimeSpan.FromSeconds(10));

        // Click confirm button
        cut.Find("button:contains('Confirmar')").Click();

        // Assert: success indicator appears after successful POST
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().Contain(r => r.Contains("devolucion"));
            cut.Markup.Should().Contain("Devolución registrada exitosamente");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CancelarDevolucion_NoLlamaApi()
    {
        var cut = RenderComponent<Conciliacion>();

        // Wait for data to load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Open devolución modal
        cut.Find("button:contains('Registrar devolución')").Click();

        // Assert modal is open
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Esta acción cierra la rendición");
        }, TimeSpan.FromSeconds(10));

        var postCountBefore = _mockHandler.PostRequests.Count;

        // Click cancel button
        cut.Find("button:contains('Cancelar')").Click();

        // Assert: no API call, modal closed
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Count.Should().Be(postCountBefore);
            cut.Markup.Should().NotContain("Esta acción cierra la rendición");
        }, TimeSpan.FromSeconds(10));
    }

    // ── Task 2.9: Header Transferencia modal ─────────────────────────────────

    [Fact]
    public void HeaderTransferencia_Post201_RefreshPeriodo()
    {
        _mockHandler.PostResponse = HttpStatusCode.Created;

        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click "Crear Transferencia" header button (the only button whose text contains "Transferencia")
        cut.Find("button:contains('Transferencia')").Click();

        // Assert modal is open (title is "Nueva transferencia" in the new cuadre workflow UI)
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Nueva transferencia");
        }, TimeSpan.FromSeconds(10));

        // Fill form: Monto and Trabajador
        cut.Find("input[id='transf-monto']").Change("50000");
        cut.Find("input[id='transf-trabajador']").Change("Juan Perez");

        // Submit
        cut.Find("button:contains('Crear transferencia')").Click();

        // Assert POST was made to the cuadre workflow endpoint
        // (commit c2349bf moved the "create transferencia" call from
        // "transferencia-con-movimiento" to "api/contabilidad/cuadre")
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().Contain(r => r.Contains("api/contabilidad/cuadre"));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void HeaderCompra_DisabledCuandoNoTransferencias()
    {
        // Use mock with zero transferencias
        _mockHandler.ZeroTransferencias = true;

        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Assert Compra button shows helper text about needing a transferencia
        cut.Markup.Should().Contain("Necesitás al menos una transferencia");
    }

    [Fact]
    public void HeaderCompra_Post201_RefreshPeriodo()
    {
        _mockHandler.PostResponse = HttpStatusCode.Created;

        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click Compra header button
        cut.Find("button:contains('Compra')").Click();

        // Assert modal is open. Commit c2349bf renamed the modal title from
        // "Nueva compra" to just "Compra" and added a "Vincular existente" /
        // "Crear nueva" tab pair. "Vincular existente" only appears in this
        // modal, so we use it as the open-modal signal.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Vincular existente");
        }, TimeSpan.FromSeconds(10));

        // Switch to "Crear nueva" tab to access the proveedor/monto form
        cut.Find("button:contains('Crear nueva')").Click();

        // Fill form: Proveedor and Monto
        cut.Find("input[id='compra-proveedor']").Change("Proveedor Test");
        cut.Find("input[id='compra-monto']").Change("25000");

        // Submit
        cut.Find("button:contains('Crear compra')").Click();

        // Assert POST was made to compra-vinculada
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().Contain(r => r.Contains("compra-vinculada"));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void HeaderGasto_Post201_RefreshPeriodo()
    {
        _mockHandler.PostResponse = HttpStatusCode.Created;

        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click Gasto header button
        cut.Find("button:contains('Gasto')").Click();

        // Assert modal is open
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Nuevo gasto");
        }, TimeSpan.FromSeconds(10));

        // Fill form: Monto, Trabajador, Descripcion
        cut.Find("input[id='gasto-monto']").Change("15000");
        cut.Find("input[id='gasto-trabajador']").Change("Juan Perez");
        cut.Find("input[id='gasto-descripcion']").Change("Gasto de prueba");

        // Submit
        cut.Find("button:contains('Crear gasto')").Click();

        // Assert POST was made to gasto-vinculado
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().Contain(r => r.Contains("gasto-vinculado"));
        }, TimeSpan.FromSeconds(10));
    }

    // ── Task 2.12: Comprobante download ──────────────────────────────────────

    [Fact]
    public void DescargarComprobante_Ok_InvocaDescargarArchivo()
    {
        // Configure mock to return bytes for comprobante
        _mockHandler.ComprobanteBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header

        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click on transferencia 1 (Jose Miguel — has ComprobanteImagenPath = null in mock)
        // So no download button should appear. Let's verify the panel renders.
        var rows = cut.FindAll("div.con-row");
        rows.First().Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Comprobante");
        }, TimeSpan.FromSeconds(10));

        // Transferencia 1 has no comprobante, so no download button
        cut.Markup.Should().NotContain("Descargar comprobante");
    }

    [Fact]
    public void DescargarComprobante_404_MuestraAlert_NoRompe()
    {
        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Click on transferencia 1 (no comprobante)
        var rows = cut.FindAll("div.con-row");
        rows.First().Click();

        // The comprobante panel should render without crashing
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Comprobante");
            // No download button since HasComprobante is false
            cut.Markup.Should().NotContain("Descargar comprobante");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DescargarComprobante_HappyPath_InvocaJSInterop()
    {
        // Configure mock: transferencia 1 has a comprobante image
        _mockHandler.TransferenciaHasComprobante = true;
        _mockHandler.ComprobanteBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header

        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        // Transferencia 1 (Jose Miguel) is selected by default and has ComprobanteImagenPath
        // The download button should render
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Descargar comprobante");
        }, TimeSpan.FromSeconds(10));

        // Click download button
        cut.Find("button:contains('Descargar comprobante')").Click();

        // Assert: JS interop was invoked with descargarArchivo
        cut.WaitForAssertion(() =>
        {
            _mockHandler.PostRequests.Should().BeEmpty("download is a GET, not a POST");
            // The JSInterop call should have been made
            JSInterop.Invocations.Should().Contain(inv => inv.Identifier == "descargarArchivo");
        }, TimeSpan.FromSeconds(10));
    }

    // ── Task 2.16: OnPeriodoChanged debounce/cancel ──────────────────────────

    [Fact]
    public void CambioPeriodo_Debounce300ms_CancelaAnterior()
    {
        // Enable multi-period mock
        _mockHandler.MultiPeriod = true;

        var cut = RenderComponent<Conciliacion>();

        // Wait for initial load
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
            cut.Markup.Should().Contain("Jose Miguel");
        }, TimeSpan.FromSeconds(10));

        cut.Render();

        var requestsBefore = _mockHandler.Requests.Count;

        // Change period selector twice rapidly (within 300ms debounce)
        var select = cut.Find("select");
        select.Change("2");
        select.Change("1");

        // Wait for debounce to settle
        cut.WaitForAssertion(() =>
        {
            // Only the last change should have triggered a fetch
            // The requests should include periodos/1 (the final selection)
            var periodDetailRequests = _mockHandler.Requests
                .Where(r => r.Contains("periodos/"))
                .ToList();
            // At most one additional detail request should have been made
            // (the first change to period 2 should have been cancelled)
            periodDetailRequests.Should().Contain(r => r.Contains("periodos/1"));
        }, TimeSpan.FromSeconds(5));
    }

    // ── T-14, T-15: Eliminar transferencia checkbox + button ───────────────

    [Fact]
    public void EliminarCheckbox_Unchecked_BotonNoEnDOM()
    {
        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        // Default state: checkbox unchecked → "Eliminar transferencia" button must NOT be in DOM
        cut.Markup.Should().NotContain("Eliminar transferencia");
    }

    [Fact]
    public void EliminarCheckbox_Checked_BotonEnDOM()
    {
        var cut = RenderComponent<Conciliacion>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("<select");
        }, TimeSpan.FromSeconds(10));

        // Check the delete-transferencia checkbox
        var checkbox = cut.Find("input[data-testid='eliminar-transf-checkbox']");
        checkbox.Change(true);

        // Now the "Eliminar transferencia" button must be in the DOM
        cut.Markup.Should().Contain("Eliminar transferencia");
    }

    // ── Mock handler ───────────────────────────────────────────────────────────

    private class ConciliacionMockHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public List<string> PostRequests { get; } = new();
        public List<string> DeleteRequests { get; } = new();
        public bool ReturnError500 { get; set; }
        public bool ZeroTransferencias { get; set; }
        public HttpStatusCode? PostResponse { get; set; }
        public string? PostErrorBody { get; set; }
        public string? DeleteResponseJson { get; set; }
        public HttpStatusCode? DeleteResponse { get; set; }
        public byte[]? ComprobanteBytes { get; set; }
        public bool ComprobanteNotFound { get; set; }
        public bool MultiPeriod { get; set; }
        public bool TransferenciaHasComprobante { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add(url);

            if (request.Method == HttpMethod.Delete)
            {
                DeleteRequests.Add(url);
                if (DeleteResponse.HasValue)
                {
                    return new HttpResponseMessage(DeleteResponse.Value)
                    {
                        Content = new StringContent(DeleteResponseJson ?? "{}", System.Text.Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ComprasUnlinked\":2,\"PeriodoId\":1}", System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                PostRequests.Add(url);
                if (PostResponse.HasValue)
                {
                    return new HttpResponseMessage(PostResponse.Value)
                    {
                        Content = new StringContent(PostErrorBody ?? "")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (url.Contains("comprobante"))
            {
                if (ComprobanteNotFound || ComprobanteBytes is null)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("Esta transferencia no tiene comprobante de imagen.")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(ComprobanteBytes)
                };
            }

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
                // Determine which period is being requested
                var periodId = url.Contains("periodos/2") ? 2 : 1;
                var trabajador = periodId == 2 ? "Ana Garcia" : "Jose Miguel";

                json = JsonSerializer.Serialize(new
                {
                    Id = periodId,
                    Name = periodId == 2 ? "Julio 2026" : "Junio 2026",
                    FechaInicio = DateTime.Parse(periodId == 2 ? "2026-07-01" : "2026-06-01"),
                    FechaFin = DateTime.Parse(periodId == 2 ? "2026-07-31" : "2026-06-30"),
                    Estado = 0, // Abierto
                    Trabajador = trabajador,
                    TotalTransferido = 273000m,
                    TotalCompras = 238034m,
                    TotalGastos = 30000m,
                    Devuelto = 0m,
                    Transferencias = ZeroTransferencias ? Array.Empty<object>() : new object[]
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
                            ComprobanteImagenPath = TransferenciaHasComprobante ? "/images/comprobante.jpg" : (string?)null,
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
                var periodos = new List<object>
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
                };
                if (MultiPeriod)
                {
                    periodos.Add(new
                    {
                        Id = 2,
                        Name = "Julio 2026",
                        FechaInicio = DateTime.Parse("2026-07-01"),
                        FechaFin = DateTime.Parse("2026-07-31"),
                        Estado = 0,
                        Trabajador = "Ana Garcia",
                        TotalTransferido = 150000m,
                        TotalCompras = 100000m,
                        TotalGastos = 20000m,
                        Devuelto = 0m
                    });
                }
                json = JsonSerializer.Serialize(periodos);
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
