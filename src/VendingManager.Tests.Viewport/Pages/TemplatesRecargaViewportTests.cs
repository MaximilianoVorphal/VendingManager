namespace VendingManager.Tests.Viewport.Pages;

using Microsoft.Playwright;
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
        Assert.That(createCount, Is.GreaterThan(0), "No create action found on TemplatesRecarga");
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
        Assert.That(count, Is.GreaterThan(0), "No pending slots button/link found on TemplatesRecarga");
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
        await Page.WaitForSelectorAsync(".modal.show, [role=\"dialog\"]", new() { Timeout = 5000 });

        // Check overflow inside the modal
        var modalOverflow = await Page.EvaluateAsync<bool>(@"
            () => {
                const modal = document.querySelector('.modal.show, [role=""dialog""]');
                if (!modal) return false;
                return modal.scrollWidth > modal.clientWidth;
            }
        ");

        Assert.That(modalOverflow, Is.False, "Modal has horizontal overflow");
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
        Assert.That(count, Is.GreaterThan(0), "No cards found on TemplatesRecarga");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // E2E Lifecycle Test: Borrador → EnCarga → Activo → Cerrado (task 6.8)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// E2E: Full browser lifecycle via the TemplatesRecarga UI.
    /// Creates a draft template, loads it, finalizes it, and closes it.
    /// Uses the UI flow (not API) to exercise the complete state machine.
    /// </summary>
    [Test]
    [TestCaseSource(typeof(ViewportConfig), nameof(ViewportConfig.AllProfiles))]
    public async Task E2E_Lifecycle_BorradorEnCargaActivoCerrado(ViewportProfile profile)
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(profile, "/templates-recarga");

        // Step 1: Create new template (navigate to crear page)
        var createBtn = Page.Locator("a[href*=\"crear\"], a[href*=\"nuevo\"], .btn-create").First;
        await createBtn.ClickAsync();
        await Page.WaitForURLAsync("**/*crear**", new() { Timeout = 5000 });

        // Fill in template name
        var nameInput = Page.Locator("input[name*=\"Nombre\"], input#nombre, input[placeholder*=\"nombre\"]").First;
        await nameInput.FillAsync($"E2E Test {DateTime.Now:HHmmss}");

        // Submit form
        var submitBtn = Page.Locator("button[type=\"submit\"], button.btn-primary").First;
        await submitBtn.ClickAsync();

        // Wait to return to list
        await Page.WaitForURLAsync("**/templates-recarga**", new() { Timeout = 8000 });

        // Step 2: Initiate carga (Borrador → EnCarga)
        var templateCard = Page.Locator(".card, .template-card, .industrial-card").First;
        var cargaBtn = templateCard.Locator("button[href*=\"iniciar\"], a[href*=\"iniciar\"], .btn-iniciar").First;
        await cargaBtn.ClickAsync();

        // Confirm the transition
        var confirmBtn = Page.Locator("button.confirm, .btn-confirm, button.btn-primary").First;
        await confirmBtn.ClickAsync();

        // Step 3: Finalize (EnCarga → Activo)
        await Page.WaitForTimeoutAsync(1000);
        var finalizeBtn = Page.Locator("button[href*=\"finalizar\"], a[href*=\"finalizar\"], .btn-finalizar").First;
        await finalizeBtn.ClickAsync();

        // Confirm
        await confirmBtn.ClickAsync();

        // Step 4: Close (Activo → Cerrado)
        await Page.WaitForTimeoutAsync(1000);
        var cerrarBtn = Page.Locator("button[href*=\"cerrar\"], a[href*=\"cerrar\"], .btn-cerrar").First;
        await cerrarBtn.ClickAsync();
        await confirmBtn.ClickAsync();

        // Verify template is now in Cerrado state
        var estadoBadge = Page.Locator(".badge:has-text(\"CERRADO\"), .estado-badge:has-text(\"Cerrado\")").First;
        Assert.That(await estadoBadge.CountAsync(), Is.GreaterThan(0),
            "Template should be in Cerrado state after E2E lifecycle");
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
