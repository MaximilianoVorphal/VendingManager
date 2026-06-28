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

public class ReportesShellTests : TestContext
{
    private readonly ReportesMockHttpMessageHandler _mockHandler;

    public ReportesShellTests()
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
    public void ControlPanel_RendersInsideVmCard_WithDarkHeader()
    {
        var cut = RenderComponent<ReportesTestHost>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("CONTROL DE INFORME");
            cut.Markup.Should().Contain("var(--ink-900)");
        });

        var vmCards = cut.FindComponents<VmCard>();
        vmCards.Should().Contain(c =>
            c.Instance.Header == "CONTROL DE INFORME" &&
            c.Instance.HeaderVariant == "dark");

        var selects = cut.FindComponents<VmSelect>();
        selects.Count.Should().BeGreaterOrEqualTo(2);
        selects.Should().Contain(s => s.Instance.Label == "TEMPLATE (OPCIONAL):");
        selects.Should().Contain(s => s.Instance.Label == "UNIDAD:");

        var dateInputs = cut.FindComponents<VmInput>().Where(i => i.Instance.Type == "date");
        dateInputs.Count().Should().Be(2);
    }

    [Fact]
    public void Kpis_UseVmKpiCard()
    {
        var cut = RenderComponent<ReportesTestHost>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("UTILIDAD (REAL / PAGADA)");
            cut.Markup.Should().Contain("VENTAS CONFIRMADAS");
            cut.Markup.Should().Contain("DIFERENCIAS TRANSBANK");
            cut.Markup.Should().Contain("PENDIENTE / CASH");
        });

        var kpis = cut.FindComponents<VmKpiCard>();
        kpis.Count.Should().Be(4);
    }

    [Fact]
    public void MainTable_UsesIndustrialPattern_WithVmBadges()
    {
        var cut = RenderComponent<ReportesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE OPERACIONES"));

        cut.Markup.Should().Contain("var(--ink-900)");
        cut.Markup.Should().Contain("var(--signal-success)");
        cut.Markup.Should().Contain("var(--signal-warning)");

        var badges = cut.FindComponents<VmBadge>();
        badges.Count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void TableRows_ApplyTints_ForPendingAndNegativeProfit()
    {
        var cut = RenderComponent<ReportesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("DETALLE DE OPERACIONES"));

        cut.Markup.Should().Contain("var(--tint-pending)");
        cut.Markup.Should().Contain("var(--tint-danger)");
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
                    Fantasmas = Array.Empty<object>()
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
