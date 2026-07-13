namespace VendingManager.Tests.Entities;

using FluentAssertions;

public class MovimientoCajaMaquinaIdTests
{
    /// <summary>
    /// MovimientoCaja now has nullable MaquinaId FK — no navigation property.
    /// </summary>
    [Fact]
    public void MovimientoCaja_HasNullableMaquinaId()
    {
        var mov = new MovimientoCaja
        {
            Descripcion = "Internet M-007",
            Monto = -30_000m,
            Fecha = DateTime.Now
        };

        mov.MaquinaId.Should().BeNull();
    }

    /// <summary>
    /// MaquinaId can be set for per-machine OPEX attribution.
    /// </summary>
    [Fact]
    public void MovimientoCaja_MaquinaId_CanBeSet()
    {
        var mov = new MovimientoCaja
        {
            Descripcion = "Internet M-007",
            Monto = -30_000m,
            Fecha = DateTime.Now,
            MaquinaId = 7
        };

        mov.MaquinaId.Should().Be(7);
    }

    /// <summary>
    /// Fleet-level MovimientoCaja leaves MaquinaId null.
    /// </summary>
    [Fact]
    public void MovimientoCaja_FleetLevel_LeavesMaquinaIdNull()
    {
        var mov = new MovimientoCaja
        {
            Descripcion = "Admin general",
            Monto = -100_000m,
            Fecha = DateTime.Now
            // MaquinaId not set — remains null
        };

        mov.MaquinaId.Should().BeNull();
    }
}
