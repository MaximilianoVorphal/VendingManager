namespace VendingManager.Tests.Entities;

using FluentAssertions;

public class DepreciacionMaquinaHistoryEntityTests
{
    /// <summary>
    /// DepreciacionMaquinaHistory has auditing metadata columns.
    /// </summary>
    [Fact]
    public void DepreciacionMaquinaHistory_HasAuditColumns()
    {
        var history = new DepreciacionMaquinaHistory
        {
            EntityId = 1,
            Action = "Added",
            Timestamp = DateTime.UtcNow,
            Usuario = "test-user"
        };

        history.EntityId.Should().Be(1);
        history.Action.Should().Be("Added");
        history.Usuario.Should().Be("test-user");
    }

    /// <summary>
    /// DepreciacionMaquinaHistory carries the same domain columns as DepreciacionMaquina.
    /// </summary>
    [Fact]
    public void DepreciacionMaquinaHistory_HasDomainColumns()
    {
        var history = new DepreciacionMaquinaHistory
        {
            Descripcion = "Internet M-007",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true,
            FechaCreacion = new DateTime(2025, 1, 1)
        };

        history.Descripcion.Should().Be("Internet M-007");
        history.ValorAdquisicion.Should().Be(2_000_000m);
        history.ValorResidual.Should().Be(200_000m);
        history.VidaUtilMeses.Should().Be(36);
        history.MetodoDepreciacion.Should().Be("LINEAL");
        history.Activo.Should().BeTrue();
    }

    /// <summary>
    /// MaquinaId is carried in the history to preserve FK context.
    /// </summary>
    [Fact]
    public void DepreciacionMaquinaHistory_HasMaquinaId()
    {
        var history = new DepreciacionMaquinaHistory
        {
            MaquinaId = 7,
            EntityId = 1,
            Action = "Added"
        };

        history.MaquinaId.Should().Be(7);
    }
}
