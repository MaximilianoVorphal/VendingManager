using FluentAssertions;
using VendingManager.Shared.Enums;

namespace VendingManager.Tests.Enums;

public class ConfianzaEnumTests
{
    [Fact]
    public void Confianza_HasAltaValue()
    {
        Enum.GetNames<Confianza>().Should().Contain("Alta");
    }

    [Fact]
    public void Confianza_HasMediaValue()
    {
        Enum.GetNames<Confianza>().Should().Contain("Media");
    }

    [Fact]
    public void Confianza_HasBajaValue()
    {
        Enum.GetNames<Confianza>().Should().Contain("Baja");
    }

    [Fact]
    public void Confianza_HasExactlyThreeValues()
    {
        Enum.GetValues<Confianza>().Should().HaveCount(3);
    }
}
