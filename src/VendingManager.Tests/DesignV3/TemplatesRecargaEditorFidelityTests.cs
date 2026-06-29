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

    private void OpenEditor(IRenderedComponent<EditorFidelityTestHost> cut)
    {
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Template Activo"));
        var abrir = cut.FindComponents<VmButton>().First(b => b.Markup.Contains("Abrir"));
        abrir.Find("button").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("rec-topbar"));
    }

    // =====================================================================
    // TASK-3b.1: Editor top bar
    // =====================================================================

    [Fact]
    public void Editor_Topbar_HasThreePxBlackBottomBorder()
    {
        // Recarga.dc.html line 128: border-bottom:3px solid var(--ink-900)
        // Implementation uses --border-3 token which resolves to 3px solid var(--ink-900).
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        css.Should().Contain(".rec-topbar");
        css.Should().MatchRegex(@"\.rec-topbar\s*\{[^}]*border-bottom:\s*(?:3px\s+solid\s+var\(--ink-900\)|var\(--border-3\))");
    }

    [Fact]
    public void Editor_Topbar_HasVolverTitleStatusAndActions()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        var topbar = cut.Find(".rec-topbar");

        // Left side: Volver button (outline)
        topbar.InnerHtml.Should().Contain("Volver");
        topbar.QuerySelector(".rec-topbar .btn-outline-dark, .rec-topbar button[aria-label*='Volver'], .rec-topbar > div:first-child button")
            .Should().NotBeNull("Volver button must be present in topbar");

        // Center: title + status badge + description
        topbar.InnerHtml.Should().Contain("rec-tag");
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

        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        // Rail: flex: 0 0 326px; width: 326px; border-right: 2px solid var(--ink-900)
        css.Should().MatchRegex(@"\.rec-rail\s*\{[^}]*flex:\s*0\s+0\s+326px");
        css.Should().MatchRegex(@"\.rec-rail\s*\{[^}]*width:\s*326px");
        css.Should().MatchRegex(@"\.rec-rail\s*\{[^}]*border-right:\s*2px\s+solid\s+var\(--ink-900\)");

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

        // 32x32 trash button (red border)
        var trashButton = card.QuerySelector("button[aria-label*='Quitar'], button[aria-label*='Maquina']");
        trashButton.Should().NotBeNull("each rail card must have a trash button");
        trashButton!.ClassList.Should().Contain("rec-mcard-del");

        // Date input
        card.QuerySelector("input[type='date']").Should().NotBeNull("rail card must have a date input");

        // Time input (per design: type='time')
        card.QuerySelector("input[type='time']").Should().NotBeNull("rail card must have a time input (type='time')");

        // Bar + units/cap
        card.InnerHtml.Should().Contain("rec-bar");
        card.InnerHtml.Should().Contain("/");

        // Config label
        card.InnerHtml.Should().Contain("configurados");
    }

    [Fact]
    public void Editor_RailCard_TrashButton_HasRedBorder()
    {
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        css.Should().MatchRegex(@"\.rec-mcard-del\s*\{[^}]*width:\s*32px");
        css.Should().MatchRegex(@"\.rec-mcard-del\s*\{[^}]*height:\s*32px");
        css.Should().MatchRegex(@"\.rec-mcard-del\s*\{[^}]*border:\s*2px\s+solid\s+var\(--signal-danger\)");
    }

    [Fact]
    public void Editor_RailCard_HoverAndActiveStates()
    {
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        // Hover: 3px 3px 0 shadow
        css.Should().MatchRegex(@"\.rec-mcard:hover\s*\{[^}]*box-shadow:\s*3px\s+3px\s+0");

        // Active: 5px 0 0 inset green
        css.Should().MatchRegex(@"\.rec-mcard-active\s*\{[^}]*box-shadow:\s*inset\s+5px\s+0\s+0\s+var\(--signal-success\)");
    }

    // =====================================================================
    // TASK-3b.4: Estanteria header
    // =====================================================================

    [Fact]
    public void Editor_EstanteriaHeader_HasGridIconTileAndTitle()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        var header = cut.Find(".rec-est-header");

        // Black 34x34 grid icon tile
        var iconTile = header.QuerySelector(".rec-icon-tile");
        iconTile.Should().NotBeNull("estanteria header must have a grid icon tile");
        iconTile!.ClassList.Should().Contain("rec-icon-tile");

        // Title: "Estanteria · Maquina {id}" (uppercase via CSS)
        header.InnerHtml.Should().Contain("Estanteria");
        header.InnerHtml.Should().Contain("Maquina");

        // Subtitle: "{pct}% LLENO · {N} SLOTS"
        header.InnerHtml.Should().Contain("LLENO");
        header.InnerHtml.Should().Contain("SLOTS");
    }

    [Fact]
    public void Editor_EstanteriaHeader_IconTileCss_Black34x34()
    {
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        css.Should().MatchRegex(@"\.rec-icon-tile\s*\{[^}]*width:\s*34px");
        css.Should().MatchRegex(@"\.rec-icon-tile\s*\{[^}]*height:\s*34px");
        css.Should().MatchRegex(@"\.rec-icon-tile\s*\{[^}]*background:\s*var\(--ink-900\)");
        css.Should().MatchRegex(@"\.rec-icon-tile\s*\{[^}]*color:\s*var\(--paper-0\)");
    }

    // =====================================================================
    // TASK-3b.5: Estanteria toolbar
    // =====================================================================

    [Fact]
    public void Editor_Toolbar_HasSearchDensityAndPhotoButtons()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        // Search input
        var search = cut.Find("input.rec-search");
        search.Should().NotBeNull("toolbar must have a search input");
        search.GetAttribute("placeholder").Should().Contain("Buscar");

        // Density toggle
        var density = cut.Find(".rec-density");
        density.Should().NotBeNull();
        density.InnerHtml.Should().Contain("Comoda");
        density.InnerHtml.Should().Contain("Compacta");

        // Photo buttons
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Foto recarga"));
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Foto guia"));
    }

    [Fact]
    public void Editor_Toolbar_DensityToggle_HasActiveClass()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        var densityButtons = cut.FindAll(".rec-density__btn");
        densityButtons.Count.Should().Be(2);

        // Exactly one of them should be active initially (Comoda)
        var activeCount = densityButtons.Count(b => b.ClassList.Contains("active"));
        activeCount.Should().Be(1, "exactly one density button must be active by default");
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
        var pisoTag = cut.Find(".rec-piso-tag");
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
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        // grid-template-columns: repeat(auto-fill, minmax(246px, 1fr))
        css.Should().MatchRegex(@"\.rec-grid\s*\{[^}]*grid-template-columns:\s*repeat\(auto-fill,\s*minmax\(246px,\s*1fr\)\)");
        css.Should().MatchRegex(@"\.rec-grid\s*\{[^}]*gap:\s*12px");
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

        // Product picker button
        var picker = slot.QuerySelector(".rec-slot-pick");
        picker.Should().NotBeNull("slot card must have a product picker button");

        // Bar + qty/cap
        slot.InnerHtml.Should().Contain("rec-bar");
        slot.InnerHtml.Should().Contain("/");

        // Stepper controls
        slot.InnerHtml.Should().Contain("−");
        slot.InnerHtml.Should().Contain("+");
        slot.InnerHtml.Should().Contain("MAX");
    }

    [Fact]
    public void Editor_SlotCard_BorderException_OnePxGrayWithSoftShadow()
    {
        // The slot card is the design system EXCEPTION: 1px solid #d1d5db + radius 6px + soft shadow
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        css.Should().MatchRegex(@"\.rec-slot\s*\{[^}]*border:\s*1px\s+solid\s+#d1d5db");
        css.Should().MatchRegex(@"\.rec-slot\s*\{[^}]*border-radius:\s*6px");
        css.Should().MatchRegex(@"\.rec-slot\s*\{[^}]*box-shadow:\s*var\(--shadow-soft\)");
    }

    [Fact]
    public void Editor_SlotCard_StepperButtons_44x42WithTwoPxBorder()
    {
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        // − and + buttons: 44x42, 2px black border
        css.Should().MatchRegex(@"\.rec-slot-step\s*\{[^}]*width:\s*44px");
        css.Should().MatchRegex(@"\.rec-slot-step\s*\{[^}]*height:\s*42px");
        css.Should().MatchRegex(@"\.rec-slot-step\s*\{[^}]*border:\s*2px\s+solid\s+var\(--ink-900\)");

        // MÁX button: 42px height, 2px black border, black bg
        css.Should().MatchRegex(@"\.rec-slot-max\s*\{[^}]*height:\s*42px");
        css.Should().MatchRegex(@"\.rec-slot-max\s*\{[^}]*border:\s*2px\s+solid\s+var\(--ink-900\)");
        css.Should().MatchRegex(@"\.rec-slot-max\s*\{[^}]*background:\s*var\(--ink-900\)");
        css.Should().MatchRegex(@"\.rec-slot-max\s*\{[^}]*color:\s*var\(--paper-0\)");
    }

    // =====================================================================
    // TASK-3b.8: Bottom bar
    // =====================================================================

    [Fact]
    public void Editor_BottomBar_HasCargaTotalsVaciosAndActions()
    {
        var cut = RenderComponent<EditorFidelityTestHost>();
        OpenEditor(cut);

        var bottom = cut.Find(".rec-bottombar");

        // "Carga maquina" small mono uppercase
        bottom.InnerHtml.Should().Contain("Carga maquina");

        // "{units} / {cap} u." large sans
        bottom.InnerHtml.Should().MatchRegex(@"\d+\s*/\s*\d+\s*u\.");

        // Buttons
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Vaciar maquina"));
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Reset"));
        cut.FindComponents<VmButton>().Should().Contain(b => b.Markup.Contains("Guardar carga"));
    }

    [Fact]
    public void Editor_BottomBar_HasTwoPxBlackTopBorder()
    {
        var cssPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages", "TemplatesRecarga.razor.css");
        var css = File.ReadAllText(Path.GetFullPath(cssPath));

        css.Should().MatchRegex(@"\.rec-bottombar\s*\{[^}]*border-top:\s*2px\s+solid\s+var\(--ink-900\)");
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
                        Estado = 1
                    },
                    new
                    {
                        NumeroSlot = "A2",
                        ProductoId = (int?)1,
                        ProductoNombre = "Coca Cola",
                        CantidadInicial = 3,
                        CapacidadSlot = 5,
                        Estado = 0
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
                                        Estado = 1
                                    },
                                    new
                                    {
                                        Id = 2,
                                        NumeroSlot = "A2",
                                        ProductoId = (int?)1,
                                        ProductoNombre = "Coca Cola",
                                        CantidadInicial = 3,
                                        CapacidadSlot = 5,
                                        Estado = 0
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
