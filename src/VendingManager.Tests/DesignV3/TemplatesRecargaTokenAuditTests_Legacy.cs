using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

/// <summary>
/// Token discipline tests for <c>TemplatesRecarga.razor.css</c> — TASK-4.4 (Legacy CSS deletion).
///
/// The pre-PR1 legacy CSS (lines 1-532) and the mid-file orphan CSS
/// (lines 1183-1314 from the deleted floating panel and modals) are dead
/// code. The token audit requires removing them. Visual rendering must not
/// change because these classes are no longer referenced in the design v3
/// markup (PR1+PR2+PR3a+PR3b replaced the markup).
/// </summary>
public class TemplatesRecargaTokenAuditTests_Legacy
{
    private static string WebPagesFolder => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "Pages"));

    private static string CssPath => Path.Combine(WebPagesFolder, "TemplatesRecarga.razor.css");

    private static string CssContent => File.ReadAllText(CssPath);

    [Fact]
    public void LegacyCssClasses_AreRemoved()
    {
        // The legacy CSS section (pre-design-v3) and the deleted floating
        // panel and orphan modals must be gone. These classes are dead code
        // from before PR1+PR2+PR3a+PR3b and the token audit.
        var content = CssContent;

        // Pre-design-v3 legacy class selectors (must be gone)
        content.Should().NotContain(".template-name {", "legacy .template-name removed");
        content.Should().NotContain(".period-list {", "legacy .period-list removed");
        content.Should().NotContain(".edit-slots-btn {", "legacy .edit-slots-btn removed");
        content.Should().NotContain(".machine-chassis {", "legacy .machine-chassis removed");
        content.Should().NotContain(".slot-grid {", "legacy .slot-grid removed");
        content.Should().NotContain(".inventory-grid {", "legacy .inventory-grid removed");
        content.Should().NotContain(".slot-unit {", "legacy .slot-unit removed");
        content.Should().NotContain(".product-select {", "legacy .product-select removed");
        content.Should().NotContain(".estado-vacio {", "legacy .estado-vacio removed");
        content.Should().NotContain(".estado-pendiente {", "legacy .estado-pendiente removed");
        content.Should().NotContain(".estado-lleno {", "legacy .estado-lleno removed");
        content.Should().NotContain(".template-card-warning {", "legacy .template-card-warning removed");

        // Orphan modal/panel CSS that PR1 should have cleared (regression guard)
        content.Should().NotContain(".product-selector-overlay {", "orphan modal .product-selector-overlay removed");
        content.Should().NotContain(".product-selector-dropdown {", "orphan modal .product-selector-dropdown removed");
        content.Should().NotContain(".product-selector-header {", "orphan modal .product-selector-header removed");
        content.Should().NotContain(".product-selector-search {", "orphan modal .product-selector-search removed");
        content.Should().NotContain(".product-selector-list {", "orphan modal .product-selector-list removed");
        content.Should().NotContain(".product-selector-item {", "orphan modal .product-selector-item removed");
        content.Should().NotContain(".slot-editor-wrapper {", "legacy .slot-editor-wrapper removed");
    }

    [Fact]
    public void FileSize_IsReducedByAtLeast500Lines()
    {
        // Pre-PR4 file was 1325 lines. After deleting legacy CSS, file should
        // be well under 800 lines (500+ line reduction).
        var lineCount = File.ReadAllLines(CssPath).Length;
        lineCount.Should().BeLessThan(800,
            $"PR4 must delete at least 500 lines of legacy CSS. Current: {lineCount} lines.");
    }
}
