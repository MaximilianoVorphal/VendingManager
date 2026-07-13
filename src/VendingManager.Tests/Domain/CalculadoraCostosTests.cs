namespace VendingManager.Tests.Domain;

using FluentAssertions;
using VendingManager.Core.Domain;

/// <summary>
/// Pure-math unit tests for CalculadoraCostos — no EF, no DI, no side effects.
/// Verifies ApplyPurchase and RevertPurchase logic per ADR-012.
/// </summary>
public class CalculadoraCostosTests
{
    // ── ApplyPurchase ────────────────────────────────────────────────

    [Theory]
    [InlineData(10, 500, 5, 600, 15, 533.333)]   // happy path
    [InlineData(0, 0, 10, 400, 10, 400)]          // zero stock → first-unit pricing
    [InlineData(5, 100, 5, 100, 10, 100)]          // same price → no change
    [InlineData(1, 0, 1, 1000, 2, 500)]            // old CPP zero
    public void ApplyPurchase_ReturnsExpectedCpp(
        int stockActual, decimal cppActual, int cantidad, decimal costoUnitario,
        int expectedStock, decimal expectedCpp)
    {
        var result = CalculadoraCostos.ApplyPurchase(
            stockActual, cppActual, cantidad, costoUnitario, out int nuevoStock);

        nuevoStock.Should().Be(expectedStock);
        result.Should().BeApproximately(expectedCpp, 0.001m);
    }

    [Fact]
    public void ApplyPurchase_ZeroStockZeroCpp_ReturnsFirstUnitPrice()
    {
        var result = CalculadoraCostos.ApplyPurchase(0, 0, 10, 400, out int nuevoStock);

        nuevoStock.Should().Be(10);
        result.Should().Be(400);
    }

    // ── RevertPurchase ───────────────────────────────────────────────

    [Theory]
    [InlineData(15, 533.333, 5, 600, 10, 500)]     // happy path: revert 5 of 15
    [InlineData(5, 533.333, 10, 600, 0, 0)]         // stock ≤ 0 → reset
    [InlineData(10, 500, 10, 500, 0, 0)]             // exact stock drain → reset
    [InlineData(10, 100, 3, 100, 7, 100)]            // same price → no change
    public void RevertPurchase_ReturnsExpectedCpp(
        int stockActual, decimal cppActual, int cantidad, decimal costoUnitario,
        int expectedStock, decimal expectedCpp)
    {
        var result = CalculadoraCostos.RevertPurchase(
            stockActual, cppActual, cantidad, costoUnitario, out int nuevoStock);

        nuevoStock.Should().Be(expectedStock);
        result.Should().BeApproximately(expectedCpp, 0.001m);
    }

    [Fact]
    public void RevertPurchase_NegativePoolValue_CapsCppAtZero()
    {
        // Edge: stock=1, cpp=100, revert 1×500 → pool goes negative
        var result = CalculadoraCostos.RevertPurchase(1, 100, 1, 500, out int nuevoStock);

        nuevoStock.Should().Be(0);
        result.Should().Be(0);  // Math.Max(0, ...) kicks in
    }

    [Fact]
    public void RevertPurchase_MoreThanStock_ResetsToZero()
    {
        var result = CalculadoraCostos.RevertPurchase(3, 200, 5, 200, out int nuevoStock);

        nuevoStock.Should().Be(0);
        result.Should().Be(0);
    }
}
