using System;
using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using VendingManager.Web.Components;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// bUnit tests for FotoGuiaPanel component (Slice C).
/// T-C1: rendering behavior (R3.1a-b, R3.2a-c).
/// T-C3: JS interop (R3.3a, R3.4a-d) — see separate test methods below.
///
/// Uses Loose JSRuntime mode for T-C1/T-C2 rendering tests.
/// Strict mode with SetupModule is introduced in T-C3 for JS interop verification.
/// </summary>
public class FotoGuiaPanelTests : TestContext
{
    public FotoGuiaPanelTests()
    {
        // Loose mode — all JS calls auto-succeed with defaults.
        // The component handles null controller references gracefully
        // by keeping the default zoom label "100%".
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /* ===================================================================
     * R3.1a — Panel renders as <aside class="rec-guia">
     * =================================================================== */

    [Fact]
    public void Panel_RendersAsAside_WithRecGuiaClass()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.OnClose, () => { }));

        cut.FindAll("aside.rec-guia").Should().NotBeEmpty();
    }

    /* ===================================================================
     * R3.2a — Image renders with draggable="false" when FotoGuiaUrl set
     * =================================================================== */

    [Fact]
    public void Image_HasDraggableFalse_WhenUrlSet()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        var img = cut.Find("img");
        img.GetAttribute("draggable").Should().Be("false");
        img.GetAttribute("src").Should().Be("data:image/jpeg;base64,test");
    }

    /* ===================================================================
     * R3.2b — Empty-state when no FotoGuiaUrl
     * =================================================================== */

    [Fact]
    public void EmptyState_WhenNoUrl()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, (string?)null)
            .Add(c => c.OnClose, () => { }));

        cut.FindAll("img").Should().BeEmpty();
        cut.Markup.Should().Contain("Sin foto");
    }

    /* ===================================================================
     * R3.2c — Close × fires OnClose
     * =================================================================== */

    [Fact]
    public void CloseButton_FiresOnClose_WhenClicked()
    {
        bool wasCalled = false;
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, (Action)(() => wasCalled = true)));

        cut.Find("button.rec-guia-close").Click();

        wasCalled.Should().BeTrue();
    }

    /* ===================================================================
     * Header — title, subtitle, close × button
     * =================================================================== */

    [Fact]
    public void Header_HasTitleSubtitleAndClose()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.OnClose, () => { }));

        cut.Markup.Should().Contain("Foto guía");
        cut.Markup.Should().Contain("Máq 1");
        cut.Markup.Should().Contain("· ya cargada");
        cut.FindAll("button.rec-guia-close").Should().NotBeEmpty();
    }

    /* ===================================================================
     * R3.4a — Zoom label element present in markup
     * =================================================================== */

    [Fact]
    public void ZoomLabelElement_PresentInFooter()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.FindAll("span.rec-zoom-label").Should().NotBeEmpty();
    }

    /* ===================================================================
     * R3.4d — Zoom controls present (− / + / restablecer)
     * =================================================================== */

    [Fact]
    public void ZoomControls_HaveMinusPlusAndReset()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        var buttons = cut.FindAll(".rec-guia-footer button");
        var allText = string.Join(" ", buttons.Select(b => b.TextContent.Trim()));
        allText.Should().Contain("−");
        allText.Should().Contain("+");
        allText.Should().Contain("Restablecer");
    }

    /* ===================================================================
     * R3.4b-c — Cámara and Subir buttons in footer
     * =================================================================== */

    [Fact]
    public void Footer_HasCameraAndSubirButtons()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.OnClose, () => { }));

        cut.Markup.Should().Contain("Cámara");
        cut.Markup.Should().Contain("Subir");
    }

    /* ===================================================================
     * R3.3a — Component renders without JS crash (initPanZoom via Loose)
     * =================================================================== */

    [Fact]
    public void Panel_RendersWithoutJsCrash_WhenUrlSet()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        // In Loose mode, JS calls auto-succeed, so no crash.
        // The image element should render under .rec-guia-body.
        cut.FindAll(".rec-guia-body img").Should().NotBeEmpty();
    }
}
