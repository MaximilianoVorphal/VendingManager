using FluentAssertions;
using VendingManager.Web.Components.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class MachineHelpersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractShortCode_NullOrEmpty_ReturnsDash(string? input)
    {
        var result = MachineHelpers.ExtractShortCode(input);

        result.Should().Be("---");
    }

    [Fact]
    public void ExtractShortCode_ValidNameWithMaquinaPrefix_ReturnsLast4Digits()
    {
        var result = MachineHelpers.ExtractShortCode("MAQUINA 2410280022 XYZ");

        result.Should().Be("0022");
    }

    [Fact]
    public void ExtractShortCode_NameWithoutPrefix_TruncatesFirst6Chars()
    {
        var result = MachineHelpers.ExtractShortCode("CafePlus");

        result.Should().Be("CafePl");
    }
}
