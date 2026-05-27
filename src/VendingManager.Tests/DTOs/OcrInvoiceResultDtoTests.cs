using System.Text.Json;

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

    [Fact]
    public void OcrInvoiceResultDto_TaxFields_DefaultToNull()
    {
        var dto = new OcrInvoiceResultDto();

        dto.TipoDocumento.Should().BeNull();
        dto.TotalNeto.Should().BeNull();
        dto.TotalIva.Should().BeNull();
        dto.TotalIla.Should().BeNull();
    }

    [Fact]
    public void OcrInvoiceItemDto_TaxFields_DefaultToNull()
    {
        var item = new OcrInvoiceItemDto();

        item.TieneIva.Should().BeFalse();
        item.TieneIla.Should().BeFalse();
        item.TipoIla.Should().BeNull();
        item.NetoUnitario.Should().BeNull();
    }

    [Fact]
    public void OcrInvoiceResultDto_DeserializeWithoutTaxFields_BackwardCompatible()
    {
        var json = """
        {
            "proveedor": "ALVI",
            "numero_documento": "12345",
            "fecha": "2026-05-27",
            "monto_total": 15000.0,
            "items": [
                {
                    "producto": "COCA COLA 350 ML",
                    "cantidad": 2,
                    "costo_unitario": 1500.0,
                    "subtotal": 3000.0
                }
            ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<OcrInvoiceResultDto>(json, options);

        result.Should().NotBeNull();
        result!.Proveedor.Should().Be("ALVI");
        result.NumeroDocumento.Should().Be("12345");
        result.MontoTotal.Should().Be(15000.0m);
        result.Items.Should().HaveCount(1);
        result.Items[0].Producto.Should().Be("COCA COLA 350 ML");
        result.Items[0].Cantidad.Should().Be(2);
        result.Items[0].CostoUnitario.Should().Be(1500.0m);
        result.Items[0].Subtotal.Should().Be(3000.0m);

        // New tax fields should deserialize to defaults when missing
        result.TipoDocumento.Should().BeNull();
        result.TotalNeto.Should().BeNull();
        result.TotalIva.Should().BeNull();
        result.TotalIla.Should().BeNull();
        result.Items[0].TieneIva.Should().BeFalse();
        result.Items[0].TieneIla.Should().BeFalse();
        result.Items[0].TipoIla.Should().BeNull();
        result.Items[0].NetoUnitario.Should().BeNull();
    }

    [Fact]
    public void OcrInvoiceResultDto_DeserializeWithTaxFields_PopulatesCorrectly()
    {
        var json = """
        {
            "proveedor": "ALVI",
            "numero_documento": "12345",
            "fecha": "2026-05-27",
            "monto_total": 15000.0,
            "tipo_documento": "FACTURA",
            "total_neto": 12184.87,
            "total_iva": 2315.13,
            "total_ila": 500.0,
            "items": [
                {
                    "producto": "COCA COLA 350 ML",
                    "cantidad": 2,
                    "costo_unitario": 1123.36,
                    "subtotal": 2246.72,
                    "tiene_iva": true,
                    "tiene_ila": true,
                    "tipo_ila": "18",
                    "neto_unitario": 800.0
                }
            ]
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<OcrInvoiceResultDto>(json, options);

        result.Should().NotBeNull();
        result!.TipoDocumento.Should().Be("FACTURA");
        result.TotalNeto.Should().Be(12184.87m);
        result.TotalIva.Should().Be(2315.13m);
        result.TotalIla.Should().Be(500.0m);

        result.Items[0].TieneIva.Should().BeTrue();
        result.Items[0].TieneIla.Should().BeTrue();
        result.Items[0].TipoIla.Should().Be("18");
        result.Items[0].NetoUnitario.Should().Be(800.0m);
    }
}
