namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

/// <summary>
/// Base class for design-v3 visual baseline tests.
/// Launches a headless Chromium context, logs in for protected pages, and captures
/// full-page PNG baselines.
///
/// Tests are <see cref="ExplicitAttribute"/> because they require the app to be
/// running and Playwright browsers installed.
/// </summary>
[TestFixture]
[Explicit("Visual baseline tests require the app to be running and Playwright browsers installed.")]
public abstract class VisualTestBase
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    /// <summary>
    /// Base URL of the VendingManager app. Override via VIEWPORT_TEST_BASE_URL.
    /// </summary>
    protected virtual string BaseUrl => GetBaseUrl();

    private static string GetBaseUrl()
    {
        var env = Environment.GetEnvironmentVariable("VIEWPORT_TEST_BASE_URL");
        return !string.IsNullOrWhiteSpace(env) ? env : "https://localhost:7001";
    }

    /// <summary>Current Playwright page instance for the test.</summary>
    protected IPage Page => _page
        ?? throw new InvalidOperationException("Setup not called. Call CaptureBaselineAsync first.");

    /// <summary>
    /// Navigates to <paramref name="path"/> at the design viewport, logs in when
    /// required, waits for a stable state, and saves/returns the baseline path.
    /// </summary>
    protected async Task<string> CaptureBaselineAsync(string path, string baselineName)
    {
        if (!await IsAppAvailableAsync())
            Assert.Ignore($"App not available at {BaseUrl}");

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_EXECUTABLE_PATH"),
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = ViewportConfig.Desktop1080p.Width,
                Height = ViewportConfig.Desktop1080p.Height,
            },
            IgnoreHTTPSErrors = true,
        });

        _page = await _context.NewPageAsync();

        var isPublic = path.Equals("/login", StringComparison.OrdinalIgnoreCase);
        if (!isPublic)
        {
            await LoginAsync();
        }

        var url = BaseUrl.TrimEnd('/') + path;
        await Page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(500);

        var baselinePath = await VisualBaseline.SaveBaselineAsync(Page, baselineName);
        Assert.That(File.Exists(baselinePath), Is.True, $"Baseline was not created: {baselinePath}");

        return baselinePath;
    }

    /// <summary>
    /// Logs in through the UI using the credentials in VIEWPORT_TEST_USER /
    /// VIEWPORT_TEST_PASSWORD (defaults to admin / admin).
    /// </summary>
    private async Task LoginAsync()
    {
        var loginUrl = BaseUrl.TrimEnd('/') + "/login";
        await Page.GotoAsync(loginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(500);

        var user = Environment.GetEnvironmentVariable("VIEWPORT_TEST_USER") ?? "admin";
        var password = Environment.GetEnvironmentVariable("VIEWPORT_TEST_PASSWORD") ?? "admin";

        try
        {
            await Page.GetByLabel("Usuario").FillAsync(user);
            await Page.GetByLabel("Contraseña").FillAsync(password);
            await Page.Locator("button[type=\"submit\"]").ClickAsync();
            await Page.WaitForURLAsync("**/", new PageWaitForURLOptions { Timeout = 10000 });
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Could not log in for visual baseline: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether the app is reachable at <see cref="BaseUrl"/>.
    /// </summary>
    protected async Task<bool> IsAppAvailableAsync()
    {
        try
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Add("Accept", "text/html");
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
        if (_context != null) await _context.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}
