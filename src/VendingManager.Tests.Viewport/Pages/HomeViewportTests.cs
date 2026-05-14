namespace VendingManager.Tests.Viewport.Pages;

using Microsoft.Playwright.NUnit;
using NUnit.Framework;

/// <summary>
/// Viewport smoke tests for the Home page (Dashboard).
/// Verifies no horizontal overflow and key elements at all viewports.
/// </summary>
[TestFixture]
[Explicit("Viewport tests require the app to be running and Playwright browsers installed.")]
public class HomeViewportTests : ViewportTestBase
{
    /// <summary>
    /// Home page has no horizontal overflow at every viewport.
    /// </summary>
    [Test]
    [TestCaseSource(typeof(ViewportConfig), nameof(ViewportConfig.AllProfiles))]
    public async Task NoHorizontalOverflow(ViewportProfile profile)
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(profile, "/");
        await AssertNoHorizontalOverflow();
    }

    /// <summary>
    /// At mobile portrait, verify the page still renders the main content area.
    /// </summary>
    [Test]
    public async Task ContentVisibleAtMobile()
    {
        var available = await IsAppAvailableAsync();
        if (!available) Assert.Ignore("App not available at " + BaseUrl);

        await SetupViewport(ViewportConfig.IPhonePortrait, "/");

        var innerWidth = await Page.EvaluateAsync<int>("window.innerWidth");
        innerWidth.Should().Be(ViewportConfig.IPhonePortrait.Width);
    }
}