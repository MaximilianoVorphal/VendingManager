namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Visual baseline for the redesigned Análisis de Productos page.
/// </summary>
[TestFixture]
public class AnalisisProductosVisualTests : VisualTestBase
{
    [Test]
    public async Task CaptureAnalisisProductosBaseline()
    {
        await CaptureBaselineAsync("/analisis-productos", "analisis-productos");
    }
}
