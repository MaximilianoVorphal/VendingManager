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
/// TASK-4.1 (Header + Table).
///
/// The header + table section of the design v3 scoped CSS must use design
/// tokens (var(--*)) instead of raw hex color literals. The canonical
/// classes (.rec-list, .rec-list__head, .rec-eyebrow, .rec-pill, .rec-table,
/// .rec-trow, .rec-progress, .rec-empty, .rec-bar, etc.) live in
/// vm-recarga.css.
/// </summary>
public class TemplatesRecargaTokenAuditTests_Table
{
    private static string WebPagesFolder => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages"));

    private static string CssPath => Path.Combine(WebPagesFolder, "TemplatesRecarga.razor.css");

    private static string CssContent => File.ReadAllText(CssPath);

    /// <summary>
    /// Canonical design-system CSS path — owner of the list view header
    /// and table classes.
    /// </summary>
    private static string CanonicalCssPath => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "wwwroot", "css", "vm-recarga.css"));

    private static string CanonicalCssContent => File.ReadAllText(CanonicalCssPath);

    /// <summary>
    /// Extracts the CSS block(s) that start with any of the given selectors
    /// and end at the matching closing brace. Returns the concatenated text.
    /// </summary>
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

    [Fact]
    public void HeaderAndTable_NoHexLiterals_OutsideExceptions()
    {
        // The header + table CSS — across the project CSS and the canonical
        // CSS — must not contain raw hex color literals. Design tokens
        // (var(--*)) must be used.
        //
        // - The page-specific rules (kept in TemplatesRecarga.razor.css)
        //   cover the row name button, the mono cell helper, the chip strip,
        //   the action group, the loading bar wrapper, and the table tweaks.
        // - The canonical rules in vm-recarga.css own the list scaffold,
        //   the table, the row states, the progress bar, the empty state,
        //   and the pill / badge / tag.

        var projectSelectors = new[]
        {
            ".rec-name-btn", ".rec-cell-mono", ".rec-chips",
            ".rec-actions", ".rec-carga", ".rec-carga-label"
        };
        var projectBlock = ExtractBlocks(CssContent, projectSelectors);

        var canonicalSelectors = new[]
        {
            ".rec-list", ".rec-list__head", ".rec-eyebrow",
            ".rec-title", ".rec-title__cursor", ".rec-subtitle",
            ".rec-pill", ".rec-tablewrap", ".rec-table",
            ".rec-trow", ".rec-progress", ".rec-empty",
            ".rec-badge", ".rec-badge--ok", ".rec-badge--pending"
        };
        var canonicalBlock = ExtractBlocks(CanonicalCssContent, canonicalSelectors);

        projectBlock.Should().NotBeNullOrWhiteSpace("page-specific table classes must exist in the project CSS");
        canonicalBlock.Should().NotBeNullOrWhiteSpace("canonical table/header classes must exist in vm-recarga.css");

        var projectHex = Regex.Matches(projectBlock, @"#[0-9a-fA-F]{3,8}\b");
        var canonicalHex = Regex.Matches(canonicalBlock, @"#[0-9a-fA-F]{3,8}\b");

        projectHex.Should().BeEmpty(
            "page-specific header+table section must not contain raw hex color literals — use design tokens (var(--*)) instead. " +
            "Found: " + string.Join(", ", projectHex.Select(m => m.Value).Distinct()));

        canonicalHex.Should().BeEmpty(
            "canonical header+table section (vm-recarga.css) must not contain raw hex color literals — use design tokens (var(--*)) instead. " +
            "Found: " + string.Join(", ", canonicalHex.Select(m => m.Value).Distinct()));
    }

    [Fact]
    public void HeaderAndTable_UsesDesignTokens()
    {
        // The header + table CSS must reference design tokens, not raw colors.
        var canonicalSelectors = new[]
        {
            ".rec-eyebrow", ".rec-list__head", ".rec-table", ".rec-pill"
        };
        var block = ExtractBlocks(CanonicalCssContent, canonicalSelectors);

        block.Should().Contain("var(--ink-900)",
            ".rec-eyebrow / .rec-list__head / .rec-table / .rec-pill must use --ink-900 token");
        block.Should().Contain("var(--paper-0)",
            "header+table must use --paper-0 token");
    }
}
