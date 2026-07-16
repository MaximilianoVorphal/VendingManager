using System;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using VendingManager.Web.Layout;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class LoginLayoutTests : TestContext
{
    public LoginLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LoginLayout_UsesPaper100Canvas()
    {
        var cut = RenderComponent<LoginLayout>();

        cut.Markup.Should().Contain("var(--paper-100)");
    }

    [Fact]
    public void LoginLayout_CentersBodyContent()
    {
        var cut = RenderComponent<LoginLayout>(parameters => parameters
            .Add(p => p.Body, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p data-testid=\"body\">page body</p>"))));

        cut.Markup.Should().Contain("page body");

        // Centering moved from an inline style to the .login-root class,
        // whose embedded <style> block uses spaced formatting.
        cut.Find("div.login-root").Should().NotBeNull();
        cut.Markup.Should().MatchRegex(@"display:\s*grid");
        cut.Markup.Should().MatchRegex(@"place-items:\s*center");
    }

    [Fact]
    public void LoginLayout_HidesLoader_OnFirstRender()
    {
        var cut = RenderComponent<LoginLayout>();

        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations
                .Should()
                .ContainSingle(inv => inv.Identifier == "hideLoader");
        }, TimeSpan.FromSeconds(5));

        // S-2: subsequent renders do NOT re-invoke hideLoader
        cut.Render();
        JSInterop.Invocations
            .Count(inv => inv.Identifier == "hideLoader")
            .Should().Be(1, "hideLoader must be invoked exactly once on first render");
    }
}
