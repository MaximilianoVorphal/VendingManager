namespace VendingManager.Tests.Enums;

using FluentAssertions;
using VendingManager.Shared.Enums;

public class EstadoTemplateTests
{
    /// <summary>
    /// Verifies enum has exactly 2 states: Pendiente and Terminado.
    /// Activo state eliminated (was intermediate step, now computed as latest Terminado).
    /// </summary>
    [Fact]
    public void EstadoTemplate_HasTwoStates()
    {
        Enum.GetValues<EstadoTemplate>().Should().HaveCount(2);
    }

    [Fact]
    public void EstadoTemplate_Pendiente_HasValueZero()
    {
        ((int)EstadoTemplate.Pendiente).Should().Be(0);
    }

    [Fact]
    public void EstadoTemplate_Terminado_HasValueTwo()
    {
        ((int)EstadoTemplate.Terminado).Should().Be(2);
    }

    /// <summary>
    /// Verifies all states are parseable by name (needed for JSON/API serialization).
    /// </summary>
    [Theory]
    [InlineData("Pendiente", EstadoTemplate.Pendiente)]
    [InlineData("Terminado", EstadoTemplate.Terminado)]
    public void EstadoTemplate_ParseFromName_ReturnsCorrectValue(string name, EstadoTemplate expected)
    {
        Enum.TryParse<EstadoTemplate>(name, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }
}