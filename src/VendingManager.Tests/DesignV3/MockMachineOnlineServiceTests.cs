using FluentAssertions;
using System.Linq;
using System.Threading.Tasks;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class MockMachineOnlineServiceTests
{
    [Fact]
    public async Task GetOnlineMachinesAsync_ReturnsFiveOrSixMachines_WithMixedStates()
    {
        var service = new MockMachineOnlineService();

        var result = await service.GetOnlineMachinesAsync();

        result.Count.Should().BeInRange(5, 6);
        result.Any(m => m.IsOnline).Should().BeTrue("al menos una máquina debe estar online");
        result.Any(m => !m.IsOnline).Should().BeTrue("al menos una máquina debe estar offline");
        result.All(m => !string.IsNullOrWhiteSpace(m.Name)).Should().BeTrue();
        result.All(m => m.LastSeen != default).Should().BeTrue();
    }
}
