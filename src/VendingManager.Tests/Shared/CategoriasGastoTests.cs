namespace VendingManager.Tests.Shared;

using FluentAssertions;
using VendingManager.Shared;

/// <summary>
/// Unit tests for CategoriasGasto classification.
/// Verifies Estructurales set and EsGastoOperativoReal extension.
/// </summary>
public class CategoriasGastoTests
{
    // ── Estructurales set ────────────────────────────────────────────

    [Theory]
    [InlineData("RETIRO_CAPITAL", true)]
    [InlineData("retiro_capital", true)]          // OrdinalIgnoreCase
    [InlineData("DEVOLUCION_RENDICION", true)]
    [InlineData("devolucion_rendicion", true)]    // OrdinalIgnoreCase
    [InlineData("LOGISTICA", false)]
    [InlineData("GASTOS GENERALES", false)]
    [InlineData("MERMA", false)]
    [InlineData("", false)]
    public void Estructurales_Contains_IsCaseInsensitive(string categoria, bool expected)
    {
        CategoriasGasto.Estructurales.Contains(categoria).Should().Be(expected);
    }

    [Fact]
    public void Estructurales_HasExactlyTwoEntries()
    {
        CategoriasGasto.Estructurales.Count.Should().Be(2);
    }

    // ── EsGastoOperativoReal ─────────────────────────────────────────

    [Theory]
    [InlineData("LOGISTICA", true)]          // real operational expense
    [InlineData("PEAJES", true)]
    [InlineData("INFRA", true)]
    [InlineData("RETIRO_CAPITAL", false)]    // structural
    [InlineData("DEVOLUCION_RENDICION", false)] // structural
    [InlineData("", true)]                   // empty → not structural → considered real
    [InlineData(null, true)]                 // null → not structural → considered real
    public void EsGastoOperativoReal_ReturnsExpected(string? categoria, bool expected)
    {
        CategoriasGasto.EsGastoOperativoReal(categoria).Should().Be(expected);
    }
}
