using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// Token discipline tests for <c>TemplatesRecarga.razor.css</c> — TASK-4.2 (Editor + Slot Cards).
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

    private const string SlotCardExceptionComment = "SlotCard is the design system EXCEPTION";

    [Fact]
    public void EditorAndSlotCards_NoHexLiterals_OutsideDocumentedException()
    {
        // The editor + slot card CSS must not contain raw hex literals, with
        // the ONE documented exception: .rec-slot uses #d1d5db (gray border
        // + 6px radius + soft shadow) as the slot card design system exception.
        var sectionSelectors = new[]
        {
            ".rec-editor", ".rec-topbar", ".rec-finalizado",
            ".rec-split", ".rec-rail", ".rec-rail-header", ".rec-mcard",
            ".rec-mcard-active", ".rec-mcard__select", ".rec-mcard-del",
            ".rec-mcard-config", ".rec-mcard-vacios",
            ".rec-estanteria", ".rec-est-header", ".rec-icon-tile",
            ".rec-search", ".rec-density", ".rec-density__btn",
            ".rec-floors", ".rec-piso-tag", ".rec-grid",
            ".rec-slot", ".rec-slot-empty", ".rec-slot-id",
            ".rec-slot-vacio-tag", ".rec-slot-price", ".rec-slot-pick",
            ".rec-slot-step", ".rec-slot-qty", ".rec-slot-max",
            ".rec-bottombar", ".rec-bottombar-totals",
            ".rec-bottombar-cap", ".rec-bottombar-vacios"
        };

        var block = ExtractBlocks(CssContent, sectionSelectors);
        block.Should().NotBeNullOrWhiteSpace("editor+slot card classes must exist in design v3");

        // Find all hex literals in the section.
        var hexMatches = Regex.Matches(block, @"#[0-9a-fA-F]{3,8}\b").ToList();

        // The ONLY allowed hex literal in this section is the slot card border #d1d5db
        // (documented design system exception).
        var nonExceptionHex = hexMatches
            .Where(m => !m.Value.Equals("#d1d5db", StringComparison.OrdinalIgnoreCase))
            .ToList();

        nonExceptionHex.Should().BeEmpty(
            "editor+slot section must not contain hex literals outside the documented slot card exception (#d1d5db). " +
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
        // The editor + slot card CSS must reference design tokens for borders + colors.
        var sectionSelectors = new[]
        {
            ".rec-topbar", ".rec-rail", ".rec-mcard",
            ".rec-mcard-del", ".rec-mcard-vacios", ".rec-slot"
        };

        var block = ExtractBlocks(CssContent, sectionSelectors);

        // Editor + slot card must use tokens like --signal-danger, --signal-warning, --signal-success.
        block.Should().Contain("var(--signal-danger)", ".rec-mcard-del uses --signal-danger");
        block.Should().Contain("var(--signal-warning)", "vacíos badge uses --signal-warning");
        block.Should().Contain("var(--signal-success)", "active state uses --signal-success");
    }
}
