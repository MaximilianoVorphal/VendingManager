namespace VendingManager.Tests.Entities;

using FluentAssertions;

public class MovimientoCajaHistoryMaquinaIdTests
{
    /// <summary>
    /// MovimientoCajaHistory has MaquinaId column to match MovimientoCaja.
    /// </summary>
    [Fact]
    public void MovimientoCajaHistory_HasNullableMaquinaId()
    {
        var history = new MovimientoCajaHistory
        {
            EntityId = 1,
            Action = "Added",
            Descripcion = "Test",
            Fecha = DateTime.Now
        };

        history.MaquinaId.Should().BeNull();
    }

    /// <summary>
    /// MaquinaId can be set in history records.
    /// </summary>
    [Fact]
    public void MovimientoCajaHistory_MaquinaId_CanBeSet()
    {
        var history = new MovimientoCajaHistory
        {
            EntityId = 1,
            Action = "Added",
            Descripcion = "Internet M-007",
            Fecha = DateTime.Now,
            MaquinaId = 7
        };

        history.MaquinaId.Should().Be(7);
    }
}
