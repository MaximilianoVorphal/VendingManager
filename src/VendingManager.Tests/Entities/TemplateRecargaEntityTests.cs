namespace VendingManager.Tests.Entities;

using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Shared.Enums;

public class TemplateRecargaEntityTests
{
    /// <summary>
    /// New template instances default to Pendiente (the starting state).
    /// </summary>
    [Fact]
    public void NewTemplateRecarga_DefaultsToPendiente()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        template.Estado.Should().Be(EstadoTemplate.Pendiente);
    }

    /// <summary>
    /// RowVersion starts as null array before persistence.
    /// EF Core generates the actual value on insert.
    /// </summary>
    [Fact]
    public void NewTemplateRecarga_RowVersionIsNull()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        template.RowVersion.Should().BeNull();
    }

    /// <summary>
    /// Estado can be set explicitly (needed for migration-verified Terminado default).
    /// </summary>
    [Fact]
    public void TemplateRecarga_CanSetEstadoToTerminado()
    {
        var template = new TemplateRecarga
        {
            Nombre = "Test",
            FechaCreacion = DateTime.Now,
            Estado = EstadoTemplate.Terminado
        };
        template.Estado.Should().Be(EstadoTemplate.Terminado);
    }

    /// <summary>
    /// RowVersion can hold a byte array from EF Core concurrency token.
    /// </summary>
    [Fact]
    public void TemplateRecarga_RowVersionCanBeSet()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        template.RowVersion = bytes;
        template.RowVersion.Should().BeEquivalentTo(bytes);
    }

    /// <summary>
    /// FechaCargaInicio and FechaCargaFin removed — these were for EnCarga→Activo transition.
    /// Verify they are no longer properties on the entity.
    /// </summary>
    [Fact]
    public void TemplateRecarga_FechaCargaInicio_Removed()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        // The property should not exist — compile-time check via reflection
        typeof(TemplateRecarga).GetProperty("FechaCargaInicio").Should().BeNull("FechaCargaInicio was only used for EnCarga→Activo transition");
    }

    [Fact]
    public void TemplateRecarga_FechaCargaFin_Removed()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        typeof(TemplateRecarga).GetProperty("FechaCargaFin").Should().BeNull("FechaCargaFin was only used for EnCarga→Activo transition");
    }
}