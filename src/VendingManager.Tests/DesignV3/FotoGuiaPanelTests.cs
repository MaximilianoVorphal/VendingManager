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
///
/// T-C1: rendering behavior (R3.1a-b, R3.2a-c).
/// T-C3: JS interop wiring (R3.3a, R3.4a-d).
///
/// Rendering tests use Loose JSRuntime mode — JS calls auto-succeed with defaults.
/// JS-interop tests switch to Strict mode with SetupModule to assert real invocations.
///
/// IMPORTANT bUnit 1.39.5 API notes (discovered during PR2 revision):
///   - SetupModule returns BunitJSModuleInterop (inherits BunitJSInterop).
///   - SetupVoid/setup overloads require explicit 3-arg calls with InvocationMatcher.
///     Two-arg calls like module.SetupVoid("name") do NOT resolve to any overload.
///     Use module.SetupVoid("name", (InvocationMatcher)(inv => true)).SetVoidResult().
///   - Setup{T} requires same pattern: module.Setup<string>("label", (InvocationMatcher)(inv => true)).SetResult("...").
///   - Module-level invocations ARE recorded in module.Invocations when Setup + Set*Result used.
///   - Root-level calls (import) are recorded in JSInterop.Invocations.
/// </summary>
public class FotoGuiaPanelTests : TestContext
{
    public FotoGuiaPanelTests()
    {
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
        // Subtitle reflects real state: no photo loaded here
        cut.Markup.Should().Contain("· sin foto");
        cut.FindAll("button.rec-guia-close").Should().NotBeEmpty();
    }

    [Fact]
    public void Header_Subtitle_ShowsYaCargada_WhenPhotoPresent()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.Markup.Should().Contain("· ya cargada");
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
     * R3.4b — Cámara InputFile has capture="environment"
     * =================================================================== */

    [Fact]
    public void CameraInputFile_HasCaptureEnvironment()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.OnClose, () => { }));

        var cameraLabel = cut.Find(".rec-guia-camara");
        var input = cameraLabel.QuerySelector("input[type='file']");
        input.Should().NotBeNull();
        input!.GetAttribute("capture").Should().Be("environment");
    }

    /* ===================================================================
     * R3.4c — Subir InputFile has accept="image/*" inside hidden label
     * =================================================================== */

    [Fact]
    public void SubirInputFile_HasAcceptImage()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.OnClose, () => { }));

        var subirLabel = cut.Find(".rec-guia-subir");
        var input = subirLabel.QuerySelector("input[type='file']");
        input.Should().NotBeNull();
        input!.GetAttribute("accept").Should().Be("image/*");
        // Input is hidden via CSS: .rec-guia-subir ::deep input { display: none; }
        // (verified in FotoGuiaPanel.razor.css, not by computed style in bUnit)
    }

    // =====================================================================
    // JS-INTEROP TESTS — Strict mode with SetupModule
    // =====================================================================
    //
    // These tests switch to Strict JSRuntime mode and use SetupModule to
    // mock the foto-guia.js ES module. This allows asserting that the
    // Blazor component actually dispatches the expected JS interop calls
    // (import, initPanZoom, zoomIn, zoomOut, reset, label) rather than
    // just verifying elements exist in the DOM.
    //
    // IMPORTANT: Use the 3-arg SetupVoid/Setup<T> overloads with
    // (InvocationMatcher)(inv => true) + .SetVoidResult() / .SetResult().
    // Two-arg calls like module.SetupVoid("name") do NOT resolve correctly.

    /// <summary>
    /// Helper: set up a Strict-mode module mock for the foto-guia.js module
    /// with handlers for all functions called by the component.
    /// </summary>
    private BunitJSModuleInterop SetupFotoGuiaModule()
    {
        var module = JSInterop.SetupModule("./js/foto-guia.js");
        module.SetupVoid("initPanZoom", (InvocationMatcher)(inv => true)).SetVoidResult();
        module.SetupVoid("zoomIn", (InvocationMatcher)(inv => true)).SetVoidResult();
        module.SetupVoid("zoomOut", (InvocationMatcher)(inv => true)).SetVoidResult();
        module.SetupVoid("reset", (InvocationMatcher)(inv => true)).SetVoidResult();
        module.Setup<string>("label", (InvocationMatcher)(inv => true)).SetResult("100%");
        return module;
    }

    /* ===================================================================
     * R3.3a — initPanZoom invocation must be REAL-tested (was VACUOUS)
     *
     * Asserts that:
     *   1. The component called import("./js/foto-guia.js") via IJSRuntime
     *   2. The module's initPanZoom function was invoked with an element ref
     * =================================================================== */

    [Fact]
    public void Panel_LoadsJsModule_AndInvokesInitPanZoom()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = SetupFotoGuiaModule();

        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.WaitForAssertion(() =>
        {
            // The import call goes through IJSRuntime and is recorded on JSInterop
            JSInterop.Invocations.Should().Contain(i =>
                i.Identifier == "import"
                && i.Arguments.Any(a => a != null && a.ToString() != null
                    && a.ToString()!.Contains("foto-guia.js")));

            // initPanZoom is called on the IJSObjectReference returned by import
            // and is recorded on the module's invocations
            module.Invocations.Should().Contain(i => i.Identifier == "initPanZoom");
        });
    }

    /* ===================================================================
     * R3.4a — Zoom label shows "100%" (component reads label() after init)
     *
     * The module mock's label() returns "100%", so after OnAfterRenderAsync
     * the component's _zoomLabel field should be "100%".
     * =================================================================== */

    [Fact]
    public void ZoomLabel_Shows100Percent_AfterInit()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = SetupFotoGuiaModule();

        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.WaitForAssertion(() =>
        {
            cut.Find("span.rec-zoom-label").TextContent.Trim().Should().Be("100%");
        });
    }

    /* ===================================================================
     * R3.4d — Zoom buttons invoke module functions and re-read label
     *
     * Clicking − / + / Restablecer must call zoomOut / zoomIn / reset
     * on the module AND re-read label() to update the display.
     * =================================================================== */

    [Fact]
    public void ZoomIn_InvokesZoomInOnModule()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = SetupFotoGuiaModule();

        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        // Wait for first render + OnAfterRenderAsync to complete
        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations.Should().Contain(i => i.Identifier == "import");
        });

        var initialLabelCalls = module.Invocations.Count(i => i.Identifier == "label");
        var initialZoomInCalls = module.Invocations.Count(i => i.Identifier == "zoomIn");

        // Find the + button and click it
        var zoomInBtn = cut.FindAll("button.rec-zoom-btn")
            .First(b => b.TextContent.Trim() == "+");
        zoomInBtn.Click();

        cut.WaitForAssertion(() =>
        {
            module.Invocations.Count(i => i.Identifier == "zoomIn")
                .Should().Be(initialZoomInCalls + 1);
            // label is re-read after zoom
            module.Invocations.Count(i => i.Identifier == "label")
                .Should().BeGreaterThan(initialLabelCalls);
        });
    }

    [Fact]
    public void ZoomOut_InvokesZoomOutOnModule()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = SetupFotoGuiaModule();

        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations.Should().Contain(i => i.Identifier == "import");
        });

        var initialLabelCalls = module.Invocations.Count(i => i.Identifier == "label");
        var initialZoomOutCalls = module.Invocations.Count(i => i.Identifier == "zoomOut");

        var zoomOutBtn = cut.FindAll("button.rec-zoom-btn")
            .First(b => b.TextContent.Trim() == "−");
        zoomOutBtn.Click();

        cut.WaitForAssertion(() =>
        {
            module.Invocations.Count(i => i.Identifier == "zoomOut")
                .Should().Be(initialZoomOutCalls + 1);
            module.Invocations.Count(i => i.Identifier == "label")
                .Should().BeGreaterThan(initialLabelCalls);
        });
    }

    [Fact]
    public void ResetZoom_InvokesResetOnModule()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = SetupFotoGuiaModule();

        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations.Should().Contain(i => i.Identifier == "import");
        });

        var initialLabelCalls = module.Invocations.Count(i => i.Identifier == "label");
        var initialResetCalls = module.Invocations.Count(i => i.Identifier == "reset");

        var resetBtn = cut.FindAll("button.rec-zoom-btn")
            .First(b => b.TextContent.Trim().Contains("Restablecer"));
        resetBtn.Click();

        cut.WaitForAssertion(() =>
        {
            module.Invocations.Count(i => i.Identifier == "reset")
                .Should().Be(initialResetCalls + 1);
            module.Invocations.Count(i => i.Identifier == "label")
                .Should().BeGreaterThan(initialLabelCalls);
        });
    }

    // =====================================================================
    // STRENGTHENED TESTS (replace vacuous tests with real assertions)
    // =====================================================================

    /// <summary>
    /// Replace the previous vacuous "Panel_RendersWithoutJsCrash_WhenUrlSet"
    /// with a REAL assertion that the JS module was loaded, initPanZoom fired,
    /// AND the image still renders correctly.
    /// </summary>
    [Fact]
    public void Panel_RendersWithoutJsCrash_WhenUrlSet()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = SetupFotoGuiaModule();

        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.WaitForAssertion(() =>
        {
            // Module imported
            JSInterop.Invocations.Should().Contain(i =>
                i.Identifier == "import"
                && i.Arguments.Any(a => a != null && a.ToString() != null
                    && a.ToString()!.Contains("foto-guia.js")));

            // initPanZoom called with body element reference
            module.Invocations.Should().Contain(i => i.Identifier == "initPanZoom");

            // Image still renders correctly
            var img = cut.Find(".rec-guia-body img");
            img.GetAttribute("draggable").Should().Be("false");
        });
    }

    /// <summary>
    /// Replace the vacuous "ZoomLabelElement_Present" test with a real
    /// assertion that the label shows the expected value after init.
    /// </summary>
    [Fact]
    public void ZoomLabelElement_ShowsExpectedValue()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        SetupFotoGuiaModule();

        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.FotoGuiaUrl, "data:image/jpeg;base64,test")
            .Add(c => c.OnClose, () => { }));

        cut.WaitForAssertion(() =>
        {
            // The label() mock returns "100%", so the span must show that
            cut.Find("span.rec-zoom-label").TextContent.Trim().Should().Be("100%");
        });
    }

    /// <summary>
    /// Replace the vacuous "Footer_HasCameraAndSubirButtons" test with
    /// assertions for actual InputFile attributes (R3.4b + R3.4c).
    /// </summary>
    [Fact]
    public void Footer_HasCameraAndSubirInputFiles_WithCorrectAttributes()
    {
        var cut = RenderComponent<FotoGuiaPanel>(p => p
            .Add(c => c.MaquinaId, "1")
            .Add(c => c.OnClose, () => { }));

        // Cámara label with InputFile
        var cameraLabel = cut.Find(".rec-guia-camara");
        cameraLabel.TextContent.Trim().Should().Be("Cámara");
        var cameraInput = cameraLabel.QuerySelector("input[type='file']");
        cameraInput.Should().NotBeNull();
        cameraInput!.GetAttribute("capture").Should().Be("environment");
        cameraInput.GetAttribute("accept").Should().Be("image/*");

        // Subir label with InputFile
        var subirLabel = cut.Find(".rec-guia-subir");
        subirLabel.TextContent.Trim().Should().Be("Subir");
        var subirInput = subirLabel.QuerySelector("input[type='file']");
        subirInput.Should().NotBeNull();
        subirInput!.GetAttribute("accept").Should().Be("image/*");
        // Input is hidden via CSS: .rec-guia-subir ::deep input { display: none; }
    }
}
