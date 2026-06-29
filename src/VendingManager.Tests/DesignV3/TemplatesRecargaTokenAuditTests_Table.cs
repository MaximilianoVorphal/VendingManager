using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// Token discipline tests for <c>TemplatesRecarga.razor.css</c> — TASK-4.1 (Header + Table).
///
/// The header + table section of the design v3 scoped CSS must use design
/// tokens (var(--*)) instead of raw hex color literals.
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
        // The header + table CSS in the design v3 section must not contain
        // raw hex color literals — design tokens (var(--*)) must be used.
        var sectionSelectors = new[]
        {
            ".rec-page", ".rec-header", ".rec-breadcrumb", ".rec-title",
            ".rec-cursor", ".rec-subtitle", ".rec-pbtn",
            ".rec-table-wrap", ".rec-table", ".rec-trow",
            ".rec-name-btn", ".rec-cell-mono", ".rec-chips",
            ".rec-actions", ".rec-no-rows", ".rec-bar",
            ".rec-bar__fill", ".rec-bar--good", ".rec-bar--warning", ".rec-bar--danger",
            ".rec-carga", ".rec-carga-label", ".rec-tag", ".rec-tag--ok", ".rec-tag--pend"
        };

        var block = ExtractBlocks(CssContent, sectionSelectors);
        block.Should().NotBeNullOrWhiteSpace("header+table classes must exist in design v3");

        var hexMatches = Regex.Matches(block, @"#[0-9a-fA-F]{3,8}\b");

        hexMatches.Should().BeEmpty(
            "header+table section must not contain raw hex color literals — use design tokens (var(--*)) instead. " +
            "Found: " + string.Join(", ", hexMatches.Select(m => m.Value).Distinct()));
    }

    [Fact]
    public void HeaderAndTable_UsesDesignTokens()
    {
        // The header + table CSS must reference design tokens, not raw colors.
        var sectionSelectors = new[]
        {
            ".rec-header", ".rec-breadcrumb", ".rec-table", ".rec-pbtn"
        };

        var block = ExtractBlocks(CssContent, sectionSelectors);
        block.Should().Contain("var(--ink-900)", ".rec-header / .rec-breadcrumb / .rec-table / .rec-pbtn must use --ink-900 token");
        block.Should().Contain("var(--paper-0)", "header+table must use --paper-0 token");
    }
}
