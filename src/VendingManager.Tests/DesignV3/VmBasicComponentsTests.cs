using System.IO;
using Bunit;
using FluentAssertions;
using VendingManager.Web.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class VmBasicComponentsTests : TestContext
{
    [Fact]
    public void VmBadge_RendersText()
    {
        var cut = RenderComponent<VmBadge>(parameters => parameters.AddChildContent("Activo"));

        cut.Markup.Should().Contain("Activo");
    }

    [Fact]
    public void VmBadge_RendersWithVariant()
    {
        var cut = RenderComponent<VmBadge>(parameters => parameters
            .Add(p => p.Variant, "danger")
            .AddChildContent("Sin Stock"));

        cut.Markup.Should().Contain("Sin Stock");
        cut.Find("span").GetAttribute("style").Should().Contain("var(--signal-danger)");
    }

    [Fact]
    public void VmButton_RendersText()
    {
        var cut = RenderComponent<VmButton>(parameters => parameters.AddChildContent("Guardar"));

        cut.Markup.Should().Contain("Guardar");
    }

    [Fact]
    public void VmButton_RendersWithIcon()
    {
        var cut = RenderComponent<VmButton>(parameters => parameters
            .Add(p => p.Icon, "bi-download")
            .AddChildContent("Exportar"));

        cut.Markup.Should().Contain("bi-download");
    }

    [Fact]
    public void VmButton_OnClick_Fires()
    {
        var clicked = false;
        var cut = RenderComponent<VmButton>(parameters => parameters
            .Add(p => p.OnClick, () => clicked = true)
            .AddChildContent("Click me"));

        cut.Find("button").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void VmCard_RendersContent()
    {
        var cut = RenderComponent<VmCard>(parameters => parameters.AddChildContent("contenido"));

        cut.Markup.Should().Contain("contenido");
    }

    [Fact]
    public void VmCard_RendersHeader()
    {
        var cut = RenderComponent<VmCard>(parameters => parameters
            .Add(p => p.Header, "STOCK")
            .AddChildContent("x"));

        cut.Markup.Should().Contain("STOCK");
    }

    [Fact]
    public void VmInput_RendersLabel()
    {
        var cut = RenderComponent<VmInput>(parameters => parameters.Add(p => p.Label, "Código"));

        cut.Find("label").TextContent.Should().Be("Código");
    }

    [Fact]
    public void VmInput_BindsValue()
    {
        var value = "initial";
        var cut = RenderComponent<VmInput>(parameters => parameters
            .Add(p => p.Value, value)
            .Add(p => p.ValueChanged, (string v) => value = v));

        cut.Find("input").Input("changed");

        value.Should().Be("changed");
    }

    [Fact]
    public void VmInput_NullableFix_DoesNotWarn()
    {
        var path = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "VendingManager.Web", "Shared", "VmInput.razor"));

        File.Exists(path).Should().BeTrue("VmInput.razor must exist");

        var source = File.ReadAllText(path);
        source.Should().Contain("e.Value?.ToString() ?? string.Empty");
    }
}
