using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Shared.Mappers;
using VendingManager.Shared.Models;
using FluentAssertions;

namespace VendingManager.Tests.Shared;

public class OcrRecargaMapperTests
{
    private static readonly List<SlotActual> ActualSlots = new()
    {
        new() { Index = 0, Slot = "A1", Producto = "Coca Cola", Capacidad = 5 },
        new() { Index = 1, Slot = "A2", Producto = "Pepsi", Capacidad = 5 },
        new() { Index = 2, Slot = "A3", Producto = "Sprite", Capacidad = 5 }
    };

    [Fact]
    public void FromOcr_Confidence090_ReturnsAlta()
    {
        // R2.3a: Alta for confidence 0.9
        var dto = new OcrRecargaResultDto
        {
            ExtractedSlots = new List<MatchedSlotDto>
            {
                new() { SlotNumber = "01", MatchedSlot = "A1", Quantity = 5, Confidence = 0.9f }
            }
        };

        var result = OcrRecargaMapper.FromOcr(dto, ActualSlots);

        result.Slots.Should().HaveCount(1);
        result.Slots[0].Slot.Should().Be("A1");
        result.Slots[0].SlotIndex.Should().Be(0);
        result.Slots[0].CantidadDetectada.Should().Be(5);
        result.Slots[0].Capacidad.Should().Be(5);
        result.Slots[0].Producto.Should().Be("Coca Cola");
        result.Slots[0].Confianza.Should().Be(Confianza.Alta);
    }

    [Fact]
    public void FromOcr_Confidence070_ReturnsMedia()
    {
        // R2.3b: Media for confidence 0.7
        var dto = new OcrRecargaResultDto
        {
            ExtractedSlots = new List<MatchedSlotDto>
            {
                new() { SlotNumber = "02", MatchedSlot = "A2", Quantity = 3, Confidence = 0.7f }
            }
        };

        var result = OcrRecargaMapper.FromOcr(dto, ActualSlots);

        result.Slots.Should().HaveCount(1);
        result.Slots[0].Confianza.Should().Be(Confianza.Media);
    }

    [Fact]
    public void FromOcr_Confidence050_ReturnsBaja()
    {
        // R2.3c: Baja for confidence 0.5
        var dto = new OcrRecargaResultDto
        {
            ExtractedSlots = new List<MatchedSlotDto>
            {
                new() { SlotNumber = "03", MatchedSlot = "A3", Quantity = 2, Confidence = 0.5f }
            }
        };

        var result = OcrRecargaMapper.FromOcr(dto, ActualSlots);

        result.Slots.Should().HaveCount(1);
        result.Slots[0].Confianza.Should().Be(Confianza.Baja);
    }

    [Fact]
    public void FromOcr_QtyGreaterThanCapacity_ForcesBaja()
    {
        // R2.3d: Qty > Capacidad forces Baja even with confidence 0.9
        var dto = new OcrRecargaResultDto
        {
            ExtractedSlots = new List<MatchedSlotDto>
            {
                new() { SlotNumber = "01", MatchedSlot = "A1", Quantity = 10, Confidence = 0.9f }
            }
        };

        var result = OcrRecargaMapper.FromOcr(dto, ActualSlots);

        result.Slots.Should().HaveCount(1);
        result.Slots[0].CantidadDetectada.Should().Be(10);
        result.Slots[0].Capacidad.Should().Be(5);
        result.Slots[0].Confianza.Should().Be(Confianza.Baja);
    }

    [Fact]
    public void FromOcr_UnknownSlot_ForcesBaja()
    {
        // R2.3e: Unknown slot forces Baja
        var dto = new OcrRecargaResultDto
        {
            ExtractedSlots = new List<MatchedSlotDto>
            {
                new() { SlotNumber = "99", MatchedSlot = "X99", Quantity = 3, Confidence = 0.95f }
            }
        };

        var result = OcrRecargaMapper.FromOcr(dto, ActualSlots);

        result.Slots.Should().HaveCount(1);
        result.Slots[0].Slot.Should().Be("X99");
        result.Slots[0].SlotIndex.Should().Be(-1);
        result.Slots[0].Producto.Should().BeNull();
        result.Slots[0].Capacidad.Should().Be(0);
        result.Slots[0].Confianza.Should().Be(Confianza.Baja);
    }

    [Fact]
    public void FromOcr_EmptyDto_ReturnsEmptyModel()
    {
        // R2.3f: Empty OCR result → empty LecturaRecarga (zero slots)
        var dto = new OcrRecargaResultDto();

        var result = OcrRecargaMapper.FromOcr(dto, ActualSlots);

        result.Slots.Should().BeEmpty();
        result.TotalUnidades.Should().Be(0);
        result.ARevisar.Should().Be(0);
    }
}
