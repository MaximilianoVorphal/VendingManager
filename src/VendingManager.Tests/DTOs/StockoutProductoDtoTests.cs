namespace VendingManager.Tests.DTOs;

using VendingManager.Shared.DTOs;

public class StockoutProductoDtoTests
{
    [Fact]
    public void StockoutProductoDto_UsesCalendarDaysAndRetainsWarningMetadata()
    {
        var postDepletionSale = new DateTime(2026, 7, 14, 9, 0, 0);
        var dto = new StockoutProductoDto
        {
            HorasSinStock = 180,
            FechaAgotamientoEstimada = postDepletionSale.AddDays(-4),
            TieneVentasPosterioresAlAgotamiento = true,
            UltimaVentaPosteriorAlAgotamiento = postDepletionSale,
            Maquinas = ["M1", "M2"]
        };

        dto.DiasSinStock.Should().Be(7.5);
        dto.TieneVentasPosterioresAlAgotamiento.Should().BeTrue();
        dto.UltimaVentaPosteriorAlAgotamiento.Should().Be(postDepletionSale);
        dto.MaquinasResumen.Should().Be("M1, M2");
    }
}
