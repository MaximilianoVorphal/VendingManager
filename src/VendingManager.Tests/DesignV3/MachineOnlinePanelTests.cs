using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VendingManager.Web.Components;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class MachineOnlinePanelTests : TestContext
{
    [Fact]
    public void MachineOnlinePanel_RendersMachinesFromService()
    {
        var machines = new List<MachineOnlineStatus>
        {
            new(1, "Máquina A", true, DateTime.Now.AddMinutes(-2)),
            new(2, "Máquina B", false, DateTime.Now.AddMinutes(-15)),
            new(3, "Máquina C", true, DateTime.Now.AddHours(-1))
        };
        Services.AddSingleton<IMachineOnlineService>(new TestMachineOnlineService(machines));

        var cut = RenderComponent<MachineOnlinePanel>();

        cut.Markup.Should().Contain("Máquina A");
        cut.Markup.Should().Contain("Máquina B");
        cut.Markup.Should().Contain("Máquina C");
        cut.Markup.Should().Contain("var(--signal-success)");
        cut.Markup.Should().Contain("var(--signal-danger)");
    }

    [Fact]
    public void MachineOnlinePanel_EmptyState_ShowsMessage()
    {
        Services.AddSingleton<IMachineOnlineService>(new TestMachineOnlineService(new List<MachineOnlineStatus>()));

        var cut = RenderComponent<MachineOnlinePanel>();

        cut.Markup.Should().Contain("Sin máquinas configuradas.");
    }

    private class TestMachineOnlineService : IMachineOnlineService
    {
        private readonly IReadOnlyList<MachineOnlineStatus> _machines;

        public TestMachineOnlineService(IReadOnlyList<MachineOnlineStatus> machines)
        {
            _machines = machines;
        }

        public Task<IReadOnlyList<MachineOnlineStatus>> GetOnlineMachinesAsync(CancellationToken ct = default)
            => Task.FromResult(_machines);
    }
}
