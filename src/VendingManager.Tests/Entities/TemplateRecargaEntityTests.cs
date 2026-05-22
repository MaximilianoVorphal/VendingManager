namespace VendingManager.Tests.Entities;

using FluentAssertions;
using VendingManager.Core.Entities;
using VendingManager.Shared.Enums;

public class TemplateRecargaEntityTests
{
    /// <summary>
    /// New template instances default to Borrador (the starting state).
    /// Migration sets existing templates to Activo.
    /// </summary>
    [Fact]
    public void NewTemplateRecarga_DefaultsToBorrador()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        template.Estado.Should().Be(EstadoTemplate.Borrador);
    }

    /// <summary>
    /// New template has no carga dates until transition to EnCarga.
    /// </summary>
    [Fact]
    public void NewTemplateRecarga_FechaCargaInicioIsNull()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        template.FechaCargaInicio.Should().BeNull();
    }

    [Fact]
    public void NewTemplateRecarga_FechaCargaFinIsNull()
    {
        var template = new TemplateRecarga { Nombre = "Test", FechaCreacion = DateTime.Now };
        template.FechaCargaFin.Should().BeNull();
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
    /// Estado can be set explicitly (needed for migration-verified Activo default).
    /// </summary>
    [Fact]
    public void TemplateRecarga_CanSetEstadoToActivo()
    {
        var template = new TemplateRecarga
        {
            Nombre = "Test",
            FechaCreacion = DateTime.Now,
            Estado = EstadoTemplate.Activo
        };
        template.Estado.Should().Be(EstadoTemplate.Activo);
    }

    /// <summary>
    /// Carga dates can be set during EnCarga transition.
    /// </summary>
    [Fact]
    public void TemplateRecarga_CanSetCargaDates()
    {
        var inicio = DateTime.Now;
        var fin = DateTime.Now.AddHours(8);
        var template = new TemplateRecarga
        {
            Nombre = "Test",
            FechaCreacion = DateTime.Now,
            FechaCargaInicio = inicio,
            FechaCargaFin = fin
        };
        template.FechaCargaInicio.Should().Be(inicio);
        template.FechaCargaFin.Should().Be(fin);
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
}