using Bunit;
using FluentAssertions;
using VendingManager.Web.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class VmAdvancedComponentsTests : TestContext
{
    [Fact]
    public void VmKpiCard_RendersHeader()
    {
        var cut = RenderComponent<VmKpiCard>(parameters => parameters
            .Add(p => p.Header, "VENTAS HOY"));

        cut.Markup.Should().Contain("VENTAS HOY");
    }

    [Fact]
    public void VmKpiCard_RendersValue()
    {
        var cut = RenderComponent<VmKpiCard>(parameters => parameters
            .Add(p => p.Value, "$1.240.500"));

        cut.Markup.Should().Contain("$1.240.500");
    }

    [Fact]
    public void VmKpiCard_RendersCaption()
    {
        var cut = RenderComponent<VmKpiCard>(parameters => parameters
            .Add(p => p.Caption, "Total acumulado"));

        cut.Markup.Should().Contain("Total acumulado");
    }

    [Fact]
    public void VmSelect_RendersOptions()
    {
        var options = new[]
        {
            new VmSelect.VmSelectOption("a", "A"),
            new VmSelect.VmSelectOption("b", "B")
        };

        var cut = RenderComponent<VmSelect>(parameters => parameters
            .Add(p => p.Options, options));

        cut.Markup.Should().Contain(">A<");
        cut.Markup.Should().Contain(">B<");
    }

    [Fact]
    public void VmSelect_BindsValue()
    {
        var selected = "b";
        var options = new[]
        {
            new VmSelect.VmSelectOption("a", "A"),
            new VmSelect.VmSelectOption("b", "B")
        };

        var cut = RenderComponent<VmSelect>(parameters => parameters
            .Add(p => p.Value, selected)
            .Add(p => p.ValueChanged, (string v) => selected = v)
            .Add(p => p.Options, options));

        cut.Find("select").Change("a");

        selected.Should().Be("a");
    }

    [Fact]
    public void VmSlotCard_RendersSlot()
    {
        var cut = RenderComponent<VmSlotCard>(parameters => parameters
            .Add(p => p.Slot, "1")
            .Add(p => p.Product, "Coca-Cola")
            .Add(p => p.Quantity, 3)
            .Add(p => p.Capacity, 10));

        cut.Markup.Should().Contain("SLOT 1");
        cut.Markup.Should().Contain("Coca-Cola");
        cut.Markup.Should().Contain("3 / 10");
    }

    [Fact]
    public void VmSlotCard_Highlighted_AppliesClass()
    {
        var cut = RenderComponent<VmSlotCard>(parameters => parameters
            .Add(p => p.Slot, "1")
            .Add(p => p.Product, "Coca-Cola")
            .Add(p => p.Quantity, 3)
            .Add(p => p.Capacity, 10)
            .Add(p => p.Highlighted, true));

        var style = cut.Find("div").GetAttribute("style");
        style.Should().Contain("var(--fill-good)");
    }

    [Fact]
    public void VmSlotCard_OnChange_Fires()
    {
        var newQuantity = 0;
        var cut = RenderComponent<VmSlotCard>(parameters => parameters
            .Add(p => p.Slot, "1")
            .Add(p => p.Product, "Coca-Cola")
            .Add(p => p.Quantity, 3)
            .Add(p => p.Capacity, 10)
            .Add(p => p.OnChange, (int q) => newQuantity = q));

        cut.FindAll("button")[1].Click();

        newQuantity.Should().Be(4);
    }

    [Fact]
    public void VmNavbar_RendersWordmark()
    {
        var cut = RenderComponent<VmNavbar>();

        cut.Markup.Should().Contain("VENDING");
    }

    [Fact]
    public void VmNavbar_RendersNavItems()
    {
        var cut = RenderComponent<VmNavbar>();

        cut.Markup.Should().Contain("Panel de Control");
        cut.Markup.Should().Contain("Templates Recarga");
        cut.Markup.Should().Contain("Caja");
    }

    [Fact]
    public void VmSlotCard_HasSoftRadius()
    {
        var cut = RenderComponent<VmSlotCard>(parameters => parameters
            .Add(p => p.Slot, "1")
            .Add(p => p.Product, "Coca-Cola")
            .Add(p => p.Quantity, 3)
            .Add(p => p.Capacity, 10));

        var style = cut.Find("div").GetAttribute("style");
        style.Should().Contain("border-radius:var(--radius-soft)");
    }
}
