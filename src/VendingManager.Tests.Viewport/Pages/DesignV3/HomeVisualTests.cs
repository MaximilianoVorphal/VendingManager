namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Visual baseline for the redesigned Home (Panel de Control) page.
/// </summary>
[TestFixture]
public class HomeVisualTests : VisualTestBase
{
    [Test]
    public async Task CaptureHomeBaseline()
    {
        await CaptureBaselineAsync("/", "home");
    }
}
