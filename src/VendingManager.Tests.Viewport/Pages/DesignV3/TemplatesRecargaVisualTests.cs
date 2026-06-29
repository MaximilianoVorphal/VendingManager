namespace VendingManager.Tests.Viewport.Pages.DesignV3;

using System.Threading.Tasks;
using NUnit.Framework;

/// <summary>
/// Visual baseline for the redesigned Templates Recarga page.
/// </summary>
/// <remarks>
/// Baseline regeneration (manual, after apply):
///   1. Ensure the app is runnable: dotnet run --project src/VendingManager/VendingManager.csproj
///   2. Delete the stale baseline:
///      rm src/VendingManager.Tests.Viewport/DesignV3/Baselines/templates-recarga.png
///   3. Run the explicit baseline test:
///      dotnet test src/VendingManager.Tests.Viewport/VendingManager.Tests.Viewport.csproj --filter "FullyQualifiedName~CaptureTemplatesRecargaBaseline"
///   4. Verify the new PNG exists at:
///      src/VendingManager.Tests.Viewport/DesignV3/Baselines/templates-recarga.png
/// </remarks>
[TestFixture]
public class TemplatesRecargaVisualTests : VisualTestBase
{
    [Test]
    public async Task CaptureTemplatesRecargaBaseline()
    {
        await CaptureBaselineAsync("/templates-recarga", "templates-recarga");
    }
}
