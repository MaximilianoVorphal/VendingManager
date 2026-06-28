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
using Bunit.TestDoubles;
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

public class ReportesModalTests : TestContext
{
    private readonly ReportesMockHttpMessageHandler _mockHandler;

    public ReportesModalTests()
    {
        _mockHandler = new ReportesMockHttpMessageHandler();
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
    public void FinancieroModal_OpensWithVmCard_AndVmButtonClose()
    {
        var cut = RenderReportes();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE OPERACIONES"));

        var button = cut.FindComponents<VmButton>().First(b => b.Instance.Icon == "bi-graph-up-arrow");
        button.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindComponents<VmCard>();
            cards.Should().Contain(c => c.Instance.Header == "ESTADO DE RESULTADOS");
        });

        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("CERRAR"))
            .Should().BeTrue();
    }

    [Fact]
    public void FantasmasModal_OpensWithVmCard_AndVmButtonClose()
    {
        var cut = RenderReportes();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE OPERACIONES"));

        var kpi = cut.FindComponents<VmKpiCard>().First(k => k.Instance.Header == "DIFERENCIAS TRANSBANK");
        kpi.Find("div").Click();

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindComponents<VmCard>();
            cards.Should().Contain(c => c.Instance.Header == "DIFERENCIAS TRANSBANK");
        });

        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("CERRAR"))
            .Should().BeTrue();
    }

    [Fact]
    public void SyncModal_OpensWithVmCard_AndVmButtonActions()
    {
        var cut = RenderReportes();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE OPERACIONES"));

        var button = cut.FindComponents<VmButton>().First(b => b.Instance.Icon == "bi-cloud-arrow-down-fill");
        button.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindComponents<VmCard>();
            cards.Should().Contain(c => c.Instance.Header == "SINCRONIZACIÓN OURVEND");
        });

        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("CANCELAR"))
            .Should().BeTrue();
        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("EJECUTAR"))
            .Should().BeTrue();
    }

    [Fact]
    public void FiltroProductosModal_OpensWithVmCard_AndVmButtonActions()
    {
        var cut = RenderReportes();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE OPERACIONES"));

        var button = cut.FindComponents<VmButton>().First(b => b.Instance.Icon == "bi-funnel-fill");
        button.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindComponents<VmCard>();
            cards.Should().Contain(c => c.Instance.Header == "FILTRAR PRODUCTOS");
        });

        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("SELECCIONAR TODOS"))
            .Should().BeTrue();
        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("DESELECCIONAR TODOS"))
            .Should().BeTrue();
        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("EXCLUIR VACÍOS"))
            .Should().BeTrue();
        cut.FindComponents<VmButton>()
            .Any(b => b.Markup.Contains("APLICAR"))
            .Should().BeTrue();
    }

    [Fact]
    public void AdminModal_OpensWithVmCard_DangerButtonDisabledUntilBorrar()
    {
        var nav = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        nav?.NavigateTo("http://localhost/informe-ventas?maquinaId=1");

        var cut = RenderReportes();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE OPERACIONES"));

        var button = cut.FindComponents<VmButton>().First(b => b.Instance.Icon == "bi-gear-fill text-danger");
        button.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindComponents<VmCard>();
            cards.Should().Contain(c => c.Instance.Header == "ZONA PELIGROSA");
        });

        var deleteButton = cut.FindComponents<VmButton>()
            .First(b => b.Markup.Contains("ELIMINAR Y RESTAURAR STOCK"));
        deleteButton.Instance.Disabled.Should().BeTrue();

        var input = cut.Find("input[placeholder*=\"BORRAR\"]");
        input.Change("BORRAR");

        deleteButton = cut.FindComponents<VmButton>()
            .First(b => b.Markup.Contains("ELIMINAR Y RESTAURAR STOCK"));
        deleteButton.Instance.Disabled.Should().BeFalse();
    }

    private IRenderedFragment RenderReportes()
    {
        return RenderComponent<ReportesTestHost>();
    }

    private class ReportesMockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("lista-maquinas"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Máquina 001" },
                    new { Id = 2, Nombre = "Máquina 002" }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("TemplateRecarga"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Template A", Periodos = Array.Empty<object>() },
                    new { Id = 2, Nombre = "Template B", Periodos = Array.Empty<object>() }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("reporte-rango"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    TotalVentas = 3,
                    MontoTotal = 3000m,
                    MontoPagado = 2000m,
                    MontoPendiente = 1000m,
                    MontoPhantom = 500m,
                    GananciaTotal = 800m,
                    Detalle = new[]
                    {
                        new { FechaRaw = DateTime.Now, Maquina = "Máquina 001", Monto = 1000m, Estado = "Pagado", Slot = "10", Producto = "Coca Cola", Ganancia = 300m },
                        new { FechaRaw = DateTime.Now.AddMinutes(-1), Maquina = "Máquina 002", Monto = 1000m, Estado = "Pendiente", Slot = "20", Producto = "Pepsi", Ganancia = 100m },
                        new { FechaRaw = DateTime.Now.AddMinutes(-2), Maquina = "Máquina 001", Monto = 1000m, Estado = "Pagado", Slot = "30", Producto = "Agua", Ganancia = -50m }
                    },
                    Fantasmas = new[]
                    {
                        new { FechaRaw = DateTime.Now.AddMinutes(-5), Maquina = "Máquina 001", Monto = 150m }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("informe-financiero"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    VentasTotales = 2000m,
                    CostoVentas = 800m,
                    MargenBruto = 1200m,
                    MargenPorcentaje = 60m,
                    GastosOperativos = 200m,
                    UtilidadNeta = 1000m
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
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

    private class ReportesTestHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<Reportes>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
