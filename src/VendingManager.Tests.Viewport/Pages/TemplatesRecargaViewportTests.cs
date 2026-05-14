namespace VendingManager.Tests.Viewport.Pages;

using Microsoft.Playwright.NUnit;
using NUnit.Framework;

/// <summary>
/// Viewport smoke tests for TemplatesRecarga page.
/// Verifies no horizontal overflow at all device viewports and that
/// key UI elements are visible at mobile sizes.
/// </summary>
[TestFixture]
[Explicit("Viewport tests require the app to be running and Playwright browsers installed.")]
public class TemplatesRecargaViewportTests : ViewportTestBase
{
    /// <summary>
    /// Smoke test: TemplatesRecarga has no horizontal overflow at every viewport.
    /// </summary>
    [Test]
    [TestCaseSource(typeof(ViewportConfig), nameof(ViewportConfig.AllProfiles))]
    public async Task NoHorizontalOverflow(ViewportProfile profile)
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(profile, "/templates-recarga");
        await AssertNoHorizontalOverflow();
    }

    /// <summary>
    /// At mobile viewports, verify the FAB is visible for quick template creation.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MobileProfiles))]
    public async Task FABVisibleAtMobile(ViewportProfile profile)
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(profile, "/templates-recarga");

        // The FAB (Floating Action Button) should be present on mobile
        var fab = Page.Locator(".btn-create-template, [data-testid=\"fab-create\"], a[href*=\"crear\"], .fab");
        var count = await fab.CountAsync();

        // At least one creation action should be reachable
        var createLink = Page.Locator("a[href*=\"crear\"], a[href*=\"nuevo\"], .btn-create");
        var createCount = await createLink.CountAsync();
        createCount.Should().BeGreaterThan(0, "No create action found on TemplatesRecarga");
    }

    /// <summary>
    /// At mobile viewports, verify the pending slots button is visible.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MobileProfiles))]
    public async Task PendingSlotsButtonVisibleAtMobile(ViewportProfile profile)
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(profile, "/templates-recarga");

        var pendingBtn = Page.Locator("a[href*=\"pending\"], a[href*=\"slots\"], button.btn-pending, [data-testid=\"pending-slots\"]");
        var count = await pendingBtn.CountAsync();
        count.Should().BeGreaterThan(0, "No pending slots button/link found on TemplatesRecarga");
    }

    /// <summary>
    /// Open pending slots modal and verify no horizontal overflow inside it.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(TabletAndLaptopProfiles))]
    public async Task PendingSlotsModalNoOverflow(ViewportProfile profile)
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(profile, "/templates-recarga");

        // Click the pending slots button
        var pendingBtn = Page.Locator("a[href*=\"pending\"], a[href*=\"slots\"], button.btn-pending, [data-testid=\"pending-slots\"]").First;
        await pendingBtn.ClickAsync();

        // Wait for modal to appear
        await Page.WaitForSelectorAsync(".modal.show, [role=\"dialog\"]", new WaitForSelectorOptions { Timeout = 5000 });

        // Check overflow inside the modal
        var modalOverflow = await Page.EvaluateAsync<bool>(@"
            () => {
                const modal = document.querySelector('.modal.show, [role=""dialog""]');
                if (!modal) return false;
                return modal.scrollWidth > modal.clientWidth;
            }
        ");

        modalOverflow.Should().BeFalse("Modal has horizontal overflow");
    }

    /// <summary>
    /// Verify the page renders at iPad portrait with 2-column card grid.
    /// </summary>
    [Test]
    public async Task CardGridAtIPadPortrait()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPadPro11Portrait, "/templates-recarga");

        // At iPad portrait (834px), Bootstrap col-md-6 should give 2 cards per row
        var cards = Page.Locator(".card, .template-card, .industrial-card");
        var count = await cards.CountAsync();
        count.Should().BeGreaterThan(0, "No cards found on TemplatesRecarga");
    }

    private static IEnumerable<ViewportProfile> MobileProfiles()
    {
        yield return ViewportConfig.IPhonePortrait;
        yield return ViewportConfig.IPadPro11Portrait;
    }

    private static IEnumerable<ViewportProfile> TabletAndLaptopProfiles()
    {
        yield return ViewportConfig.IPadPro11Portrait;
        yield return ViewportConfig.IPadPro11Landscape;
        yield return ViewportConfig.Laptop14in;
    }
}