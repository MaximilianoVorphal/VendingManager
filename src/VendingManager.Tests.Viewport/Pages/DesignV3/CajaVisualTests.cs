namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Visual baseline for the redesigned Caja page.
/// </summary>
[TestFixture]
public class CajaVisualTests : VisualTestBase
{
    [Test]
    public async Task CaptureCajaBaseline()
    {
        await CaptureBaselineAsync("/caja", "caja");
    }
}
