using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// Token discipline tests for <c>TemplatesRecarga.razor.css</c> — TASK-4.3 (Modals + Toast).
///
/// The .rec-toast class (transient toast) must use design tokens instead of
/// raw hex literals. The toast text color was hardcoded as #fff in PR3a and
/// must be replaced with the --paper-0 token (which resolves to #ffffff).
/// </summary>
public class TemplatesRecargaTokenAuditTests_Toast
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

    [Fact]
    public void ModalsAndToast_NoHexLiterals_AllReferencesAreTokens()
    {
        // The .rec-toast (and any design v3 modal markup) must not contain
        // raw hex literals — design tokens (var(--*)) must be used.
        var sectionSelectors = new[]
        {
            ".rec-toast"
        };

        var block = ExtractBlocks(CssContent, sectionSelectors);
        block.Should().NotBeNullOrWhiteSpace(".rec-toast must exist in design v3");

        var hexMatches = Regex.Matches(block, @"#[0-9a-fA-F]{3,8}\b");

        hexMatches.Should().BeEmpty(
            ".rec-toast must not contain raw hex literals — use design tokens (var(--*)). " +
            "Found: " + string.Join(", ", hexMatches.Select(m => m.Value).Distinct()));
    }

    [Fact]
    public void ModalsAndToast_ToastUsesPaperZeroToken()
    {
        // The toast text color must use the --paper-0 token (which is #ffffff).
        var block = ExtractBlocks(CssContent, new[] { ".rec-toast" });

        block.Should().Contain("var(--paper-0)",
            ".rec-toast text must use --paper-0 token (--paper-0: #ffffff)");
    }
}
