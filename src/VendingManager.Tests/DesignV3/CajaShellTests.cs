using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Pages;
using VendingManager.Web.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class CajaShellTests : TestContext
{
    private readonly CajaMockHttpMessageHandler _mockHandler;

    public CajaShellTests()
    {
        _mockHandler = new CajaMockHttpMessageHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider, AuthenticatedAuthStateProvider>();
        Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static string ProjectCssPath => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "Caja.razor.css"));

    [Fact]
    public void Caja_RendersFourVmKpiCards()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("VENTAS DEL MES");
            cut.Markup.Should().Contain("GASTOS TOTALES");
            cut.Markup.Should().Contain("APORTES / INYECCIONES");
            cut.Markup.Should().Contain("DISPONIBLE EN CAJA");
        });

        var kpis = cut.FindComponents<VmKpiCard>();
        kpis.Count.Should().Be(4);
    }

    [Fact]
    public void Caja_KpiValues_UseDesignTokens()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("VENTAS DEL MES"));

        cut.Markup.Should().Contain("var(--signal-success)");
        cut.Markup.Should().Contain("var(--signal-danger)");
        cut.Markup.Should().Contain("#0d6efd");
        cut.Markup.Should().Contain("var(--ink-900)");
    }

    [Fact]
    public void Caja_NewMovementForm_UsesVmInputAndVmSelectAndVmButton()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("NUEVO MOVIMIENTO"));

        var nuevoButton = cut.FindAll("button").First(b => b.TextContent.Contains("NUEVO MOVIMIENTO"));
        nuevoButton.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("REGISTRAR MOVIMIENTO"));

        var inputs = cut.FindComponents<VmInput>();
        inputs.Should().Contain(i => i.Instance.Label == "FECHA DE REGISTRO");
        inputs.Should().Contain(i => i.Instance.Label == "DESCRIPCIÓN");
        inputs.Should().Contain(i => i.Instance.Label == "MONTO ($)");

        var selects = cut.FindComponents<VmSelect>();
        selects.Should().Contain(s => s.Instance.Label == "TIPO DE OPERACIÓN");
        selects.Should().Contain(s => s.Instance.Label == "CATEGORÍA");
        selects.Should().Contain(s => s.Instance.Label == "VINCULAR A ORDEN DE CARGA");

        cut.Markup.Should().Contain("REGISTRAR OPERACIÓN_");

        var buttons = cut.FindComponents<VmButton>();
        buttons.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Caja_MovementsList_HasInternalScrollContainer()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("MOVIMIENTOS DE"));

        // The scroll container moved from an inline style to the scoped CSS
        // class .caja-movements-scroll — bUnit can't see scoped CSS rules,
        // so assert the class is applied and the rule lives in Caja.razor.css.
        cut.Find("div.caja-movements-scroll").Should().NotBeNull();

        var css = File.ReadAllText(ProjectCssPath);
        css.Should().MatchRegex(@"\.caja-movements-scroll\s*\{[^}]*overflow-y:\s*auto");
    }

    [Fact]
    public void Caja_ActionButtonsRow_UsesVmButton()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("GASTOS FIJOS"));

        var buttons = cut.FindComponents<VmButton>();
        buttons.Should().Contain(b => cut.Markup.Contains("GASTOS FIJOS"));
        buttons.Should().Contain(b => cut.Markup.Contains("ESTADO DE RESULTADOS"));
    }

    [Fact]
    public void Caja_AplicarPendientesButton_HiddenWhenNoPendientes()
    {
        _mockHandler.SinPendientes = true;
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("GASTOS FIJOS"));

        cut.Markup.Should().NotContain("APLICAR PENDIENTES");
    }

    [Fact]
    public void Caja_NuevoMovimientoButton_DisabledWhenLocked()
    {
        _mockHandler.IsLocked = true;
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("NUEVO MOVIMIENTO"));

        var nuevoButton = cut.FindAll("button").First(b => b.TextContent.Contains("NUEVO MOVIMIENTO"));
        nuevoButton.HasAttribute("disabled").Should().BeTrue();
    }

    private class CajaMockHttpMessageHandler : HttpMessageHandler
    {
        public bool SinPendientes { get; set; }
        public bool IsLocked { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            string json;

            if (url.Contains("api/caja/resumen"))
            {
                json = JsonSerializer.Serialize(new
                {
                    SaldoAnterior = 0m,
                    IngresosVentas = 4820000m,
                    GastosOperativos = 1640500m,
                    AportesExtra = 500000m,
                    SaldoFinal = 3429500m,
                    UtilidadTotal = 3179500m,
                    GastosMercaderia = 1020000m,
                    TotalCostoVenta = 1020000m,
                    Mermas = 20000m,
                    GastosVariables = 800000m,
                    GastosFijos = 840500m,
                    UtilidadOperacional = 1979500m,
                    SueldoEsperado = 0m,
                    UtilidadNeta = 1979500m,
                    CostoTransbank = 57358m,
                    CantidadVentasTransbank = 1284,
                    IsLocked
                });
            }
            else if (url.Contains("api/caja/movimientos"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Fecha = DateTime.Now, Descripcion = "Copec — Bencina", Monto = -18500m, Tipo = "GASTO", Categoria = "LOGISTICA", ImagenPath = (string?)null, ProductoId = (int?)null, Cantidad = 0, OrdenCargaId = (int?)null, CompraId = (int?)null, GastoRecurrenteId = (int?)null },
                    new { Id = 2, Fecha = DateTime.Now.AddDays(-1), Descripcion = "Inyección socio", Monto = 250000m, Tipo = "APORTE", Categoria = "APORTE_CAPITAL", ImagenPath = (string?)null, ProductoId = (int?)null, Cantidad = 0, OrdenCargaId = (int?)null, CompraId = (int?)null, GastoRecurrenteId = (int?)null }
                });
            }
            else if (url.Contains("api/OrdenCarga/historial"))
            {
                json = JsonSerializer.Serialize(Array.Empty<object>());
            }
            else if (url.Contains("api/caja/productos-simple"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Coca Cola", StockBodega = 50 },
                    new { Id = 2, Nombre = "Pepsi", StockBodega = 30 }
                });
            }
            else if (url.Contains("api/GastoRecurrente/pendientes"))
            {
                if (SinPendientes)
                {
                    json = JsonSerializer.Serialize(Array.Empty<object>());
                }
                else
                {
                    json = JsonSerializer.Serialize(new[]
                    {
                        new { GastoRecurrenteId = 1, Descripcion = "Teléfono máquinas", MontoEstimado = 30000m, Categoria = "INTERNET", MaquinaId = (int?)null, MaquinaNombre = (string?)null }
                    });
                }
            }
            else if (url.Contains("api/GastoRecurrente") && !url.Contains("pendientes"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Descripcion = "Arriendo bodega", MontoEstimado = 320000m, Categoria = "INFRA", Tipo = "GASTO", Activo = true, MaquinaId = (int?)null }
                });
            }
            else if (url.Contains("api/Ventas/lista-maquinas"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Máquina 001" },
                    new { Id = 2, Nombre = "Máquina 002" }
                });
            }
            else if (url.Contains("api/caja/valorizacion"))
            {
                json = JsonSerializer.Serialize(new { ValorBodega = 3728698m, ValorMaquinas = 1240000m });
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

    private class AuthenticatedAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "test") },
                "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private class FakeAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private class CajaTestHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<Caja>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
