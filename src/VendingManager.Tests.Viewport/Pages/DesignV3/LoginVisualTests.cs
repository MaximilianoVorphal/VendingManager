namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Visual baseline for the redesigned Login page.
/// </summary>
[TestFixture]
public class LoginVisualTests : VisualTestBase
{
    [Test]
    public async Task CaptureLoginBaseline()
    {
        await CaptureBaselineAsync("/login", "login");
    }
}
