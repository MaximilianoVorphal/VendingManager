using FluentAssertions;
using System.Linq;
using System.Threading.Tasks;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class MockPlantillaServiceTests
{
    [Fact]
    public async Task GetPlantillasAsync_ReturnsFiveHardcodedTemplates()
    {
        var service = new MockPlantillaService();

        var result = await service.GetPlantillasAsync();

        result.Count.Should().Be(5);
        result.All(p => !string.IsNullOrWhiteSpace(p.Nombre)).Should().BeTrue();
        result.All(p => p.Nombre.StartsWith("PLANTILLA ")).Should().BeTrue();
    }

    [Fact]
    public async Task GetPlantillasAsync_ContainsExpectedNamedTemplates()
    {
        var service = new MockPlantillaService();

        var result = await service.GetPlantillasAsync();

        var nombres = result.Select(p => p.Nombre).ToList();
        nombres.Should().Contain("PLANTILLA ESTÁNDAR");
        nombres.Should().Contain("PLANTILLA PREMIUM");
        nombres.Should().Contain("PLANTILLA COMPACTA");
        nombres.Should().Contain("PLANTILLA MAXI");
        nombres.Should().Contain("PLANTILLA OFICINA");
    }
}
