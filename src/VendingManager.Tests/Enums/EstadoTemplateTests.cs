namespace VendingManager.Tests.Enums;

using FluentAssertions;
using VendingManager.Shared.Enums;

public class EstadoTemplateTests
{
    /// <summary>
    /// Verifies enum has the expected values in the correct order.
    /// This is structural but needed to document the contract.
    /// </summary>
    [Fact]
    public void EstadoTemplate_HasFourStates()
    {
        Enum.GetValues<EstadoTemplate>().Should().HaveCount(4);
    }

    [Fact]
    public void EstadoTemplate_Borrador_HasValueZero()
    {
        ((int)EstadoTemplate.Borrador).Should().Be(0);
    }

    [Fact]
    public void EstadoTemplate_EnCarga_HasValueOne()
    {
        ((int)EstadoTemplate.EnCarga).Should().Be(1);
    }

    [Fact]
    public void EstadoTemplate_Activo_HasValueTwo()
    {
        ((int)EstadoTemplate.Activo).Should().Be(2);
    }

    [Fact]
    public void EstadoTemplate_Cerrado_HasValueThree()
    {
        ((int)EstadoTemplate.Cerrado).Should().Be(3);
    }

    /// <summary>
    /// Verifies all states are parseable by name (needed for JSON/API serialization).
    /// </summary>
    [Theory]
    [InlineData("Borrador", EstadoTemplate.Borrador)]
    [InlineData("EnCarga", EstadoTemplate.EnCarga)]
    [InlineData("Activo", EstadoTemplate.Activo)]
    [InlineData("Cerrado", EstadoTemplate.Cerrado)]
    public void EstadoTemplate_ParseFromName_ReturnsCorrectValue(string name, EstadoTemplate expected)
    {
        Enum.TryParse<EstadoTemplate>(name, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }
}