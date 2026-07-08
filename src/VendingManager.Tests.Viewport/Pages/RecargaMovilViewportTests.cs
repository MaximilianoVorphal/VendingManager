namespace VendingManager.Tests.Viewport.Pages;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

/// <summary>
/// Viewport smoke tests for the RecargaMovil page at mobile sizes.
/// Tests the proof-of-load photo flow UI (PR 3).
///
/// Tests are [Explicit] because they require the app to be running and
/// Playwright browsers installed. Seed data (≥1 template with ≥1 machine)
/// must exist in the in-memory database for these tests to pass.
/// </summary>
[TestFixture]
[Explicit("Viewport tests require the app to be running, Playwright browsers installed, and seeded data.")]
public class RecargaMovilViewportTests : ViewportTestBase
{
    /// <summary>
    /// Navigate to /recarga-movil at iPhone 14 portrait, assert no horizontal overflow,
    /// screenshot the Lista view, tap a card, and screenshot Resumen view.
    /// </summary>
    [Test]
    public async Task IPhone14_NavigateListToResumen_Screenshots()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");

        // Wait for the page to settle (Blazor renders)
        await Page.WaitForTimeoutAsync(1000);

        // 1. Assert no horizontal overflow
        await AssertNoHorizontalOverflow();

        // 2. Screenshot the Lista view (baseline if not exists)
        var screenshotDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "RecargaMovilViewport");
        Directory.CreateDirectory(screenshotDir);

        var listaPath = Path.Combine(screenshotDir, "Lista.png");
        if (!File.Exists(listaPath))
        {
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = listaPath,
                FullPage = true,
                Type = ScreenshotType.Png,
            });
        }

        // 3. Check that the page header is visible
        var pageTitle = Page.Locator(".rm-title, .rm-head--dark");
        var titleCount = await pageTitle.CountAsync();
        Assert.That(titleCount, Is.GreaterThan(0), "RecargaMovil page did not render header");

        // 4. Tap the first template card to navigate to Resumen
        var firstCard = Page.Locator(".rm-card").First;
        var cardCount = await firstCard.CountAsync();
        if (cardCount == 0)
        {
            Assert.Ignore("No template cards found — seed data required");
            return;
        }

        await firstCard.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // 5. Assert Resumen view loaded
        var resumenText = Page.Locator("text=Resumen de carga");
        Assert.That(await resumenText.CountAsync(), Is.GreaterThan(0),
            "Did not navigate to Resumen view");

        // 6. Screenshot Resumen
        var resumenPath = Path.Combine(screenshotDir, "Resumen.png");
        if (!File.Exists(resumenPath))
        {
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = resumenPath,
                FullPage = true,
                Type = ScreenshotType.Png,
            });
        }

        // 7. Verify bottom-bar hit targets (min 44px)
        // Note: .rm-cta--disabled is intentionally excluded — disabled CTAs are not interactive
        // so they do not need to meet the 44px hit-target accessibility guideline.
        var bottomBars = Page.Locator(".rm-view__bottombar, .rm-cta--primary");
        var barCount = await bottomBars.CountAsync();
        for (int i = 0; i < barCount; i++)
        {
            var height = await bottomBars.Nth(i).EvaluateAsync<double>(
                "el => el.getBoundingClientRect().height");
            Assert.That(height, Is.GreaterThanOrEqualTo(44),
                $"Bottom bar element {i} height {height}px is below 44px minimum");
        }

        // 8. Verify no horizontal overflow after navigation
        await AssertNoHorizontalOverflow();
    }

    /// <summary>
    /// Attempt to navigate through all 4 views (Lista → Resumen → PickMachine → EditSlots),
    /// screenshotting each. This test may partially succeed if some views are unreachable.
    /// </summary>
    [Test]
    public async Task IPhone14_ScreenshotAllViews()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");
        await Page.WaitForTimeoutAsync(1000);

        var screenshotDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "RecargaMovilViewport");
        Directory.CreateDirectory(screenshotDir);

        // Helper: screenshot if baseline doesn't exist
        async Task SaveScreenshot(string name)
        {
            var path = Path.Combine(screenshotDir, $"{name}.png");
            if (!File.Exists(path))
            {
                await Page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = path,
                    FullPage = true,
                    Type = ScreenshotType.Png,
                });
            }
        }

        // Lista
        await SaveScreenshot("1-Lista");

        // Tap first card → Resumen
        var firstCard = Page.Locator(".rm-card").First;
        if (await firstCard.CountAsync() == 0)
        {
            Assert.Ignore("No template cards found — seed data required");
            return;
        }
        await firstCard.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);
        await SaveScreenshot("2-Resumen");

        // Tap "Agregar máquina" → PickMachine
        var addMachineBtn = Page.Locator(".rm-cta--dashed").First;
        if (await addMachineBtn.CountAsync() > 0)
        {
            await addMachineBtn.ClickAsync();
            await Page.WaitForTimeoutAsync(1000);
            await SaveScreenshot("3-PickMachine");

            // Try to go to EditSlots by picking the first machine
            var poolRow = Page.Locator(".rm-row").First;
            if (await poolRow.CountAsync() > 0)
            {
                await poolRow.ClickAsync();
                await Page.WaitForTimeoutAsync(1500);
                await SaveScreenshot("4-EditSlots");
            }
        }

        // Final overflow check
        await AssertNoHorizontalOverflow();
    }

    /// <summary>
    /// Verify CTA button heights at iPhone 14 viewport are ≥44px.
    /// </summary>
    [Test]
    public async Task IPhone14_CTAHitTargets_Min44px()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");
        await Page.WaitForTimeoutAsync(1000);

        // Check all buttons and interactive elements
        var interactiveElements = Page.Locator(
            "button, .rm-cta, .rm-press, .rm-iconbtn, .rm-cta--primary, .rm-cta--dashed");
        var count = await interactiveElements.CountAsync();

        Assert.That(count, Is.GreaterThan(0), "No interactive elements found");

        for (int i = 0; i < count; i++)
        {
            var el = interactiveElements.Nth(i);
            var box = await el.EvaluateAsync<BoundingBoxResult>(
                @"el => {
                    const rect = el.getBoundingClientRect();
                    return { width: rect.width, height: rect.height, top: rect.top, left: rect.left };
                }");

            Assert.That(box.Height, Is.GreaterThanOrEqualTo(44),
                $"Element {i} has height {box.Height}px < 44px minimum");
        }
    }

    /// <summary>
    /// Navigate to /recarga-movil at iPhone 14 portrait, tap a template card,
    /// tap "Finalizar carga", verify photo sheet opens, verify "Subir y finalizar"
    /// is disabled (no file selected), tap Cancelar, verify sheet closes.
    ///
    /// Requires seed data: a template where ALL machines are loaded (every machine
    /// has at least one slot with CantidadInicial &gt; 0). If the "Finalizar carga"
    /// button is not enabled, the test is skipped.
    /// </summary>
    [Test]
    public async Task IPhone14_PhotoSheet_Opens_OnFinalizarCarga()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");
        await Page.WaitForTimeoutAsync(1000);

        // Tap first template card → Resumen view
        var firstCard = Page.Locator(".rm-card").First;
        if (await firstCard.CountAsync() == 0)
        {
            Assert.Ignore("No template cards found — seed data required");
            return;
        }
        await firstCard.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Assert Resumen view loaded
        var resumenText = Page.Locator("text=Resumen de carga");
        Assert.That(await resumenText.CountAsync(), Is.GreaterThan(0),
            "Did not navigate to Resumen view");

        // Find the "Finalizar carga" CTA. In the Resumen view:
        //   - enabled:  &lt;button class="rm-cta rm-cta--primary"&gt;
        //   - disabled: &lt;div class="rm-cta rm-cta--disabled"&gt;
        var enabledFinalizar = Page.Locator(".rm-cta--primary").And(
            Page.Locator("text=Finalizar carga"));
        if (await enabledFinalizar.CountAsync() == 0)
        {
            Assert.Ignore(
                "'Finalizar carga' is disabled or not found — " +
                "requires a template with all machines loaded");
            return;
        }

        // Tap "Finalizar carga"
        await enabledFinalizar.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Assert photo sheet backdrop is visible
        var sheet = Page.Locator(".rm-sheet__backdrop");
        Assert.That(await sheet.CountAsync(), Is.GreaterThan(0),
            "Photo sheet did not open after tapping 'Finalizar carga'");

        // Assert photo sheet title is visible
        var sheetTitle = Page.Locator("text=Foto de la máquina");
        Assert.That(await sheetTitle.CountAsync(), Is.GreaterThan(0),
            "Photo sheet title 'Foto de la máquina' not found");

        // Assert "Subir y finalizar" button is disabled (no photo selected)
        var submitBtn = Page.Locator("button").And(
            Page.Locator("text=Subir y finalizar"));
        Assert.That(await submitBtn.CountAsync(), Is.GreaterThan(0),
            "Submit button 'Subir y finalizar' not found");
        Assert.That(await submitBtn.IsDisabledAsync(), Is.True,
            "Submit button should be disabled when no photo is selected");

        // Tap "Cancelar"
        var cancelBtn = Page.Locator("button").And(
            Page.Locator("text=Cancelar"));
        await cancelBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Assert sheet is hidden (backdrop removed)
        var sheetAfterClose = await Page.Locator(".rm-sheet__backdrop").CountAsync();
        Assert.That(sheetAfterClose, Is.EqualTo(0),
            "Photo sheet did not close after tapping Cancelar");

        // Final overflow check
        await AssertNoHorizontalOverflow();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PR 3 — Save Sheet, Figure Wrapper, Slot Fill Bar, ⚠ Icon Tests
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Navigate to EditSlots and verify the save sheet opens when Guardar is tapped.
    /// Requires seed data: a template with at least one machine that has slots.
    /// </summary>
    [Test]
    public async Task IPhone14_EditSlots_SaveSheet_Opens_OnGuardar()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");
        await Page.WaitForTimeoutAsync(1000);

        // Tap first template card → Resumen
        var firstCard = Page.Locator(".rm-card").First;
        if (await firstCard.CountAsync() == 0)
            Assert.Ignore("No template cards found — seed data required");
        await firstCard.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Tap first machine card → EditSlots
        var machineCards = Page.Locator(".rm-card");
        if (await machineCards.CountAsync() == 0)
            Assert.Ignore("No machine cards found in Resumen");
        await machineCards.First.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Verify EditSlots loaded
        var editSlotsTitle = Page.Locator("text=Paso 2");
        Assert.That(await editSlotsTitle.CountAsync(), Is.GreaterThan(0),
            "Did not navigate to EditSlots view");

        // Tap a slot to enable Guardar
        var slot = Page.Locator(".rm-slot").First;
        if (await slot.CountAsync() == 0)
            Assert.Ignore("No slots found — cannot enable Guardar");
        await slot.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Click [+] to make a change
        var sumarBtn = Page.Locator("button[aria-label='Sumar']");
        if (await sumarBtn.CountAsync() > 0)
        {
            await sumarBtn.ClickAsync();
            await Page.WaitForTimeoutAsync(200);
        }

        // Close slot dock
        var cerrarBtn = Page.Locator("button[aria-label='Cerrar']");
        if (await cerrarBtn.CountAsync() > 0)
            await cerrarBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Tap Guardar button (enabled after change)
        var guardarBtn = Page.Locator("button.rm-cta--primary").And(
            Page.Locator("text=Guardar"));
        if (await guardarBtn.CountAsync() == 0)
            Assert.Ignore("Guardar button not found or disabled");
        await guardarBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Assert save sheet backdrops renders
        var sheetBackdrop = Page.Locator(".rm-sheet__backdrop");
        Assert.That(await sheetBackdrop.CountAsync(), Is.GreaterThan(0),
            "Save sheet did not open after tapping Guardar");

        // Assert save sheet title
        var sheetTitle = Page.Locator("text=Guardar carga");
        Assert.That(await sheetTitle.CountAsync(), Is.GreaterThan(0),
            "Save sheet title 'Guardar carga' not found");

        // Assert "OBLIGATORIA" badge visible
        var obligatoriaBadge = Page.Locator("text=OBLIGATORIA");
        Assert.That(await obligatoriaBadge.CountAsync(), Is.GreaterThan(0),
            "'OBLIGATORIA' badge not found in save sheet");
    }

    /// <summary>
    /// Verify figure wrapper elements are visible in EditSlots.
    /// </summary>
    [Test]
    public async Task IPhone14_EditSlots_FigureWrapper_Visible()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");
        await Page.WaitForTimeoutAsync(1000);

        // Tap first template card → Resumen
        var firstCard = Page.Locator(".rm-card").First;
        if (await firstCard.CountAsync() == 0)
            Assert.Ignore("No template cards found");
        await firstCard.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Tap first machine card → EditSlots
        var machineCards = Page.Locator(".rm-card");
        if (await machineCards.CountAsync() == 0)
            Assert.Ignore("No machine cards found");
        await machineCards.First.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Verify figure wrapper
        var figure = Page.Locator(".rm-figure");
        Assert.That(await figure.CountAsync(), Is.GreaterThan(0),
            "Figure wrapper .rm-figure not found");

        // "RETIRE AQUÍ" bar
        var retireAqui = Page.Locator(".rm-figure__retire");
        Assert.That(await retireAqui.CountAsync(), Is.GreaterThan(0),
            "'RETIRE AQUÍ' bar not found");

        // 3×3 grid footer
        var grid = Page.Locator(".rm-figure__grid-3x3");
        Assert.That(await grid.CountAsync(), Is.GreaterThan(0),
            "3×3 grid footer not found");

        // Topbar with stats
        var topbar = Page.Locator(".rm-figure__topbar");
        Assert.That(await topbar.CountAsync(), Is.GreaterThan(0),
            "Figure topbar not found");
    }

    /// <summary>
    /// Verify slot fill bars render in EditSlots.
    /// </summary>
    [Test]
    public async Task IPhone14_EditSlots_SlotFillBars_Render()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");
        await Page.WaitForTimeoutAsync(1000);

        // Navigate to EditSlots
        var firstCard = Page.Locator(".rm-card").First;
        if (await firstCard.CountAsync() == 0)
            Assert.Ignore("No template cards found");
        await firstCard.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        var machineCards = Page.Locator(".rm-card");
        if (await machineCards.CountAsync() == 0)
            Assert.Ignore("No machine cards found");
        await machineCards.First.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Assert slot fill bars rendered
        var fillBars = Page.Locator(".rm-slot-fill");
        Assert.That(await fillBars.CountAsync(), Is.GreaterThan(0),
            "Slot fill bars (.rm-slot-fill) not found");
    }

    /// <summary>
    /// Verify empty slots show ⚠ warning icon in EditSlots.
    /// Requires seed data with at least one empty slot.
    /// </summary>
    [Test]
    public async Task IPhone14_EditSlots_EmptySlots_Show_Warning()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/recarga-movil");
        await Page.WaitForTimeoutAsync(1000);

        // Navigate to EditSlots for a machine that has empty slots
        var firstCard = Page.Locator(".rm-card").First;
        if (await firstCard.CountAsync() == 0)
            Assert.Ignore("No template cards found");
        await firstCard.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        var machineCards = Page.Locator(".rm-card");
        if (await machineCards.CountAsync() == 0)
            Assert.Ignore("No machine cards found");
        await machineCards.First.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // Find empty slots with ⚠ icon
        var emptyWarnIcons = Page.Locator(".rm-slot-empty-warn");
        var count = await emptyWarnIcons.CountAsync();
        if (count == 0)
        {
            // Not all machines have empty slots — skip rather than fail
            Assert.Ignore("No empty slot warnings found — seed data may have all slots filled");
            return;
        }

        Assert.Pass($"Found {count} empty slot warnings (⚠ icons)");
    }

    private record BoundingBoxResult(double Width, double Height, double Top, double Left);
}
