using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Components;
using VendingManager.Web.Pages;
using VendingManager.Web.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// PR3b editor fidelity tests (REQ-FID-2). One test per task; each describes a
/// specific fidelity requirement from Recarga.dc.html lines 124-268.
///
/// The editor scaffold (rec-editor, rec-bar, rec-rail, rec-shelf, rec-floor,
/// rec-grid, rec-iconbox, rec-mcard, rec-status, rec-iconbtn, rec-stepper,
/// rec-segment, rec-badge, rec-progress, rec-empty, rec-tag-empty) lives in
/// the canonical <c>vm-recarga.css</c>. The page-specific rules
/// (rec-mcard__select, rec-mcard-config, rec-slot*, rec-bottombar-*,
/// rec-toast, rec-name-btn, etc.) stay in <c>TemplatesRecarga.razor.css</c>.
/// </summary>
public class TemplatesRecargaEditorFidelityTests : TestContext
{
    private readonly EditorFidelityMockHandler _mockHandler;

    public TemplatesRecargaEditorFidelityTests()
    {
        _mockHandler = new EditorFidelityMockHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider, AuthenticatedAuthStateProvider>();
        Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static string ProjectCssPath => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css"));

    /// <summary>
    /// Canonical design-system CSS — owner of the editor scaffold, the
    /// rail/shelf, the floor/slot grid, the segmented/stepper/status/
    /// iconbtn controls, the badges and progress bar, and the toast-free
    /// keyframes (recBlink).
    /// </summary>
    private static string CanonicalCssPath => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "wwwroot", "css", "vm-recarga.css"));

    private void OpenEditor(IRenderedComponent<EditorFidelityTestHost> cut)
    {
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Template Activo"));
        var abrir = cut.FindComponents<VmButton>().First(b => b.Markup.Contains("Abrir"));
        abrir.Find("button").Click();
        // Editor top bar is .rec-bar in the canonical CSS (was .rec-topbar).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("rec-bar"));
    }

    // =====================================================================
    // TASK-3b.1: Editor top bar
    // =====================================================================

    [Fact]
    public void Editor_Topbar_HasThreePxBlackBottomBorder()
    {
        // Recarga.dc.html line 128: border-bottom:3px solid var(--ink-900)
        // Canonical .rec-bar uses --border-3 token which resolves to
        // 3px solid var(--ink-900). The selector is .rec-bar (was .rec-topbar).
        var css = File.ReadAllText(CanonicalCssPath);

        css.Should().Contain(".rec-bar");
        css.Should().MatchRegex(@"\.rec-bar\s*\{[^}]*border-bottom:\s*(?:3px\s+solid\s+var\(--ink-900\)|var\(--border-3\))");
    }

    [Fact]
    public void Editor_Topbar_HasVolverTitleStatusAndActions()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // Editor top bar is .rec-bar (was .rec-topbar).
        var topbar = cut.Find(".rec-bar");

        // Left side: Volver button (outline)
        topbar.InnerHtml.Should().Contain("Volver");
        topbar.QuerySelector(".rec-bar .btn-outline-dark, .rec-bar button[aria-label*='Volver'], .rec-bar > div:first-child button")
            .Should().NotBeNull("Volver button must be present in topbar");

        // Center: title + status badge + description.
        // Canonical badge class is .rec-badge (was .rec-tag).
        topbar.InnerHtml.Should().Contain("rec-badge");
        topbar.InnerHtml.Should().Contain("PENDIENTE");

        // Right: action buttons in order
        var actionButtons = cut.FindComponents<VmButton>()
            .Where(b => b.Markup.Contains("Editar")
                     || b.Markup.Contains("Analizar stockout")
                     || b.Markup.Contains("Sincronizar todo")
                     || b.Markup.Contains("Finalizar")
                     || b.Markup.Contains("Finalizado"))
            .ToList();

        actionButtons.Count.Should().BeGreaterOrEqualTo(3);
    }

    // =====================================================================
    // TASK-3b.2: Master-detail split layout
    // =====================================================================

    [Fact]
    public void Editor_SplitLayout_RailIs326pxAndEstanteriaIsFlex()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // All editor scaffold rules now live in the canonical CSS. The
        // rail width is driven by --rec-rail-w (defined as 326px in :root).
        var css = File.ReadAllText(CanonicalCssPath);

        // --rec-rail-w: 326px in :root (the canonical CSS uses the token).
        css.Should().MatchRegex(@":root\s*\{[^}]*--rec-rail-w:\s*326px");

        // .rec-rail uses the var() token, not the literal 326px.
        css.Should().MatchRegex(@"\.rec-rail\s*\{[^}]*flex:\s*0\s+0\s+var\(--rec-rail-w\)");
        css.Should().MatchRegex(@"\.rec-rail\s*\{[^}]*width:\s*var\(--rec-rail-w\)");
        css.Should().MatchRegex(@"\.rec-rail\s*\{[^}]*border-right:\s*(?:2px\s+solid\s+var\(--ink-900\)|var\(--border-2\))");

        // Split container: flex
        css.Should().MatchRegex(@"\.rec-split\s*\{[^}]*display:\s*flex");

        // Media query stacks vertically <1000px
        css.Should().MatchRegex(@"@media\s*\(\s*max-width:\s*1000px\s*\)\s*\{[^}]*\.rec-split\s*\{[^}]*flex-direction:\s*column");
    }

    // =====================================================================
    // TASK-3b.3: Machine card in rail
    // =====================================================================

    [Fact]
    public void Editor_RailCard_HasSelectTrashDateTimeBarConfigVacios()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        var cards = cut.FindAll(".rec-mcard");
        cards.Should().NotBeEmpty();

        var card = cards[0];

        // Select with maquina options
        card.QuerySelector("select").Should().NotBeNull("each rail card must have a machine select");
        card.InnerHtml.Should().Contain("<option");

        // Trash button is now rendered by <VmIconButton Variant="danger"> which
        // produces a <button class="rec-iconbtn rec-iconbtn--danger">.
        var trashButton = card.QuerySelector("button[aria-label*='Quitar'], button[aria-label*='Maquina']");
        trashButton.Should().NotBeNull("each rail card must have a trash button");
        trashButton!.ClassList.Should().Contain("rec-iconbtn");
        trashButton.ClassList.Should().Contain("rec-iconbtn--danger");

        // Date input
        card.QuerySelector("input[type='date']").Should().NotBeNull("rail card must have a date input");

        // Time input (per design: type='time')
        card.QuerySelector("input[type='time']").Should().NotBeNull("rail card must have a time input (type='time')");

        // Bar (loading bar = .rec-progress with .fill-* child) + units/cap
        card.InnerHtml.Should().Contain("rec-progress");
        card.InnerHtml.Should().Contain("/");

        // Config label
        card.InnerHtml.Should().Contain("configurados");
    }

    [Fact]
    public void Editor_RailCard_TrashButton_HasRedBorder()
    {
        // Canonical .rec-iconbtn--danger has width:32px; height:32px;
        // border-color: var(--signal-danger) (red).
        var css = File.ReadAllText(CanonicalCssPath);

        css.Should().MatchRegex(@"\.rec-iconbtn\s*\{[^}]*width:\s*32px");
        css.Should().MatchRegex(@"\.rec-iconbtn\s*\{[^}]*height:\s*32px");
        css.Should().MatchRegex(@"\.rec-iconbtn--danger\s*\{[^}]*border-color:\s*var\(--signal-danger\)");
    }

    [Fact]
    public void Editor_RailCard_HoverAndActiveStates()
    {
        // Canonical .rec-mcard:hover has a 3px 3px 0 hard-offset shadow;
        // .rec-mcard.is-active has an inset 5px 0 0 --signal-success
        // accent + outline.
        var css = File.ReadAllText(CanonicalCssPath);

        // Hover: 3px 3px 0 shadow
        css.Should().MatchRegex(@"\.rec-mcard:hover\s*\{[^}]*box-shadow:\s*3px\s+3px\s+0");

        // Active: 5px 0 0 inset green (canonical uses .is-active on .rec-mcard)
        css.Should().MatchRegex(@"\.rec-mcard\.is-active\s*\{[^}]*box-shadow:\s*inset\s+5px\s+0\s+0\s+var\(--signal-success\)");
    }

    // =====================================================================
    // TASK-3b.4: Estanteria header
    // =====================================================================

    [Fact]
    public void Editor_EstanteriaHeader_HasGridIconTileAndTitle()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // Canonical estanteria header class is .rec-shelf__head
        // (was .rec-est-header).
        var header = cut.Find(".rec-shelf__head");

        // Black 34x34 grid icon box (was .rec-icon-tile, now .rec-iconbox)
        var iconBox = header.QuerySelector(".rec-iconbox");
        iconBox.Should().NotBeNull("estanteria header must have a grid icon box");
        iconBox!.ClassList.Should().Contain("rec-iconbox");

        // Title: "Estantería · Máquina {id}" (uppercase via CSS)
        header.InnerHtml.Should().Contain("Estantería");
        header.InnerHtml.Should().Contain("Máquina");

        // Subtitle: "{pct}% LLENO · {N} SLOTS"
        header.InnerHtml.Should().Contain("LLENO");
        header.InnerHtml.Should().Contain("SLOTS");
    }

    [Fact]
    public void Editor_EstanteriaHeader_IconTileCss_Black34x34()
    {
        // Canonical .rec-iconbox is the 34x34 black icon container
        // (was .rec-icon-tile). It lives in vm-recarga.css.
        var css = File.ReadAllText(CanonicalCssPath);

        css.Should().MatchRegex(@"\.rec-iconbox\s*\{[^}]*width:\s*34px");
        css.Should().MatchRegex(@"\.rec-iconbox\s*\{[^}]*height:\s*34px");
        css.Should().MatchRegex(@"\.rec-iconbox\s*\{[^}]*background:\s*var\(--ink-900\)");
        css.Should().MatchRegex(@"\.rec-iconbox\s*\{[^}]*color:\s*var\(--paper-0\)");
    }

    [Fact]
    public void Editor_EstanteriaHeader_CssHasTwoPxBlackBottomBorder()
    {
        // Canonical .rec-shelf__head has border-bottom: var(--border-2) which
        // resolves to 2px solid var(--ink-900) (was .rec-est-header).
        var css = File.ReadAllText(CanonicalCssPath);

        css.Should().MatchRegex(@"\.rec-shelf__head\s*\{[^}]*border-bottom:\s*(?:2px\s+solid\s+var\(--ink-900\)|var\(--border-2\))");
    }

    // =====================================================================
    // TASK-3b.5: Estanteria toolbar
    // =====================================================================

    [Fact]
    public void Editor_Toolbar_HasSearchDensityAndPhotoButtons()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // Search input (canonical class .rec-search)
        var search = cut.Find("input.rec-search");
        search.Should().NotBeNull("toolbar must have a search input");
        search.GetAttribute("placeholder").Should().Contain("Buscar");

        // Density toggle is now <VmSegmented> which renders .rec-segment
        // (was .rec-density).
        var segment = cut.Find(".rec-segment");
        segment.Should().NotBeNull();
        segment.InnerHtml.Should().Contain("Cómoda");
        segment.InnerHtml.Should().Contain("Compacta");

        // Photo buttons
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Foto recarga"));
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Foto guia"));
    }

    [Fact]
    public void Editor_Toolbar_DensityToggle_HasActiveClass()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // VmSegmented renders the two buttons as direct children of
        // .rec-segment, with .is-active on the selected one (was
        // .rec-density__btn with .active).
        var segmentButtons = cut.FindAll(".rec-segment > button");
        segmentButtons.Count.Should().Be(2);

        // Exactly one of them should be active initially (Cómoda)
        var activeCount = segmentButtons.Count(b => b.ClassList.Contains("is-active"));
        activeCount.Should().Be(1, "exactly one density button must be active by default");
    }

    [Fact]
    public void Editor_Toolbar_SearchAndDensity_CssProperties()
    {
        // Recarga.dc.html line 198: search input width:204px, mono 0.76rem, 2px black border
        // Recarga.dc.html line 199-204: density toggle group, 2px black border.
        // Both rules now live in the canonical CSS (vm-recarga.css).
        var css = File.ReadAllText(CanonicalCssPath);

        // Search: 204px wide
        css.Should().MatchRegex(@"\.rec-search\s*\{[^}]*width:\s*204px");
        // Search: 2px black border (or token)
        css.Should().MatchRegex(@"\.rec-search\s*\{[^}]*border:\s*(?:2px\s+solid\s+var\(--ink-900\)|var\(--border-2\))");

        // Segment container: 2px black border
        css.Should().MatchRegex(@"\.rec-segment\s*\{[^}]*border:\s*(?:2px\s+solid\s+var\(--ink-900\)|var\(--border-2\))");
    }

    // =====================================================================
    // TASK-3b.6: PISOS sections
    // =====================================================================

    [Fact]
    public void Editor_Pisos_HasTagDividerAndSummary()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // PISO N tag
        cut.Markup.Should().Contain("PISO 1");
        // Canonical piso tag class is .rec-floor__tag (was .rec-piso-tag)
        var pisoTag = cut.Find(".rec-floor__tag");
        pisoTag.Should().NotBeNull();
        pisoTag.TextContent.Should().MatchRegex(@"PISO\s+\d+");

        // "X slots" summary
        cut.Markup.Should().MatchRegex(@"\d+\s+slots");

        // Piso grid
        var grid = cut.Find(".rec-grid");
        grid.Should().NotBeNull();
    }

    [Fact]
    public void Editor_Pisos_GridTemplate_MinMax246px()
    {
        // .rec-grid lives in the canonical CSS. The minmax uses
        // var(--rec-card-min) which is defined as 246px in :root.
        var css = File.ReadAllText(CanonicalCssPath);

        // :root defines --rec-card-min: 246px
        css.Should().MatchRegex(@":root\s*\{[^}]*--rec-card-min:\s*246px");

        // .rec-grid uses the var() token, not the literal 246px.
        css.Should().MatchRegex(@"\.rec-grid\s*\{[^}]*grid-template-columns:\s*repeat\(auto-fill,\s*minmax\(var\(--rec-card-min\),\s*1fr\)\)");
        css.Should().MatchRegex(@"\.rec-grid\s*\{[^}]*gap:\s*12px");
    }

    [Fact]
    public void Editor_Pisos_PisoTagCss_BlackBgWhiteMono()
    {
        // Recarga.dc.html line 214: background:var(--ink-900); color:#fff; font-family:var(--font-mono);
        // font-size:0.7rem; padding:4px 12px; text-transform:uppercase
        // Canonical class is .rec-floor__tag (was .rec-piso-tag) in vm-recarga.css.
        var css = File.ReadAllText(CanonicalCssPath);

        css.Should().MatchRegex(@"\.rec-floor__tag\s*\{[^}]*background:\s*var\(--ink-900\)");
        css.Should().MatchRegex(@"\.rec-floor__tag\s*\{[^}]*color:\s*var\(--paper-0\)");
        css.Should().MatchRegex(@"\.rec-floor__tag\s*\{[^}]*padding:\s*4px\s+12px");
        css.Should().MatchRegex(@"\.rec-floor__tag\s*\{[^}]*text-transform:\s*uppercase");
    }

    // =====================================================================
    // TASK-3b.7: Slot card
    // =====================================================================

    [Fact]
    public void Editor_SlotCard_HasSlotIdPickerBarStepperMax()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        var slots = cut.FindAll(".rec-slot");
        slots.Should().NotBeEmpty();

        var slot = slots[0];

        // SLOT NN label
        slot.InnerHtml.Should().MatchRegex(@"Slot\s+\w+");

        // Product picker button (kept selector in page-specific CSS)
        var picker = slot.QuerySelector(".rec-slot-pick");
        picker.Should().NotBeNull("slot card must have a product picker button");

        // Bar (loading bar = .rec-progress) + qty/cap
        slot.InnerHtml.Should().Contain("rec-progress");
        slot.InnerHtml.Should().Contain("/");

        // Stepper controls are now rendered by <VmStepper> which produces
        // − / value / + / Máx inside a .rec-stepper container.
        var stepper = slot.QuerySelector(".rec-stepper");
        stepper.Should().NotBeNull("slot card must have a stepper");
        stepper!.InnerHtml.Should().Contain("−");
        stepper.InnerHtml.Should().Contain("+");
        // Markup-side text is "Máx" (Spanish); text-transform:uppercase
        // is a CSS effect and does not alter the rendered text.
        stepper.InnerHtml.Should().Contain("Máx");
    }

    [Fact]
    public void Editor_SlotCard_BorderException_OnePxGrayWithSoftShadow()
    {
        // The slot card is the design system EXCEPTION: 1px solid #d1d5db
        // + radius 6px + soft shadow. The .rec-slot rule stays in the
        // project CSS (page-specific).
        var css = File.ReadAllText(ProjectCssPath);

        css.Should().MatchRegex(@"\.rec-slot\s*\{[^}]*border:\s*1px\s+solid\s+#d1d5db");
        css.Should().MatchRegex(@"\.rec-slot\s*\{[^}]*border-radius:\s*6px");
        css.Should().MatchRegex(@"\.rec-slot\s*\{[^}]*box-shadow:\s*var\(--shadow-soft\)");
    }

    [Fact]
    public void Editor_SlotCard_StepperButtons_44x42WithTwoPxBorder()
    {
        // Canonical .rec-stepper__btn (was .rec-slot-step) is 44×42 with
        // 2px black border; .rec-stepper__max (was .rec-slot-max) is 42px
        // tall with black bg + paper-0 text.
        var css = File.ReadAllText(CanonicalCssPath);

        // − and + buttons: 44x42, 2px black border
        // --border-2 token resolves to 2px solid var(--ink-900).
        css.Should().MatchRegex(@"\.rec-stepper__btn\s*\{[^}]*width:\s*44px");
        css.Should().MatchRegex(@"\.rec-stepper__btn\s*\{[^}]*height:\s*42px");
        css.Should().MatchRegex(@"\.rec-stepper__btn\s*\{[^}]*border:\s*(?:2px\s+solid\s+var\(--ink-900\)|var\(--border-2\))");

        // MÁX button: 42px height, 2px black border, black bg
        css.Should().MatchRegex(@"\.rec-stepper__max\s*\{[^}]*height:\s*42px");
        css.Should().MatchRegex(@"\.rec-stepper__max\s*\{[^}]*border:\s*(?:2px\s+solid\s+var\(--ink-900\)|var\(--border-2\))");
        css.Should().MatchRegex(@"\.rec-stepper__max\s*\{[^}]*background:\s*var\(--ink-900\)");
        css.Should().MatchRegex(@"\.rec-stepper__max\s*\{[^}]*color:\s*var\(--paper-0\)");
    }

    [Fact]
    public void Editor_SlotCard_PriceDisplays_WhenSlotHasProduct()
    {
        // REQ-11: price displayed as $N.NNN when slot has matching ProductoId
        // Mock data: slot A2 has ProductoId=1 with PrecioVenta=1200
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("rec-slot"));

        // The slot card with product should show the formatted price
        cut.Markup.Should().Contain("$1.200");
    }

    [Fact]
    public void Editor_SlotCard_ShowsDash_WhenSlotHasNoPrice()
    {
        // REQ-FID-2 dash scenario: empty/no-price slots must render $— in the
        // price span (not omit it). Mock data: slot A1 has ProductoId=null,
        // Estado=0 (Vacio) — marked with .rec-slot-empty class.
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("rec-slot-empty"));

        // The empty slot card must contain a price span with $— text.
        // Use regex on the full markup to scope the assertion to the empty
        // slot's HTML (Bunit.IElement.QuerySelector scoping is unreliable
        // across bunit versions, so a markup-level regex is the safest
        // approach to assert the price span lives INSIDE the empty slot).
        // The regex uses [\s\S] instead of . to match across newlines because
        // Blazor renders the slot HTML across multiple lines.
        cut.Markup.Should().MatchRegex(
            @"<div\s+class=""rec-slot\s+rec-slot-empty""[^>]*>[\s\S]*?<span\s+class=""rec-slot-price[^""]*""[^>]*>\s*\$\u2014\s*</span>[\s\S]*?</div>",
            "empty slot must render a <span class='rec-slot-price'>$—</span> per REQ-FID-2");
    }

    // =====================================================================
    // TASK-3b.8: Bottom bar
    // =====================================================================

    [Fact]
    public void Editor_BottomBar_HasCargaTotalsVaciosAndActions()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // Canonical bottom bar class is .rec-shelf__foot (was .rec-bottombar).
        var bottom = cut.Find(".rec-shelf__foot");

        // "Carga máquina" small mono uppercase
        bottom.InnerHtml.Should().Contain("Carga máquina");

        // Totals: bold units + muted cap "u." (split across nested span)
        var totals = bottom.QuerySelector(".rec-bottombar-totals");
        totals.Should().NotBeNull("bottom bar must have a totals element");
        var cap = bottom.QuerySelector(".rec-bottombar-cap");
        cap.Should().NotBeNull("totals must have a muted cap span");
        cap!.TextContent.Should().Contain("u.");

        // Buttons
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Vaciar máquina"));
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Reset"));
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Guardar carga"));
    }

    [Fact]
    public void Editor_BottomBar_HasTwoPxBlackTopBorder()
    {
        // Canonical .rec-shelf__foot has border-top: var(--border-2) which
        // resolves to 2px solid var(--ink-900) (was .rec-bottombar).
        var css = File.ReadAllText(CanonicalCssPath);

        // --border-2 token resolves to 2px solid var(--ink-900).
        css.Should().MatchRegex(@"\.rec-shelf__foot\s*\{[^}]*border-top:\s*(?:2px\s+solid\s+var\(--ink-900\)|var\(--border-2\))");
    }

    [Fact]
    public void Editor_BottomBar_CapPart_HasMutedStyle()
    {
        // Recarga.dc.html line 256: / {cap} u. in muted color
        // .rec-bottombar-cap stays in the project CSS (page-specific font choice).
        var css = File.ReadAllText(ProjectCssPath);

        css.Should().MatchRegex(@"\.rec-bottombar-cap\s*\{[^}]*color:\s*var\(--text-muted\)");
    }

    // =====================================================================
    // Mocks
    // =====================================================================

    private class EditorFidelityMockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("api/Ventas/lista-maquinas"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Máquina 001" },
                    new { Id = 2, Nombre = "Máquina 002" }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("api/Ventas/lista-productos"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Coca Cola", PrecioVenta = 1200m },
                    new { Id = 2, Nombre = "Pepsi", PrecioVenta = 800m }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("api/TemplateRecarga/maquina/") && url.Contains("/slots"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        NumeroSlot = "A1",
                        ProductoId = (int?)null,
                        ProductoNombre = "",
                        CantidadInicial = 0,
                        CapacidadSlot = 5,
                        Estado = 0
                    },
                    new
                    {
                        NumeroSlot = "A2",
                        ProductoId = (int?)1,
                        ProductoNombre = "Coca Cola",
                        CantidadInicial = 3,
                        CapacidadSlot = 5,
                        Estado = 2
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (url.Contains("api/TemplateRecarga"))
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        Id = 1,
                        Nombre = "Template Activo",
                        Descripcion = "Template con slots pendientes",
                        FechaCreacion = DateTime.Now,
                        Estado = 0,
                        EsActivo = false,
                        Periodos = new[]
                        {
                            new
                            {
                                Id = 1,
                                TemplateRecargaId = 1,
                                MaquinaId = 1,
                                MaquinaNombre = "Máquina 001",
                                FechaRecarga = DateTime.Now,
                                FechaFin = DateTime.Now.AddDays(7),
                                TieneFotoGuia = false,
                                TieneFotoOcr = false,
                                SnapshotSlots = new object[]
                                {
                                    new
                                    {
                                        Id = 1,
                                        NumeroSlot = "A1",
                                        ProductoId = (int?)null,
                                        ProductoNombre = "",
                                        CantidadInicial = 0,
                                        CapacidadSlot = 5,
                                        Estado = 0
                                    },
                                    new
                                    {
                                        Id = 2,
                                        NumeroSlot = "A2",
                                        ProductoId = (int?)1,
                                        ProductoNombre = "Coca Cola",
                                        CantidadInicial = 3,
                                        CapacidadSlot = 5,
                                        Estado = 2
                                    }
                                }
                            }
                        }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private class AuthenticatedAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "test") },
                "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private class FakeAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private class EditorFidelityTestHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TemplatesRecarga>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
