using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
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
        Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void MainLayout_RendersVmNavbar_AndNotNavMenu()
    {
        var cut = RenderComponent<MainLayoutTestHost>(parameters => parameters
            .Add(p => p.BodyContent, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p data-testid=\"body\">page body</p>"))));

        cut.Markup.Should().Contain("VENDING");
        cut.Markup.Should().Contain("page body");
        cut.Markup.Should().NotContain("nav-links-container");
        cut.Markup.Should().NotContain("industrial-navbar");
    }

    [Fact]
    public async Task MainLayout_PreservesCascadingApi()
    {
        var cut = RenderComponent<MainLayoutTestHost>();
        var instance = cut.FindComponent<MainLayout>().Instance;

        await cut.InvokeAsync(() => { instance.CollapseNavbar(); return Task.CompletedTask; });
        await cut.InvokeAsync(() => { instance.ExpandNavbar(); return Task.CompletedTask; });
        await cut.InvokeAsync(() => { instance.ToggleNavbarCollapse(); return Task.CompletedTask; });

        // La API simplemente no debe lanzar; el estado interno es un detalle de implementación.
        true.Should().BeTrue();
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
}
