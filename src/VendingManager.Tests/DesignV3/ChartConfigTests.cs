using System.IO;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class ChartConfigTests
{
    private static string HomeRazorPath =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "Home.razor"));

    [Fact]
    public void HomeRazor_BarChartUsesInk900Hex()
    {
        var source = File.ReadAllText(HomeRazorPath);
        source.Should().Contain("#000000");
        source.Should().Contain("equivalent to var(--ink-900)");
    }

    [Fact]
    public void HomeRazor_DoughnutChartUsesSignalColors()
    {
        var source = File.ReadAllText(HomeRazorPath);
        source.Should().Contain("#198754");
        source.Should().Contain("#dc3545");
    }

    [Fact]
    public void HomeRazor_BarChartAxesUseIndustrialPalette()
    {
        var source = File.ReadAllText(HomeRazorPath);
        source.Should().Contain("#e0e0e0");
        source.Should().Contain("#333333");
    }

    [Fact]
    public void HomeRazor_ChartConfig_HasHexTokenEquivalenceComment()
    {
        var source = File.ReadAllText(HomeRazorPath);
        source.Should().Contain("Chart.js canvas cannot read CSS custom properties");
        source.Should().ContainAll(new[]
        {
            "var(--ink-900)",
            "var(--signal-success)",
            "var(--signal-danger)",
            "var(--line-200)",
            "var(--ink-700)"
        });
    }
}
