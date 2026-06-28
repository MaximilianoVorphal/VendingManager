using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using VendingManager.Web.Layout;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class LoginLayoutTests : TestContext
{
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
        cut.Markup.Should().Contain("align-items:center");
        cut.Markup.Should().Contain("justify-content:center");
    }
}
