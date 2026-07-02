using FluentAssertions;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.Web;

public class ChileanAmountParserTests
{
    // ── Casos pedidos por el usuario (los tres formatos deben dar 340000) ─────

    [Theory]
    [InlineData("340.000", 340000)]
    [InlineData("340000", 340000)]
    [InlineData("340,000", 340000)]
    public void TryParse_Acepta_TresFormatosChilenos_ParaEntero(string input, decimal expected)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    // ── Otros enteros con separador de miles (período) ─────────────────────────

    [Theory]
    [InlineData("1.000", 1000)]
    [InlineData("12.500", 12500)]
    [InlineData("1.000.000", 1000000)]
    [InlineData("1.234.567", 1234567)]
    public void TryParse_PeriodoComoMiles(string input, decimal expected)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,000", 1000)]
    [InlineData("12,500", 12500)]
    [InlineData("1,000,000", 1000000)]
    [InlineData("1,234,567", 1234567)]
    public void TryParse_ComaComoMiles_Leniente(string input, decimal expected)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    // ── Decimales canónicos chilenos (coma como decimal) ──────────────────────

    [Theory]
    [InlineData("340,5", 340.5)]
    [InlineData("1.000,5", 1000.5)]
    [InlineData("1,5", 1.5)]
    public void TryParse_ComaComoDecimal(string input, decimal expected)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    // ── Decimales con punto (leniente, muy común al tipear) ───────────────────

    [Theory]
    [InlineData("340.5", 340.5)]
    [InlineData("1.5", 1.5)]
    [InlineData("0.5", 0.5)]
    [InlineData(".5", 0.5)]      // sin parte entera, se acepta como decimal
    [InlineData(",5", 0.5)]      // idem con coma
    public void TryParse_PuntoComoDecimal_Leniente(string input, decimal expected)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    // ── Decimales con múltiples puntos: se interpretan como miles + miles (regla "todos miles") ──

    [Theory]
    [InlineData("1.000.5", 10005)]    // dos puntos → todos miles → 10005
    [InlineData("1.000.50", 100050)]
    public void TryParse_MultiplesPuntos_TodosMiles(string input, decimal expected)
    {
        // Si el usuario quiere 1.000,5 con punto decimal, debe usar formato mixto:
        //   "1.000,5" → 1000.5
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    // ── Formato mixto europeo (puntos miles + coma decimal) ───────────────────

    [Theory]
    [InlineData("1.000,5", 1000.5)]
    [InlineData("1.234.567,89", 1234567.89)]
    public void TryParse_FormatoEuropeoMixto(string input, decimal expected)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    // ── Bordes: vacío, blanco, símbolos $ y -, espacios ───────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_EntradaVaciaOFalla_RetornaFalse(string? input)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeFalse();
        value.Should().Be(0m);
    }

    [Fact]
    public void TryParse_ConSimboloPesos_LoIgnora()
    {
        ChileanAmountParser.TryParse("$340.000", out var value).Should().BeTrue();
        value.Should().Be(340000m);
    }

    [Fact]
    public void TryParse_ConSignoNegativo()
    {
        ChileanAmountParser.TryParse("-340.000", out var value).Should().BeTrue();
        value.Should().Be(-340000m);
    }

    [Fact]
    public void TryParse_ConEspaciosAlrededor()
    {
        ChileanAmountParser.TryParse("  340.000  ", out var value).Should().BeTrue();
        value.Should().Be(340000m);
    }

    // ── Rechazo de entradas inválidas ────────────────────────────────────────

    [Theory]
    [InlineData("abc")]
    [InlineData("12.5.6.78x")]
    [InlineData("$abc")]
    [InlineData("--340")]
    [InlineData(".")]
    [InlineData(",")]
    public void TryParse_EntradasInvalidas_RetornaFalse(string input)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeFalse();
        value.Should().Be(0m);
    }

    // ── Sanity: múltiples separadores del mismo tipo se tratan como miles ────

    [Theory]
    [InlineData("12.34.56.78", 12345678)]
    [InlineData("12,34,56,78", 12345678)]
    public void TryParse_MultiplesSeparadoresMismoTipo_TodosMiles(string input, decimal expected)
    {
        ChileanAmountParser.TryParse(input, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    // ── El bug específico que reportó el usuario ─────────────────────────────

    [Fact]
    public void BugReport_Usuario_340Punto000_NoSeConvierteEn340()
    {
        // Antes del fix: type=number + @bind=decimal interpretaba "340.000" como 340.0
        // Después del fix: debe interpretarse como trescientos cuarenta mil.
        ChileanAmountParser.TryParse("340.000", out var value).Should().BeTrue();
        value.Should().Be(340000m);
        value.Should().NotBe(340m);
        value.Should().NotBe(340.000m);
    }
}
