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

public class TemplatesRecargaModalTests : TestContext
{
    private readonly TemplatesMockHttpMessageHandler _mockHandler;

    public TemplatesRecargaModalTests()
    {
        _mockHandler = new TemplatesMockHttpMessageHandler();
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
    public void EliminarModal_OpensOnEliminarClick_WithDangerAndOutlineButtons()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Template Activo"));

        var eliminarButton = cut.FindComponents<VmButton>()
            .First(b => b.Markup.Contains("ELIMINAR"));
        eliminarButton.Find("button").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("ELIMINAR TEMPLATE"));

        var vmCards = cut.FindComponents<VmCard>();
        vmCards.Should().Contain(c =>
            c.Instance.Header == "ELIMINAR TEMPLATE" &&
            c.Instance.HeaderVariant == "danger");

        var buttons = cut.FindComponents<VmButton>();
        buttons.Should().Contain(b =>
            b.Instance.Variant == "outline" && b.Markup.Contains("CANCELAR"));
        buttons.Should().Contain(b =>
            b.Instance.Variant == "danger" && b.Markup.Contains("SÍ, ELIMINAR"));
    }

    [Fact]
    public void SincronizarGlobalModal_OpensOnSyncAllClick_WithDarkAndOutlineButtons()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SINCRONIZAR TODO"));

        var syncAllButton = cut.FindComponents<VmButton>()
            .First(b => b.Markup.Contains("SINCRONIZAR TODO") && b.Instance.Variant == "dark");
        syncAllButton.Find("button").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SINCRONIZAR TODOS LOS TEMPLATES"));

        var vmCards = cut.FindComponents<VmCard>();
        vmCards.Should().Contain(c =>
            c.Instance.Header == "SINCRONIZAR TODOS LOS TEMPLATES" &&
            c.Instance.HeaderVariant == "dark");

        var buttons = cut.FindComponents<VmButton>();
        buttons.Should().Contain(b =>
            b.Instance.Variant == "outline" && b.Markup.Contains("CANCELAR"));
        buttons.Should().Contain(b =>
            b.Instance.Variant == "dark" && b.Markup.Contains("SÍ, SINCRONIZAR TODO"));
    }

    [Fact]
    public void SincronizarTemplateModal_OpensOnCardSyncClick_WithWarningAndOutlineButtons()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Template Activo"));

        var syncButton = cut.FindComponents<VmButton>()
            .First(b => b.Markup.Contains("SINCRONIZAR TODO") && b.Instance.Variant == "warning");
        syncButton.Find("button").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("CONFIRMAR SINCRONIZACIÓN"));

        var vmCards = cut.FindComponents<VmCard>();
        vmCards.Should().Contain(c =>
            c.Instance.Header == "CONFIRMAR SINCRONIZACIÓN" &&
            c.Instance.HeaderVariant == "dark");

        var buttons = cut.FindComponents<VmButton>();
        buttons.Should().Contain(b =>
            b.Instance.Variant == "outline" && b.Markup.Contains("CANCELAR"));
        buttons.Should().Contain(b =>
            b.Instance.Variant == "warning" && b.Markup.Contains("SÍ, SINCRONIZAR"));
    }

    [Fact]
    public void PendientesModal_OpensOnPendientesClick_WithWarningBadgeAndDarkConfigurar()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("PENDIENTES"));

        var pendientesButton = cut.FindComponents<VmButton>()
            .First(b => b.Markup.Contains("PENDIENTES") && b.Instance.Variant == "warning");
        pendientesButton.Find("button").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("SLOTS PENDIENTES DE CONFIGURAR"));

        var vmCards = cut.FindComponents<VmCard>();
        vmCards.Should().Contain(c =>
            c.Instance.Header == "SLOTS PENDIENTES DE CONFIGURAR" &&
            c.Instance.HeaderVariant == "dark");

        var badges = cut.FindComponents<VmBadge>();
        badges.Should().Contain(b =>
            b.Instance.Variant == "warning" && b.Markup.Contains("PENDIENTE"));

        var buttons = cut.FindComponents<VmButton>();
        buttons.Should().Contain(b =>
            b.Instance.Variant == "dark" && b.Markup.Contains("CONFIGURAR"));
    }

    private class TemplatesMockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("api/Ventas/lista-maquinas"))
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

            if (url.Contains("api/Ventas/lista-productos"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Coca Cola" },
                    new { Id = 2, Nombre = "Pepsi" }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("api/TemplateRecarga/maquina/") && url.Contains("/slots"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        NumeroSlot = "A1",
                        ProductoId = (int?)null,
                        ProductoNombre = "",
                        CantidadInicial = 0,
                        CapacidadSlot = 5,
                        Estado = 1
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("api/TemplateRecarga"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        Id = 1,
                        Nombre = "Template Activo",
                        Descripcion = "Template con slots pendientes",
                        FechaCreacion = DateTime.Now,
                        Estado = 0,
                        EsActivo = false,
                        Periodos = new[]
                        {
                            new
                            {
                                Id = 1,
                                TemplateRecargaId = 1,
                                MaquinaId = 1,
                                MaquinaNombre = "Máquina 001",
                                FechaRecarga = DateTime.Now,
                                FechaFin = DateTime.Now.AddDays(7),
                                TieneFotoGuia = false,
                                TieneFotoOcr = false,
                                SnapshotSlots = new object[]
                                {
                                    new
                                    {
                                        Id = 1,
                                        NumeroSlot = "A1",
                                        ProductoId = (int?)null,
                                        ProductoNombre = "",
                                        CantidadInicial = 0,
                                        CapacidadSlot = 5,
                                        Estado = 1
                                    }
                                }
                            }
                        }
                    },
                    new
                    {
                        Id = 2,
                        Nombre = "Template Terminado",
                        Descripcion = "Template finalizado",
                        FechaCreacion = DateTime.Now,
                        Estado = 2,
                        EsActivo = true,
                        Periodos = new[]
                        {
                            new
                            {
                                Id = 2,
                                TemplateRecargaId = 2,
                                MaquinaId = 2,
                                MaquinaNombre = "Máquina 002",
                                FechaRecarga = DateTime.Now.AddDays(-7),
                                FechaFin = DateTime.Now,
                                TieneFotoGuia = false,
                                TieneFotoOcr = false,
                                SnapshotSlots = new object[] { }
                            }
                        }
                    }
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

    private class TemplatesTestHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TemplatesRecarga>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
