namespace VendingManager.Tests.Viewport.Pages;

/// <summary>
/// Base class for viewport smoke tests. Provides page navigation,
/// browser context management, and common assertions.
///
/// Tests are marked [Explicit] so they only run when the app is available
/// and Playwright browsers are installed. This allows the test project to
/// build cleanly even when no browser is available.
/// </summary>
[TestFixture]
[Explicit("Viewport tests require the app to be running and Playwright browsers installed.")]
public abstract class ViewportTestBase
{
    private IBrowser? _browser;
    private IPage? _page;

    /// <summary>The base URL of the VendingManager Web app.</summary>
    protected virtual string BaseUrl => GetBaseUrl();

    private static string GetBaseUrl()
    {
        var env = Environment.GetEnvironmentVariable("VIEWPORT_TEST_BASE_URL");
        return !string.IsNullOrWhiteSpace(env) ? env : "https://localhost:7001";
    }

    /// <summary>Current Playwright page instance for the test.</summary>
    protected IPage Page => _page
        ?? throw new InvalidOperationException("Setup not called. Call SetViewport before accessing Page.");

    /// <summary>Current browser context for the test.</summary>
    protected IBrowserContext Context { get; private set; } = null!;

    /// <summary>
    /// Launch browser, create context with viewport, and navigate to the given path.
    /// Override <see cref="BaseUrl"/> to change the base URL.
    /// </summary>
    /// <param name="profile">Viewport profile to set.</param>
    /// <param name="path">Server-relative path (e.g. "/templates-recarga").</param>
    protected async Task SetupViewport(ViewportProfile profile, string path)
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        _browser = await playwright.Chromium.ConnectOverCDPAsync(
            Environment.GetEnvironmentVariable("PLAYWRIGHT_CDP_URL") ?? "http://localhost:9222");

        Context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = profile.Width, Height = profile.Height },
            DeviceScaleFactor = (float)profile.DeviceScaleFactor,
            IsMobile = profile.IsMobile,
        });

        _page = await Context.NewPageAsync();

        var url = BaseUrl.TrimEnd('/') + path;
        await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // Wait a bit for Blazor to settle
        await Task.Delay(500);
    }

    /// <summary>
    /// Assert that the page has no horizontal overflow.
    /// Fails the test if documentElement.scrollWidth > window.innerWidth.
    /// </summary>
    protected async Task AssertNoHorizontalOverflow()
    {
        var result = await Page.EvaluateAsync<OverflowResult>(@"
            () => ({
                scrollWidth: document.documentElement.scrollWidth,
                clientWidth: window.innerWidth,
            })
        ");

        Assert.That(result.ScrollWidth, Is.LessThanOrEqualTo(result.ClientWidth),
            $"Horizontal overflow detected: scrollWidth={result.ScrollWidth} > clientWidth={result.ClientWidth}");
    }

    /// <summary>
    /// Checks whether the app is reachable at BaseUrl.
    /// Returns false if the server is down; tests will be skipped.
    /// </summary>
    protected async Task<bool> IsAppAvailableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.GetAsync(BaseUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_page != null) await _page.CloseAsync();
        if (Context != null) await Context.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
    }

    private record OverflowResult(int ScrollWidth, int ClientWidth);
}
