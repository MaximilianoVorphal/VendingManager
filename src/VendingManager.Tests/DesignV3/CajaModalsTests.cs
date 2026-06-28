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

public class CajaModalsTests : TestContext
{
    private readonly CajaMockHttpMessageHandler _mockHandler;

    public CajaModalsTests()
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
    public void Caja_RegistroModal_OpensOnNuevoMovimientoClick()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("NUEVO MOVIMIENTO"));

        var nuevoButton = cut.FindAll("button").First(b => b.TextContent.Contains("NUEVO MOVIMIENTO"));
        nuevoButton.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("REGISTRAR MOVIMIENTO");
            cut.Markup.Should().Contain("MONTO ($)");
        });
    }

    [Fact]
    public async Task Caja_RegistroModal_Registrar_CallsAddMovementHandler()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("NUEVO MOVIMIENTO"));

        var nuevoButton = cut.FindAll("button").First(b => b.TextContent.Contains("NUEVO MOVIMIENTO"));
        nuevoButton.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("REGISTRAR MOVIMIENTO"));

        var descripcionInput = cut.FindComponents<VmInput>().First(i => i.Instance.Label == "DESCRIPCIÓN");
        await cut.InvokeAsync(() => descripcionInput.Instance.ValueChanged.InvokeAsync("Copec test"));

        var montoInput = cut.FindComponents<VmInput>().First(i => i.Instance.Label == "MONTO ($)");
        await cut.InvokeAsync(() => montoInput.Instance.ValueChanged.InvokeAsync("15000"));

        var registrarButton = cut.FindAll("button").First(b => b.TextContent.Contains("REGISTRAR"));
        registrarButton.Click();

        _mockHandler.RegistrarCalled.Should().BeTrue();
    }

    [Fact]
    public void Caja_DetalleModal_OpensOnRowClick()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Copec — Bencina"));

        var row = cut.FindAll("tr").First(r => r.TextContent.Contains("Copec — Bencina"));
        row.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("DETALLE DE MOVIMIENTO");
            cut.Markup.Should().Contain("Copec — Bencina");
            cut.Markup.Should().Contain("LOGISTICA");
        });
    }

    [Fact]
    public void Caja_DetalleModal_Cerrar_ClosesModal()
    {
        var cut = RenderComponent<CajaTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Copec — Bencina"));

        var row = cut.FindAll("tr").First(r => r.TextContent.Contains("Copec — Bencina"));
        row.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE MOVIMIENTO"));

        var cerrarButton = cut.FindAll("button").First(b => b.TextContent.Contains("CERRAR"));
        cerrarButton.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("DETALLE DE MOVIMIENTO"));
    }

    private class CajaMockHttpMessageHandler : HttpMessageHandler
    {
        public bool SinPendientes { get; set; }
        public bool IsLocked { get; set; }
        public bool RegistrarCalled { get; set; }

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
            else if (url.Contains("api/caja/registrar"))
            {
                RegistrarCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":99}")
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
