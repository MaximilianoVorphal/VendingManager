namespace VendingManager.Tests.Entities;

using FluentAssertions;

public class MaquinaEbitdaFieldsTests
{
    /// <summary>
    /// Maquina now has FechaInstalacion — required DateTime for operational-day counting.
    /// </summary>
    [Fact]
    public void Maquina_HasFechaInstalacion()
    {
        var maquina = new Maquina
        {
            Nombre = "M-001",
            FechaInstalacion = new DateTime(2025, 1, 15)
        };

        maquina.FechaInstalacion.Should().Be(new DateTime(2025, 1, 15));
    }

    /// <summary>
    /// FechaBaja is nullable — active machines have null.
    /// </summary>
    [Fact]
    public void Maquina_FechaBaja_IsNullable()
    {
        var maquina = new Maquina
        {
            Nombre = "M-001",
            FechaInstalacion = new DateTime(2025, 1, 1)
        };

        maquina.FechaBaja.Should().BeNull();
    }

    /// <summary>
    /// FechaBaja can be set when a machine is retired.
    /// </summary>
    [Fact]
    public void Maquina_FechaBaja_CanBeSet()
    {
        var maquina = new Maquina
        {
            Nombre = "M-001",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = new DateTime(2025, 6, 30)
        };

        maquina.FechaBaja.Should().Be(new DateTime(2025, 6, 30));
    }

    /// <summary>
    /// Existing properties (Nombre, IdInternoMaquina, Ubicacion, etc.) still work.
    /// </summary>
    [Fact]
    public void Maquina_ExistingPropertiesUnchanged()
    {
        var maquina = new Maquina
        {
            Nombre = "M-007",
            IdInternoMaquina = "2410280023",
            Ubicacion = "Santiago Centro",
            CodigoTerminalPos = "SIV01099",
            FechaInstalacion = new DateTime(2025, 1, 1)
        };

        maquina.Nombre.Should().Be("M-007");
        maquina.IdInternoMaquina.Should().Be("2410280023");
        maquina.Ubicacion.Should().Be("Santiago Centro");
        maquina.CodigoTerminalPos.Should().Be("SIV01099");
        maquina.Slots.Should().NotBeNull();
    }
}
