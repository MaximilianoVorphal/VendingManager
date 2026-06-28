namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Visual baseline for the redesigned Informe de Ventas (Reportes) page.
/// </summary>
[TestFixture]
public class ReportesVisualTests : VisualTestBase
{
    [Test]
    public async Task CaptureReportesBaseline()
    {
        await CaptureBaselineAsync("/informe-ventas", "informe-ventas");
    }
}
