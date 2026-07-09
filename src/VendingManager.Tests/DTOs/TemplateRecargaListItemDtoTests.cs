namespace VendingManager.Tests.DTOs;

using FluentAssertions;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

/// <summary>
/// Tests for TemplateRecargaListItemDto — lightweight list projection.
/// Verifies the DTO structure matches spec requirements.
/// </summary>
public class TemplateRecargaListItemDtoTests
{
    [Fact]
    public void TemplateRecargaListItemDto_HasRequiredProperties()
    {
        var dto = new TemplateRecargaListItemDto
        {
            Id = 1,
            Nombre = "Recarga Semana 1",
            Descripcion = "Test description",
            MaquinaNombre = "Máquina 23",
            EsActivo = true,
            FechaCreacion = new DateTime(2025, 1, 15),
            Estado = EstadoTemplate.Terminado,
            PeriodoCount = 3,
            TotalProducts = 15
        };

        dto.Id.Should().Be(1);
        dto.Nombre.Should().Be("Recarga Semana 1");
        dto.Descripcion.Should().Be("Test description");
        dto.MaquinaNombre.Should().Be("Máquina 23");
        dto.EsActivo.Should().BeTrue();
        dto.FechaCreacion.Should().Be(new DateTime(2025, 1, 15));
        dto.Estado.Should().Be(EstadoTemplate.Terminado);
        dto.PeriodoCount.Should().Be(3);
        dto.TotalProducts.Should().Be(15);
    }

    [Fact]
    public void TemplateRecargaListItemDto_NullableFieldsDefaultToNull()
    {
        var dto = new TemplateRecargaListItemDto();

        dto.Nombre.Should().Be(string.Empty);
        dto.Descripcion.Should().BeNull();
        dto.MaquinaNombre.Should().Be(string.Empty);
        dto.EsActivo.Should().BeFalse();
        dto.Estado.Should().Be(EstadoTemplate.Pendiente);
        dto.PeriodoCount.Should().Be(0);
        dto.TotalProducts.Should().Be(0);
    }

    [Fact]
    public void TemplateRecargaListItemDto_DoesNotContainNestedPeriodos()
    {
        // This test documents that the list DTO is FLAT — no Periodos collection
        var dto = new TemplateRecargaListItemDto();

        // The type should NOT have a Periodos property
        var properties = typeof(TemplateRecargaListItemDto).GetProperties();
        var periodoProperty = properties.FirstOrDefault(p => p.Name == "Periodos");
        periodoProperty.Should().BeNull("TemplateRecargaListItemDto should not have a Periodos property — it is a flat DTO");
    }

    [Fact]
    public void TemplateRecargaListItemDto_DoesNotContainSnapshotSlots()
    {
        var dto = new TemplateRecargaListItemDto();

        var properties = typeof(TemplateRecargaListItemDto).GetProperties();
        var slotsProperty = properties.FirstOrDefault(p => p.Name == "SnapshotSlots");
        slotsProperty.Should().BeNull("TemplateRecargaListItemDto should not have SnapshotSlots — list endpoint is lightweight");
    }
}
