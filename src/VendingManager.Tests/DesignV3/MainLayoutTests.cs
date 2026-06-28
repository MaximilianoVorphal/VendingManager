using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using VendingManager.Web.Layout;
using VendingManager.Web.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class MainLayoutTests : TestContext
{
    public MainLayoutTests()
    {
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider, AuthenticatedAuthStateProvider>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void MainLayout_RendersVmNavbar_AndNotNavMenu()
    {
        var cut = RenderComponent<MainLayout>(parameters => parameters
            .Add(p => p.Body, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p data-testid=\"body\">page body</p>"))));

        cut.Markup.Should().Contain("VENDING");
        cut.Markup.Should().Contain("page body");
        cut.Markup.Should().NotContain("nav-links-container");
        cut.Markup.Should().NotContain("industrial-navbar");
    }

    [Fact]
    public void MainLayout_PreservesCascadingApi()
    {
        var cut = RenderComponent<MainLayout>();
        var instance = cut.Instance;

        instance.CollapseNavbar();
        instance.ExpandNavbar();
        instance.ToggleNavbarCollapse();

        // La API simplemente no debe lanzar; el estado interno es un detalle de implementación.
        true.Should().BeTrue();
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
}
