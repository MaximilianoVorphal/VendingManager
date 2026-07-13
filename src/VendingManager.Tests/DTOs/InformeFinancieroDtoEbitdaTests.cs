namespace VendingManager.Tests.DTOs;

using FluentAssertions;
using VendingManager.Shared.DTOs;

public class InformeFinancieroDtoEbitdaTests
{
    /// <summary>
    /// New EBITDA fields default to 0 (backward-compatible additive fields).
    /// </summary>
    [Fact]
    public void NewFieldsDefaultToZero()
    {
        var dto = new InformeFinancieroDto();

        dto.GastosFijos.Should().Be(0);
        dto.GastosVariables.Should().Be(0);
        dto.DepreciacionPeriodo.Should().Be(0);
        dto.Ebitda.Should().Be(0);
    }

    /// <summary>
    /// All new fields are decimal and can hold meaningful values.
    /// </summary>
    [Fact]
    public void NewFieldsCanHoldDecimalValues()
    {
        var dto = new InformeFinancieroDto
        {
            GastosFijos = 150_000m,
            GastosVariables = 85_500.50m,
            DepreciacionPeriodo = 46_296.30m,
            Ebitda = 718_203.20m
        };

        dto.GastosFijos.Should().Be(150_000m);
        dto.GastosVariables.Should().Be(85_500.50m);
        dto.DepreciacionPeriodo.Should().Be(46_296.30m);
        dto.Ebitda.Should().Be(718_203.20m);
    }

    /// <summary>
    /// Existing fields (VentasTotales, CostoVentas, MargenBruto, etc.) remain unchanged.
    /// </summary>
    [Fact]
    public void ExistingFieldsStillWork()
    {
        var dto = new InformeFinancieroDto
        {
            VentasTotales = 1_000_000m,
            CostoVentas = 500_000m,
            MargenBruto = 500_000m,
            GastosOperativos = 200_000m,
            UtilidadNeta = 300_000m,
            MargenPorcentaje = 30m
        };

        dto.VentasTotales.Should().Be(1_000_000m);
        dto.CostoVentas.Should().Be(500_000m);
        dto.MargenBruto.Should().Be(500_000m);
        dto.GastosOperativos.Should().Be(200_000m);
        dto.UtilidadNeta.Should().Be(300_000m);
        dto.MargenPorcentaje.Should().Be(30m);
    }

    /// <summary>
    /// Ebitda field is independent — can be set without touching existing fields.
    /// </summary>
    [Fact]
    public void EbitdaFieldIsIndependent()
    {
        var dto = new InformeFinancieroDto
        {
            VentasTotales = 1_000_000m,
            Ebitda = 450_000m
        };

        dto.VentasTotales.Should().Be(1_000_000m);
        dto.Ebitda.Should().Be(450_000m);
        dto.CostoVentas.Should().Be(0); // unset defaults
    }
}
