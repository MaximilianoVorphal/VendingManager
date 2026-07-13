namespace VendingManager.Tests.Entities;

using FluentAssertions;

public class DepreciacionMaquinaEntityTests
{
    /// <summary>
    /// New DepreciacionMaquina defaults to MetodoDepreciacion "LINEAL".
    /// </summary>
    [Fact]
    public void NewDepreciacionMaquina_DefaultsToLineal()
    {
        var entity = new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "Test Machine",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1)
        };

        entity.MetodoDepreciacion.Should().Be("LINEAL");
    }

    /// <summary>
    /// Activo defaults to true on new instances.
    /// </summary>
    [Fact]
    public void NewDepreciacionMaquina_ActivoDefaultsToTrue()
    {
        var entity = new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "Test Machine",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1)
        };

        entity.Activo.Should().BeTrue();
    }

    /// <summary>
    /// FechaCreacion is set to a recent DateTime (defaults to DateTime.Now).
    /// </summary>
    [Fact]
    public void NewDepreciacionMaquina_FechaCreacionIsSet()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var entity = new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "Test Machine",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1)
        };
        var after = DateTime.Now.AddSeconds(1);

        entity.FechaCreacion.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    /// <summary>
    /// Decimal properties accept and preserve CLP-scale values.
    /// </summary>
    [Fact]
    public void DepreciacionMaquina_DecimalPrecision_IsPreserved()
    {
        var entity = new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "Test",
            ValorAdquisicion = 1_999_990.50m,
            ValorResidual = 199_999.99m,
            VidaUtilMeses = 60,
            FechaAdquisicion = new DateTime(2024, 6, 15)
        };

        entity.ValorAdquisicion.Should().Be(1_999_990.50m);
        entity.ValorResidual.Should().Be(199_999.99m);
    }

    /// <summary>
    /// MetodoDepreciacion can be explicitly overridden.
    /// </summary>
    [Fact]
    public void DepreciacionMaquina_CanOverrideMetodo()
    {
        var entity = new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "Test",
            ValorAdquisicion = 1_000_000m,
            ValorResidual = 100_000m,
            VidaUtilMeses = 24,
            FechaAdquisicion = new DateTime(2025, 3, 1),
            MetodoDepreciacion = "ACELERADO"
        };

        entity.MetodoDepreciacion.Should().Be("ACELERADO");
    }
}
