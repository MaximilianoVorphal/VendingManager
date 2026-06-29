using System;
using System.Globalization;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class TemplatesRecargaPriceFormatTests
{
    [Theory]
    [InlineData(0, "$—")]
    [InlineData(700, "$700")]
    [InlineData(1200, "$1.200")]
    [InlineData(1234567, "$1.234.567")]
    public void FormatSlotPrice_ReturnsCorrectFormat(decimal price, string expected)
    {
        // The format function from Recarga.dc.html:
        // const fmt = (n) => '$' + n.toLocaleString('es-CL');
        // In C#: price.ToString("C0", new CultureInfo("es-CL"))
        // But 0 displays as "$0", not "$—". The "$—" is shown when price <= 0.
        var result = FormatSlotPrice(price);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "$1")]
    [InlineData(10, "$10")]
    [InlineData(100, "$100")]
    [InlineData(1000, "$1.000")]
    [InlineData(10000, "$10.000")]
    [InlineData(100000, "$100.000")]
    public void FormatSlotPrice_VariousSizes(decimal price, string expected)
    {
        var result = FormatSlotPrice(price);
        result.Should().Be(expected);
    }

    // Pure function extracted from TemplatesRecarga.razor slot price rendering
    private static string FormatSlotPrice(decimal price)
    {
        if (price <= 0)
            return "$—";
        return price.ToString("C0", new CultureInfo("es-CL"));
    }
}
