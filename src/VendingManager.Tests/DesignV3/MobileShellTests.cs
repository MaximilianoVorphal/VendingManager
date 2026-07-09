using Bunit;
using FluentAssertions;
using VendingManager.Web.Components.Mobile;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class MobileShellTests : TestContext
{
    [Fact]
    public void MobileShell_RendersChildContent()
    {
        var cut = RenderComponent<MobileShell>(parameters => parameters
            .AddChildContent("<p>Hello Mobile</p>"));

        cut.Markup.Should().Contain("Hello Mobile");
    }

    [Fact]
    public void MobileShell_RendersVmMobileShellWrapper()
    {
        var cut = RenderComponent<MobileShell>(parameters => parameters
            .AddChildContent("content"));

        cut.Find(".vm-mobile-shell").Should().NotBeNull();
    }

    [Fact]
    public void MobileShell_WrapsChildContentInsideShellDiv()
    {
        var cut = RenderComponent<MobileShell>(parameters => parameters
            .AddChildContent("<span>inner</span>"));

        var shell = cut.Find(".vm-mobile-shell");
        shell.QuerySelector("span").TextContent.Should().Be("inner");
    }

    [Fact]
    public void MobileShell_MultipleChildElements_AllRendered()
    {
        var cut = RenderComponent<MobileShell>(parameters => parameters
            .AddChildContent("<div>first</div>")
            .AddChildContent("<div>second</div>"));

        cut.Markup.Should().Contain("first");
        cut.Markup.Should().Contain("second");
    }
}
