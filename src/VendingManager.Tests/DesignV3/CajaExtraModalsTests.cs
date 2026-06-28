using System;
using System.Collections.Generic;
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

public class CajaExtraModalsTests : TestContext
{
    private readonly CajaMockHttpMessageHandler _mockHandler;

    public CajaExtraModalsTests()
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

    [Fact]
    public void Caja_GastosFijosModal_OpensOnButtonClick()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("GASTOS FIJOS"));

        var button = cut.FindAll("button").First(b => b.TextContent.Contains("GASTOS FIJOS"));
        button.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("CONFIGURACIÓN DE GASTOS FIJOS MENSUALES");
            cut.Markup.Should().Contain("Arriendo bodega");
        });
    }

    [Fact]
    public void Caja_GastosFijosModal_AgregarButton_ShowsAddForm()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("GASTOS FIJOS"));

        cut.FindAll("button").First(b => b.TextContent.Contains("GASTOS FIJOS")).Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("CONFIGURACIÓN DE GASTOS FIJOS MENSUALES"));

        var agregar = cut.FindAll("button").First(b => b.TextContent.Trim().Contains("AGREGAR GASTO FIJO"));
        agregar.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("NUEVO GASTO FIJO");
            cut.FindComponents<VmInput>().Should().Contain(i => i.Instance.Label == "DESCRIPCIÓN");
            cut.FindComponents<VmInput>().Should().Contain(i => i.Instance.Label == "MONTO ESTIMADO MENSUAL ($)");
        });
    }

    [Fact]
    public void Caja_PnlModal_OpensOnButtonClick()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("ESTADO DE RESULTADOS"));

        var button = cut.FindAll("button").First(b => b.TextContent.Contains("ESTADO DE RESULTADOS"));
        button.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("ESTADO DE RESULTADOS");
            cut.Markup.Should().Contain("INDICADORES DE CAPITAL");

            var kpis = cut.FindComponents<VmKpiCard>();
            kpis.Count.Should().Be(7); // 4 shell KPIs + 3 P&L KPIs
            kpis.Should().Contain(k => k.Instance.Header == "INGRESOS");
            kpis.Should().Contain(k => k.Instance.Header == "GASTOS");
            kpis.Should().Contain(k => k.Instance.Header == "UTILIDAD");
        });
    }

    [Fact]
    public void Caja_PnlModal_UsesVmCardForSections()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("ESTADO DE RESULTADOS"));

        cut.FindAll("button").First(b => b.TextContent.Contains("ESTADO DE RESULTADOS")).Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("INDICADORES DE CAPITAL"));

        var cards = cut.FindComponents<VmCard>();
        cards.Should().Contain(c => c.Instance.Header == "INDICADORES DE CAPITAL (SNAPSHOT ACTUAL)");
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
