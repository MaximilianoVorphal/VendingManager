namespace VendingManager.Tests.DTOs;

using VendingManager.Shared.DTOs;

public class OcrInvoiceResultDtoTests
{
    [Fact]
    public void OcrInvoiceItemDto_SugerirCreacion_DefaultsToFalse()
    {
        var item = new OcrInvoiceItemDto();
        item.SugerirCreacion.Should().BeFalse();
    }

    [Fact]
    public void OcrInvoiceItemDto_SugerirCreacion_CanBeSetToTrue()
    {
        var item = new OcrInvoiceItemDto { SugerirCreacion = true };
        item.SugerirCreacion.Should().BeTrue();
    }
}
