using System;
using System.Collections.Generic;
using System.Security.Claims;
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
using VendingManager.Web.Components;
using VendingManager.Web.Layout;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class MachineOnlineMountTests : TestContext
{
    public MachineOnlineMountTests()
    {
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider, AuthenticatedAuthStateProvider>();
        Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();
        Services.AddSingleton<IMachineOnlineService>(new TestMachineOnlineService(new List<MachineOnlineStatus>
        {
            new(1, "Máquina A", true, DateTime.Now.AddMinutes(-2)),
            new(2, "Máquina B", false, DateTime.Now.AddMinutes(-15))
        }));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void MainLayout_RendersMachineOnlinePanel_OnInformeVentas()
    {
        var nav = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        nav?.NavigateTo("/informe-ventas");

        var cut = RenderComponent<MainLayoutTestHost>(parameters => parameters
            .Add(p => p.BodyContent, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p data-testid=\"body\">page body</p>"))));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("MÁQUINAS ONLINE"));
        cut.Markup.Should().Contain("Máquina A");
        cut.Markup.Should().Contain("Máquina B");
    }

    [Fact]
    public void MainLayout_RendersLeftPanelDeControlSidebar_OnHome()
    {
        var nav = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        nav?.NavigateTo("/");

        var cut = RenderComponent<MainLayoutTestHost>(parameters => parameters
            .Add(p => p.BodyContent, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p data-testid=\"body\">page body</p>"))));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("pdc-sidebar"));
        cut.Markup.Should().Contain("UNIDADES");
        cut.Markup.Should().Contain("Máquina 001");
    }

    [Fact]
    public void MainLayout_HidesMachineOnlinePanel_OnCaja()
    {
        var nav = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        nav?.NavigateTo("/caja");

        var cut = RenderComponent<MainLayoutTestHost>(parameters => parameters
            .Add(p => p.BodyContent, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p data-testid=\"body\">page body</p>"))));

        cut.Markup.Should().NotContain("MÁQUINAS ONLINE");
    }

    [Fact]
    public void MainLayout_HidesMachineOnlinePanel_OnTemplatesRecarga()
    {
        var nav = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        nav?.NavigateTo("/templates-recarga");

        var cut = RenderComponent<MainLayoutTestHost>(parameters => parameters
            .Add(p => p.BodyContent, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p data-testid=\"body\">page body</p>"))));

        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("MÁQUINAS ONLINE"));
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

    private class AuthenticatedAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test") }, "test");
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

    private class TestMachineOnlineService : IMachineOnlineService
    {
        private readonly IReadOnlyList<MachineOnlineStatus> _machines;

        public TestMachineOnlineService(IReadOnlyList<MachineOnlineStatus> machines)
        {
            _machines = machines;
        }

        public Task<IReadOnlyList<MachineOnlineStatus>> GetOnlineMachinesAsync(CancellationToken ct = default)
            => Task.FromResult(_machines);
    }
}
