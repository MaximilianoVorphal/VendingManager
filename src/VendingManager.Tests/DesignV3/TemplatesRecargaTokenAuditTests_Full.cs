using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// Comprehensive token discipline audit for <c>TemplatesRecarga.razor.css</c> — TASK-4.5.
///
/// REQ-FID-7 (Design Token Discipline): the entire scoped CSS file must use
/// design tokens (var(--*)) instead of raw hex color literals. The ONLY
/// documented exception is the slot card border (#d1d5db), which is the
/// design system's explicit "SlotCard is the exception to the brutalist system"
/// (see design system handoff README).
/// </summary>
public class TemplatesRecargaTokenAuditTests_Full
{
    private static string WebPagesFolder => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages"));

    private static string CssPath => Path.Combine(WebPagesFolder, "TemplatesRecarga.razor.css");

    private static string CssContent => File.ReadAllText(CssPath);

    private const string SlotCardExceptionComment = "SlotCard is the design system EXCEPTION";

    [Fact]
    public void FullFile_NoHexLiterals_OutsideDocumentedException()
    {
        // The complete scoped CSS file must not contain raw hex color literals
        // outside the documented slot card exception (#d1d5db).
        var hexMatches = Regex.Matches(CssContent, @"#[0-9a-fA-F]{3,8}\b")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        var nonExceptionHex = hexMatches
            .Where(h => !h.Equals("#d1d5db", StringComparison.OrdinalIgnoreCase))
            .ToList();

        nonExceptionHex.Should().BeEmpty(
            "the entire TemplatesRecarga.razor.css must not contain raw hex literals outside the documented slot card exception. " +
            "Found: " + string.Join(", ", nonExceptionHex));
    }

    [Fact]
    public void SlotCardException_IsDocumentedInFile()
    {
        // The slot card exception must be documented in the CSS file itself
        // (not just in the test), so future maintainers see it.
        CssContent.Should().Contain(SlotCardExceptionComment,
            "the slot card design system exception must be documented in the CSS");
    }
}
