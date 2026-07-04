using VendingManager.Shared.Enums;
using VendingManager.Shared.Models;
using FluentAssertions;

namespace VendingManager.Tests.Shared;

public class FotoModelsTests
{
    [Fact]
    public void SlotDetectado_Properties_SetAndGet()
    {
        var slot = new SlotDetectado
        {
            Slot = "A1",
            SlotIndex = 0,
            Producto = "Coca Cola",
            CantidadDetectada = 5,
            Capacidad = 10,
            Confianza = Confianza.Alta
        };

        slot.Slot.Should().Be("A1");
        slot.SlotIndex.Should().Be(0);
        slot.Producto.Should().Be("Coca Cola");
        slot.CantidadDetectada.Should().Be(5);
        slot.Capacidad.Should().Be(10);
        slot.Confianza.Should().Be(Confianza.Alta);
    }

    [Fact]
    public void SlotDetectado_CanSetProductoNull()
    {
        var slot = new SlotDetectado
        {
            Slot = "B2",
            SlotIndex = 1,
            Producto = null,
            CantidadDetectada = 3,
            Capacidad = 5,
            Confianza = Confianza.Media
        };

        slot.Producto.Should().BeNull();
    }

    [Fact]
    public void LecturaRecarga_TotalUnidades_SumOfCantidadDetectada()
    {
        var lectura = new LecturaRecarga
        {
            MaquinaId = "M1",
            Slots = new List<SlotDetectado>
            {
                new() { Slot = "A1", SlotIndex = 0, CantidadDetectada = 5, Capacidad = 10, Confianza = Confianza.Alta },
                new() { Slot = "A2", SlotIndex = 1, CantidadDetectada = 3, Capacidad = 5, Confianza = Confianza.Media },
                new() { Slot = "B1", SlotIndex = 2, CantidadDetectada = 2, Capacidad = 5, Confianza = Confianza.Baja }
            }
        };

        lectura.TotalUnidades.Should().Be(10);
    }

    [Fact]
    public void LecturaRecarga_TotalUnidades_EmptySlots_ReturnsZero()
    {
        var lectura = new LecturaRecarga
        {
            MaquinaId = "M1",
            Slots = new List<SlotDetectado>()
        };

        lectura.TotalUnidades.Should().Be(0);
    }

    [Fact]
    public void LecturaRecarga_ARevisar_CountWhereConfianzaNotAlta()
    {
        var lectura = new LecturaRecarga
        {
            MaquinaId = "M1",
            Slots = new List<SlotDetectado>
            {
                new() { Slot = "A1", SlotIndex = 0, CantidadDetectada = 5, Capacidad = 10, Confianza = Confianza.Alta },
                new() { Slot = "A2", SlotIndex = 1, CantidadDetectada = 3, Capacidad = 5, Confianza = Confianza.Media },
                new() { Slot = "B1", SlotIndex = 2, CantidadDetectada = 2, Capacidad = 5, Confianza = Confianza.Baja }
            }
        };

        lectura.ARevisar.Should().Be(2);
    }

    [Fact]
    public void LecturaRecarga_ARevisar_AllAlta_ReturnsZero()
    {
        var lectura = new LecturaRecarga
        {
            MaquinaId = "M1",
            Slots = new List<SlotDetectado>
            {
                new() { Slot = "A1", SlotIndex = 0, CantidadDetectada = 5, Capacidad = 10, Confianza = Confianza.Alta },
                new() { Slot = "A2", SlotIndex = 1, CantidadDetectada = 3, Capacidad = 5, Confianza = Confianza.Alta }
            }
        };

        lectura.ARevisar.Should().Be(0);
    }

    [Fact]
    public void SlotActual_Properties_SetAndGet()
    {
        var slot = new SlotActual
        {
            Index = 0,
            Slot = "A1",
            Producto = "Coca Cola",
            Capacidad = 10
        };

        slot.Index.Should().Be(0);
        slot.Slot.Should().Be("A1");
        slot.Producto.Should().Be("Coca Cola");
        slot.Capacidad.Should().Be(10);
    }

    [Fact]
    public void SlotActual_CanSetProductoNull()
    {
        var slot = new SlotActual
        {
            Index = 1,
            Slot = "B2",
            Producto = null,
            Capacidad = 5
        };

        slot.Producto.Should().BeNull();
    }
}
