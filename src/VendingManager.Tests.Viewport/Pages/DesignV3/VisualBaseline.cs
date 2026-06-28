namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;

/// <summary>
/// Helper for design-v3 visual regression baselines.
/// Baselines are stored under <c>DesignV3/Baselines</c> at the test-project root.
/// A baseline is only written once; subsequent runs keep the existing file stable.
/// </summary>
public static class VisualBaseline
{
    /// <summary>
    /// Absolute path to the baseline folder (resolved from the test assembly location).
    /// </summary>
    public static string BaselineDirectory { get; } = ResolveBaselineDirectory();

    /// <summary>
    /// Returns the absolute path for a named baseline PNG.
    /// </summary>
    public static string GetBaselinePath(string name) =>
        Path.Combine(BaselineDirectory, $"{name}.png");

    /// <summary>
    /// Captures a full-page PNG screenshot and saves it as the baseline if it does not
    /// already exist. Existing baselines are never overwritten so later runs stay stable.
    /// </summary>
    /// <param name="page">Playwright page in a stable state.</param>
    /// <param name="name">Baseline file name without extension.</param>
    /// <returns>The absolute path to the baseline file.</returns>
    public static async Task<string> SaveBaselineAsync(IPage page, string name)
    {
        Directory.CreateDirectory(BaselineDirectory);

        var path = GetBaselinePath(name);

        if (!File.Exists(path))
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = path,
                FullPage = true,
                Type = ScreenshotType.Png,
            });
        }

        return path;
    }

    private static string ResolveBaselineDirectory()
    {
        var assemblyDir = AppContext.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", ".."));
        return Path.Combine(projectDir, "DesignV3", "Baselines");
    }
}
