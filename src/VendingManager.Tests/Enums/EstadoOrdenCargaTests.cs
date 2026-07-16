namespace VendingManager.Tests.Enums;

using FluentAssertions;
using VendingManager.Shared.Enums;

/// <summary>
/// EstadoOrdenCarga enum: value existence, numeric assignment, and string round-trip
/// for backwards-compatible DB persistence (NVARCHAR with uppercase values).
/// </summary>
public class EstadoOrdenCargaTests
{
    [Fact]
    public void EstadoOrdenCarga_HasThreeValues()
    {
        Enum.GetValues<EstadoOrdenCarga>().Should().HaveCount(3);
    }

    [Fact]
    public void EstadoOrdenCarga_Borrador_HasValueZero()
    {
        ((int)EstadoOrdenCarga.Borrador).Should().Be(0);
    }

    [Fact]
    public void EstadoOrdenCarga_Pendiente_HasValueOne()
    {
        ((int)EstadoOrdenCarga.Pendiente).Should().Be(1);
    }

    [Fact]
    public void EstadoOrdenCarga_Finalizada_HasValueTwo()
    {
        ((int)EstadoOrdenCarga.Finalizada).Should().Be(2);
    }

    [Theory]
    [InlineData("BORRADOR", EstadoOrdenCarga.Borrador)]
    [InlineData("Borrador", EstadoOrdenCarga.Borrador)]
    [InlineData("borrador", EstadoOrdenCarga.Borrador)]
    [InlineData("PENDIENTE", EstadoOrdenCarga.Pendiente)]
    [InlineData("Pendiente", EstadoOrdenCarga.Pendiente)]
    [InlineData("pendiente", EstadoOrdenCarga.Pendiente)]
    [InlineData("FINALIZADA", EstadoOrdenCarga.Finalizada)]
    [InlineData("Finalizada", EstadoOrdenCarga.Finalizada)]
    [InlineData("finalizada", EstadoOrdenCarga.Finalizada)]
    public void EstadoOrdenCarga_ParseCaseInsensitive_ReturnsCorrectValue(string name, EstadoOrdenCarga expected)
    {
        Enum.TryParse<EstadoOrdenCarga>(name, ignoreCase: true, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void EstadoOrdenCarga_ToString_ReturnsPascalCaseName()
    {
        EstadoOrdenCarga.Borrador.ToString().Should().Be("Borrador");
        EstadoOrdenCarga.Pendiente.ToString().Should().Be("Pendiente");
        EstadoOrdenCarga.Finalizada.ToString().Should().Be("Finalizada");
    }

    [Fact]
    public void EstadoOrdenCarga_ToUpperInvariant_ReturnsUppercaseName()
    {
        EstadoOrdenCarga.Borrador.ToString().ToUpperInvariant().Should().Be("BORRADOR");
        EstadoOrdenCarga.Pendiente.ToString().ToUpperInvariant().Should().Be("PENDIENTE");
        EstadoOrdenCarga.Finalizada.ToString().ToUpperInvariant().Should().Be("FINALIZADA");
    }
}
