using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// Token discipline tests for <c>TemplatesRecarga.razor.css</c> (page-specific
/// rules) and <c>vm-recarga.css</c> (canonical design-system classes) —
/// TASK-4.2 (Editor + Slot Cards).
///
/// The editor + slot card section of the design v3 scoped CSS must use
/// design tokens (var(--*)) with the ONE documented exception: the slot
/// card uses 1px solid #d1d5db as a deliberate design system exception
/// (see design system handoff: "SlotCard es la excepción al sistema brutalista").
/// </summary>
public class TemplatesRecargaTokenAuditTests_Editor
{
    private static string WebPagesFolder => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages"));

    private static string CssPath => Path.Combine(WebPagesFolder, "TemplatesRecarga.razor.css");

    private static string CssContent => File.ReadAllText(CssPath);

    /// <summary>
    /// Path to the canonical design-system CSS file that owns the editor
    /// scaffold, rail, shelf, slot grid, segmented/stepper/iconbtn/status
    /// controls, and table. Canonical class definitions live here.
    /// </summary>
    private static string CanonicalCssPath => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "wwwroot", "css", "vm-recarga.css"));

    private static string CanonicalCssContent => File.ReadAllText(CanonicalCssPath);

    private static string FotoGuiaPanelCssPath => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Components", "FotoGuiaPanel.razor.css"));

    private static string FotoGuiaPanelCssContent => File.ReadAllText(FotoGuiaPanelCssPath);

    /// <summary>
    /// Extracts the content of an @media block whose condition contains the given text.
    /// </summary>
    private static string ExtractMediaBlock(string css, string conditionSubstring)
    {
        var idx = 0;
        while (idx < css.Length)
        {
            var atMedia = css.IndexOf("@media", idx, StringComparison.Ordinal);
            if (atMedia < 0) break;

            var openParen = css.IndexOf('(', atMedia);
            if (openParen < 0) { idx = atMedia + 6; continue; }

            var blockStart = css.IndexOf('{', openParen);
            if (blockStart < 0) break;

            // Extract the condition text between @media and the opening {
            var conditionEnd = css.LastIndexOf(')', blockStart);
            var condition = conditionEnd >= 0
                ? css.Substring(atMedia + 6, conditionEnd - atMedia - 5).Trim()
                : "";

            if (condition.Contains(conditionSubstring, StringComparison.Ordinal))
            {
                var depth = 1;
                var pos = blockStart + 1;
                while (pos < css.Length && depth > 0)
                {
                    var ch = css[pos];
                    if (ch == '{') depth++;
                    else if (ch == '}') depth--;
                    pos++;
                }
                return css.Substring(blockStart + 1, pos - blockStart - 2);
            }

            idx = blockStart + 1;
        }
        return "";
    }

    private static string ExtractBlocks(string css, string[] selectors)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var selector in selectors)
        {
            var idx = 0;
            while (idx < css.Length)
            {
                var found = css.IndexOf(selector, idx, StringComparison.Ordinal);
                if (found < 0) break;

                var braceStart = css.IndexOf('{', found);
                if (braceStart < 0) break;

                var depth = 1;
                var pos = braceStart + 1;
                while (pos < css.Length && depth > 0)
                {
                    var ch = css[pos];
                    if (ch == '{') depth++;
                    else if (ch == '}') depth--;
                    pos++;
                }

                sb.Append(css.Substring(found, pos - found));
                idx = pos;
            }
        }
        return sb.ToString();
    }

    private const string SlotCardExceptionComment = "EXCEPTION";

    [Fact]
    public void EditorAndSlotCards_NoHexLiterals_OutsideDocumentedException()
    {
        // The page-specific rules kept in TemplatesRecarga.razor.css must not
        // contain raw hex literals, with the ONE documented exception:
        // .rec-slot uses #d1d5db (gray border + 6px radius + soft shadow)
        // as the slot card design system exception. The canonical classes
        // (editor, rail, shelf, stepper, segmented, status chip, etc.) live
        // in vm-recarga.css and are covered by a separate audit.
        var sectionSelectors = new[]
        {
            // page-specific rules still in the project CSS
            ".rec-mcard__select", ".rec-mcard-config",
            ".rec-slot", ".rec-slot-empty", ".rec-slot-id",
            ".rec-slot-vacio-tag", ".rec-slot-price", ".rec-slot-pick",
            ".rec-bottombar-totals", ".rec-bottombar-cap", ".rec-bottombar-vacios"
        };

        var block = ExtractBlocks(CssContent, sectionSelectors);
        block.Should().NotBeNullOrWhiteSpace(
            "page-specific editor+slot card classes must exist in the project CSS");

        // Find all hex literals in the section.
        var hexMatches = Regex.Matches(block, @"#[0-9a-fA-F]{3,8}\b").ToList();

        // The ONLY allowed hex literal in this section is the slot card border #d1d5db
        // (documented design system exception).
        var nonExceptionHex = hexMatches
            .Where(m => !m.Value.Equals("#d1d5db", StringComparison.OrdinalIgnoreCase))
            .ToList();

        nonExceptionHex.Should().BeEmpty(
            "page-specific editor+slot section must not contain hex literals outside the documented slot card exception (#d1d5db). " +
            "Found: " + string.Join(", ", nonExceptionHex.Select(m => m.Value).Distinct()));

        // Verify the slot card exception is present and documented.
        block.Should().Contain("#d1d5db", "the slot card border exception must be present");
        block.Should().Contain(SlotCardExceptionComment,
            "the slot card border must be documented as a design system exception");
    }

    [Fact]
    public void EditorAndSlotCards_SlotCardBorder_IsDocumentedException()
    {
        // The .rec-slot class is the documented design system exception:
        // 1px solid #d1d5db + border-radius 6px + var(--shadow-soft).
        var block = ExtractBlocks(CssContent, new[] { ".rec-slot" });

        block.Should().Contain("1px solid #d1d5db", "slot card uses 1px solid #d1d5db (gray, not brutalist black)");
        block.Should().Contain("border-radius: 6px", "slot card uses 6px radius (softer than system default 0)");
        block.Should().Contain("var(--shadow-soft)", "slot card uses --shadow-soft (gentler than the hard-offset system shadow)");
    }

    [Fact]
    public void EditorAndSlotCards_UsesDesignTokens()
    {
        // The page-specific rules kept in TemplatesRecarga.razor.css use
        // design tokens, and the canonical editor/slot classes in
        // vm-recarga.css must also use tokens (--signal-danger,
        // --signal-warning, --signal-success).
        var pageSpecificSelectors = new[]
        {
            ".rec-mcard__select", ".rec-mcard-config",
            ".rec-slot", ".rec-slot-vacio-tag",
            ".rec-bottombar-totals", ".rec-bottombar-cap", ".rec-bottombar-vacios"
        };
        var pageBlock = ExtractBlocks(CssContent, pageSpecificSelectors);

        // Canonical selectors for the danger/active/success signals
        var canonicalSelectors = new[]
        {
            ".rec-iconbtn--danger", ".rec-mcard.is-active",
            ".rec-tag-empty", ".rec-status", ".rec-badge--pending"
        };
        var canonicalBlock = ExtractBlocks(CanonicalCssContent, canonicalSelectors);

        // Page-specific rules must use tokens (--ink-900, --text-muted, etc.)
        pageBlock.Should().Contain("var(--ink-900)",
            ".rec-mcard__select / .rec-mcard-config / slot card rules use --ink-900 token");
        pageBlock.Should().Contain("var(--text-muted)",
            ".rec-mcard-config / .rec-bottombar-cap use --text-muted token");
        pageBlock.Should().Contain("var(--signal-warning)",
            ".rec-bottombar-vacios uses --signal-warning token");

        // Canonical CSS must use the corresponding signal tokens
        canonicalBlock.Should().Contain("var(--signal-danger)",
            ".rec-iconbtn--danger uses --signal-danger");
        canonicalBlock.Should().Contain("var(--signal-success)",
            ".rec-mcard.is-active / .rec-status use --signal-success");
        canonicalBlock.Should().Contain("var(--signal-warning)",
            ".rec-tag-empty / .rec-badge--pending use --signal-warning");
    }

    /* ─────────────────────────────────────────────────────────────────────
     * Slice E — Visual Polish audit (R5.1a, R5.2a, R5.3a, R5.4a-b, R3.6a, R4.3b)
     * ───────────────────────────────────────────────────────────────────── */

    [Fact]
    public void Polish_ShelfBodyBackground_UsesShelfInsetToken()
    {
        // R5.2a — Shelf grid background must use --bg-shelf-inset token, defined as #e6e7ea
        var block = ExtractBlocks(CanonicalCssContent, new[] { ".rec-shelf__body" });

        block.Should().NotBeNullOrWhiteSpace(".rec-shelf__body must be defined in canonical CSS");
        block.Should().Contain("--bg-shelf-inset",
            ".rec-shelf__body background should use the --bg-shelf-inset token");

        // Verify the token is defined in :root with matching value
        CanonicalCssContent.Should().Contain("--bg-shelf-inset:",
            "--bg-shelf-inset must be defined as a CSS custom property in :root");
        CanonicalCssContent.Should().Contain("#e6e7ea",
            "--bg-shelf-inset must resolve to #e6e7ea");
    }

    [Fact]
    public void Polish_ResponsiveQuery_CollapsesRecGuiaToFullWidth()
    {
        // R5.4a — At ≤1000px the .rec-guia must be full width (not fixed 384px)
        var mediaBody = ExtractMediaBlock(CanonicalCssContent, "max-width: 1000px");

        mediaBody.Should().NotBeNullOrWhiteSpace(
            "@media (max-width: 1000px) block must exist in canonical CSS");
        mediaBody.Should().Contain(".rec-guia",
            "responsive block must target .rec-guia");
        mediaBody.Should().Contain("width: 100%",
            ".rec-guia must be full-width at narrow viewport");
    }

    [Fact]
    public void Polish_Headers_UseRecBandHForMinHeight()
    {
        // R5.3a — All three split headers must share --rec-band-h for min-height alignment.
        // Note: the exact tokens may be "var(--rec-band-h)" (vm-recarga.css) or
        // "var(--rec-band-h, 62px)" (scoped CSS with fallback), so match on --rec-band-h.
        var railHeadBlock = ExtractBlocks(CanonicalCssContent, new[] { ".rec-rail__head" });
        var shelfHeadBlock = ExtractBlocks(CanonicalCssContent, new[] { ".rec-shelf__head" });

        railHeadBlock.Should().NotBeNullOrWhiteSpace(".rec-rail__head must be defined");
        railHeadBlock.Should().Contain("--rec-band-h",
            ".rec-rail__head must use --rec-band-h token");

        shelfHeadBlock.Should().NotBeNullOrWhiteSpace(".rec-shelf__head must be defined");
        shelfHeadBlock.Should().Contain("--rec-band-h",
            ".rec-shelf__head must use --rec-band-h token");

        // rec-guia-header is in FotoGuiaPanel.razor.css (scoped)
        var guiaHeaderBlock = ExtractBlocks(FotoGuiaPanelCssContent, new[] { ".rec-guia-header" });
        guiaHeaderBlock.Should().NotBeNullOrWhiteSpace(".rec-guia-header must be defined");
        guiaHeaderBlock.Should().Contain("--rec-band-h",
            ".rec-guia-header must use --rec-band-h token");
    }

    [Fact]
    public void Polish_ReducedMotion_DisablesScanLineAndCursor()
    {
        // R3.6a, R4.3b — prefers-reduced-motion disables scan-line and cursor animations
        var mediaBody = ExtractMediaBlock(CanonicalCssContent, "prefers-reduced-motion: reduce");

        mediaBody.Should().NotBeNullOrWhiteSpace(
            "@media (prefers-reduced-motion: reduce) block must exist in canonical CSS");
        mediaBody.Should().Contain(".rec-scan-line",
            "reduced-motion must disable .rec-scan-line animation");
        mediaBody.Should().Contain(".rec-scan-cursor",
            "reduced-motion must disable .rec-scan-cursor animation");
    }

    [Fact]
    public void Polish_ZoomLabel_NoOnePointZeroXFormat()
    {
        // R5.1a — Zoom label must not use "1.0x" format anywhere in the codebase
        // Check both canonical CSS and FotoGuiaPanel scoped CSS
        CanonicalCssContent.Should().NotContain("1.0x",
            "zoom label format must not be '1.0x' in canonical CSS");

        var panelPath = FotoGuiaPanelCssPath;
        var panelCss = File.ReadAllText(panelPath);
        panelCss.Should().NotContain("1.0x",
            "zoom label format must not be '1.0x' in FotoGuiaPanel CSS");
    }
}
