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
using VendingManager.Web.Components;
using VendingManager.Web.Pages;
using VendingManager.Web.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class TemplatesRecargaShellTests : TestContext
{
    private readonly TemplatesMockHttpMessageHandler _mockHandler;

    public TemplatesRecargaShellTests()
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
    public void Header_RendersInsideVmCard_WithDarkHeader()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("TEMPLATES DE RECARGA");
            cut.Markup.Should().Contain("var(--ink-900)");
        });

        var vmCards = cut.FindComponents<VmCard>();
        vmCards.Should().Contain(c =>
            c.Instance.Header == "TEMPLATES DE RECARGA" &&
            c.Instance.HeaderVariant == "dark");

        var buttons = cut.FindComponents<VmButton>();
        buttons.Should().Contain(b => cut.Markup.Contains("SINCRONIZAR TODO"));
        buttons.Should().Contain(b => cut.Markup.Contains("PENDIENTES"));
        buttons.Should().Contain(b => cut.Markup.Contains("NUEVO TEMPLATE"));
    }

    [Fact]
    public void TemplateList_RendersAsVmCards()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Template Activo"));

        var vmCards = cut.FindComponents<VmCard>();
        vmCards.Should().Contain(c => c.Instance.Header == "Template Activo");
        vmCards.Should().Contain(c => c.Instance.Header == "Template Terminado");
    }

    [Fact]
    public void TemplateList_PendingCount_RendersAsVmBadgeWarning()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Template Activo"));

        var badges = cut.FindComponents<VmBadge>();
        badges.Should().Contain(b =>
            b.Instance.Variant == "warning" &&
            b.Markup.Contains("1") &&
            b.Markup.Contains("PENDIENTE"));
    }

    [Fact]
    public void CrearModal_OpensOnNuevoTemplateClick()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("NUEVO TEMPLATE"));

        var nuevoButton = cut.FindComponents<VmButton>()
            .First(b => b.Markup.Contains("NUEVO TEMPLATE"));
        nuevoButton.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("NUEVO TEMPLATE DE RECARGA");
            cut.Markup.Should().Contain("NOMBRE DEL TEMPLATE");
            cut.Markup.Should().Contain("DESCRIPCIÓN (OPCIONAL)");
        });

        var inputs = cut.FindComponents<VmInput>();
        inputs.Should().Contain(i => i.Instance.Label == "NOMBRE DEL TEMPLATE");
        inputs.Should().Contain(i => i.Instance.Label == "DESCRIPCIÓN (OPCIONAL)");

        var agregarButton = cut.FindComponents<VmButton>().First(b => b.Markup.Contains("Agregar Máquina"));
        agregarButton.Find("button").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("MÁQUINA"));

        var selects = cut.FindComponents<VmSelect>();
        selects.Should().Contain(s => s.Instance.Label == "MÁQUINA");

        var buttons = cut.FindComponents<VmButton>();
        buttons.Should().Contain(b => b.Markup.Contains("GUARDAR TEMPLATE"));
        buttons.Should().Contain(b => b.Markup.Contains("CANCELAR"));
    }

    [Fact]
    public void EditarModal_SlotEditor_UsesSlotCard_NotVmSlotCard()
    {
        var cut = RenderComponent<TemplatesTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Template Activo"));

        var editButton = cut.FindAll("button")
            .First(b => b.InnerHtml.Contains("bi-pencil"));
        editButton.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("EDITAR TEMPLATE");
            cut.Markup.Should().Contain("PERÍODOS POR MÁQUINA");
        });

        var slotCards = cut.FindComponents<SlotCard>();
        slotCards.Should().NotBeEmpty();

        var vmSlotCards = cut.FindComponents<VmSlotCard>();
        vmSlotCards.Should().BeEmpty();
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
