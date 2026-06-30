using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Components;
using VendingManager.Web.Services;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class PdcUnitCatalogTests
{
    [Fact]
    public void HardcodedUnits_ReturnsFourItemsInOrder()
    {
        var units = PdcUnitCatalog.HardcodedUnits;

        units.Should().HaveCount(4);

        // Item 0: Todas
        units[0].Id.Should().Be(0);
        units[0].Label.Should().Be("Todas");
        units[0].Secondary.Should().BeNull();
        units[0].Status.Should().Be(PdcUnitStatus.All);

        // Item 1: Máquina 001
        units[1].Id.Should().Be(1);
        units[1].Label.Should().Be("Máquina 001");
        units[1].Secondary.Should().Be("2410280012 · Online");
        units[1].Status.Should().Be(PdcUnitStatus.Online);

        // Item 2: Máquina 002
        units[2].Id.Should().Be(2);
        units[2].Label.Should().Be("Máquina 002");
        units[2].Secondary.Should().Be("2410280047 · Online");
        units[2].Status.Should().Be(PdcUnitStatus.Online);

        // Item 3: Máquina 003
        units[3].Id.Should().Be(3);
        units[3].Label.Should().Be("Máquina 003");
        units[3].Secondary.Should().Be("2410280089 · Stock bajo");
        units[3].Status.Should().Be(PdcUnitStatus.StockLow);
    }
}

public class PanelDeControlSidebarTests : TestContext
{
    public PanelDeControlSidebarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void PanelDeControlSidebar_RendersAllFourItems()
    {
        var cut = RenderComponent<PanelDeControlSidebar>();

        var items = cut.FindAll(".pdc-unit-item");
        items.Should().HaveCount(4);
        items[0].TextContent.Trim().Should().Contain("Todas");
        items[1].TextContent.Trim().Should().Contain("Máquina 001");
        items[2].TextContent.Trim().Should().Contain("Máquina 002");
        items[3].TextContent.Trim().Should().Contain("Máquina 003");
    }

    [Fact]
    public void PanelDeControlSidebar_TodasItemIsActiveAndHasGlyph()
    {
        var cut = RenderComponent<PanelDeControlSidebar>();

        var items = cut.FindAll(".pdc-unit-item");
        items.Should().HaveCount(4);

        var todasItem = items[0];
        Assert.Contains("pdc-unit-item--active", todasItem.ClassName);
        todasItem.InnerHtml.Should().Contain("◈");

        // Todas should NOT have a dot or secondary text
        var dot = todasItem.QuerySelector(".pdc-dot");
        dot.Should().BeNull();
        var secondary = todasItem.QuerySelector(".pdc-unit-item__secondary");
        secondary.Should().BeNull();
    }

    [Fact]
    public void PanelDeControlSidebar_OnlineMachinesHaveGreenDot()
    {
        var cut = RenderComponent<PanelDeControlSidebar>();

        var items = cut.FindAll(".pdc-unit-item");
        var machine1 = items[1];
        var machine2 = items[2];

        var dot1 = machine1.QuerySelector(".pdc-dot.pdc-dot--ok");
        dot1.Should().NotBeNull();

        var dot2 = machine2.QuerySelector(".pdc-dot.pdc-dot--ok");
        dot2.Should().NotBeNull();
    }

    [Fact]
    public void PanelDeControlSidebar_StockLowMachineHasOrangeDot()
    {
        var cut = RenderComponent<PanelDeControlSidebar>();

        var items = cut.FindAll(".pdc-unit-item");
        var machine3 = items[3];

        var warnDot = machine3.QuerySelector(".pdc-dot.pdc-dot--warn");
        warnDot.Should().NotBeNull();

        // Secondary text should also have the --warn class
        var secondary = machine3.QuerySelector(".pdc-unit-item__secondary--warn");
        secondary.Should().NotBeNull();
        secondary!.TextContent.Trim().Should().Be("2410280089 · Stock bajo");
    }

    [Fact]
    public void PanelDeControlSidebar_RendersSecondaryText()
    {
        var cut = RenderComponent<PanelDeControlSidebar>();

        var items = cut.FindAll(".pdc-unit-item");

        var sec1 = items[1].QuerySelector(".pdc-unit-item__secondary");
        sec1.Should().NotBeNull();
        sec1!.TextContent.Trim().Should().Be("2410280012 · Online");

        var sec2 = items[2].QuerySelector(".pdc-unit-item__secondary");
        sec2.Should().NotBeNull();
        sec2!.TextContent.Trim().Should().Be("2410280047 · Online");

        var sec3 = items[3].QuerySelector(".pdc-unit-item__secondary");
        sec3.Should().NotBeNull();
        sec3!.TextContent.Trim().Should().Be("2410280089 · Stock bajo");
    }

    [Fact]
    public void PanelDeControlSidebar_RendersFooterWithCount()
    {
        var cut = RenderComponent<PanelDeControlSidebar>();

        cut.Markup.Should().Contain("3 unidades activas");
    }

    [Fact]
    public async Task PanelDeControlSidebar_ClickInvokesCallback()
    {
        var invokedId = -1;
        var cut = RenderComponent<PanelDeControlSidebar>(parameters => parameters
            .Add(p => p.OnUnitSelected, EventCallback.Factory.Create<int>(this, id => invokedId = id)));

        cut.FindAll(".pdc-unit-item button").Should().HaveCount(4);

        // Click Máquina 001 (index 1) → callback fires with id 1
        await cut.InvokeAsync(() => cut.FindAll(".pdc-unit-item button")[1].Click());
        invokedId.Should().Be(1);

        // Click Todas (index 0) → callback fires with id 0
        await cut.InvokeAsync(() => cut.FindAll(".pdc-unit-item button")[0].Click());
        invokedId.Should().Be(0);
    }

    [Fact]
    public void PanelDeControlSidebar_RendersWithoutServiceInjection()
    {
        // No IMachineOnlineService registered — should still render fine
        var cut = RenderComponent<PanelDeControlSidebar>();

        cut.FindAll(".pdc-sidebar").Should().HaveCount(1);
        cut.FindAll(".pdc-unit-item").Should().HaveCount(4);
    }

    [Fact]
    public void PanelDeControlSidebar_HeaderRendersUnidades()
    {
        var cut = RenderComponent<PanelDeControlSidebar>();

        cut.Markup.Should().Contain("UNIDADES");
    }
}
