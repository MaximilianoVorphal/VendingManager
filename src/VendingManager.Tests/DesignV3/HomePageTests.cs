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
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Layout;
using VendingManager.Web.Pages;
using Xunit;

using InputFileContent = Bunit.InputFileContent;

namespace VendingManager.Tests.DesignV3;

public class HomePageTests : TestContext
{
    private readonly HomeMockHttpMessageHandler _mockHandler;

    public HomePageTests()
    {
        _mockHandler = new HomeMockHttpMessageHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Home_RendersThreeVmKpiCards()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Reporte Diario");
            cut.Markup.Should().Contain("Reporte Semanal");
            cut.Markup.Should().Contain("Reporte Mensual");
        });

        // VmKpiCard is built on VmCard, whose container uses the industrial ink border.
        cut.Markup.Should().Contain("border:2px solid var(--ink-900)");
    }

    [Fact]
    public void Home_CriticalStockAlarm_UsesDangerVariant()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("ALERTA DE STOCK CRÍTICO"));

        cut.Markup.Should().Contain("var(--signal-danger)");
    }

    [Fact]
    public void Home_UploadModal_UsesVmInputAndVmButton()
    {
        var cut = RenderComponent<Home>();

        cut.WaitForAssertion(() => cut.FindAll("input[type=\"file\"]").Count.Should().Be(2));

        var file = InputFileContent.CreateFromText("dummy", "ventas.xls");
        var inputFile = cut.FindComponent<InputFile>();
        inputFile.UploadFiles(file);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("input[type=\"datetime-local\"]").Count.Should().Be(1);
            var buttons = cut.FindAll("button");
            buttons.Any(b => b.TextContent.Contains("SUBIR AHORA")).Should().BeTrue();
            buttons.Any(b => b.TextContent.Contains("CANCELAR")).Should().BeTrue();
        });
    }

    [Fact]
    public void Home_RendersPanelDeControlSidebar_WhenInsideLayout()
    {
        // This test renders Home within MainLayout to verify the sidebar appears
        Services.AddAuthorizationCore();
        Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(
            new MachineOnlineMountTestsHelper.AuthenticatedAuthStateProvider());
        Services.AddSingleton<IAuthorizationService>(
            new MachineOnlineMountTestsHelper.FakeAuthorizationService());

        var nav = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        nav?.NavigateTo("/");

        var cut = RenderComponent<MainLayoutTestHost>(parameters => parameters
            .Add(p => p.BodyContent, (RenderFragment)((RenderTreeBuilder builder) =>
            {
                builder.OpenComponent<Home>(0);
                builder.CloseComponent();
            })));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("pdc-sidebar");
            cut.Markup.Should().Contain("UNIDADES");
            cut.Markup.Should().Contain("Todas");
            cut.Markup.Should().Contain("Máquina 001");
            cut.Markup.Should().Contain("Máquina 002");
            cut.Markup.Should().Contain("Máquina 003");
        });
    }

    private class MainLayoutTestHost : ComponentBase
    {
        [Parameter] public RenderFragment? BodyContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<MainLayout>(2);
                childBuilder.AddAttribute(3, "Body", BodyContent);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private class MachineOnlineMountTestsHelper
    {
        public class AuthenticatedAuthStateProvider : AuthenticationStateProvider
        {
            public override Task<AuthenticationState> GetAuthenticationStateAsync()
            {
                var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "test");
                return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
            }
        }

        public class FakeAuthorizationService : IAuthorizationService
        {
            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource,
                IEnumerable<IAuthorizationRequirement> requirements)
                => Task.FromResult(AuthorizationResult.Success());

            public Task<AuthorizationResult> AuthorizeAsync(
                ClaimsPrincipal user, object? resource, string policyName)
                => Task.FromResult(AuthorizationResult.Success());
        }
    }

    private class HomeMockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            string json;

            if (url.Contains("lista-maquinas"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Máquina 001" },
                    new { Id = 2, Nombre = "Máquina 002" }
                });
            }
            else if (url.Contains("dashboard-stats"))
            {
                json = JsonSerializer.Serialize(new
                {
                    Hoy = new { VentaTotal = 100000m, PagadoTB = 80000m, Pendiente = 20000m, CantidadVentas = 10 },
                    Semana = new { VentaTotal = 700000m, PagadoTB = 600000m, Pendiente = 100000m, CantidadVentas = 70 },
                    Mes = new { VentaTotal = 3000000m, PagadoTB = 2500000m, Pendiente = 500000m, CantidadVentas = 300 },
                    CantidadStockCritico = 5
                });
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

}
