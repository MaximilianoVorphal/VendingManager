namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Visual baseline for the redesigned Templates Recarga page.
/// </summary>
[TestFixture]
public class TemplatesRecargaVisualTests : VisualTestBase
{
    [Test]
    public async Task CaptureTemplatesRecargaBaseline()
    {
        await CaptureBaselineAsync("/templates-recarga", "templates-recarga");
    }
}
