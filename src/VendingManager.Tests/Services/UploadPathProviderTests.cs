namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

/// <summary>
/// TASK-05 — Regression test that pins the existing CompraService base-path resolution
/// (three-step fallback: config FacturaUploadPath → WebRootPath → ContentRootPath/wwwroot).
/// These tests MUST pass before AND after the IUploadPathProvider refactor to confirm
/// byte-identical behavior.
///
/// Also tests the extracted IUploadPathProvider / DefaultUploadPathProvider directly.
/// </summary>
public class UploadPathProviderTests
{
    // ── Helper: build CompraService with mocked env/config ───────────────────

    private static (CompraService service, Mock<IWebHostEnvironment> envMock) BuildService(
        string? configuredPath = null,
        string? webRootPath = null,
        string contentRootPath = "/app")
    {
        var context = TestDataHelpers.CreateInMemoryContext($"UploadPathDb_{Guid.NewGuid()}");

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.WebRootPath).Returns(webRootPath!);
        envMock.SetupGet(e => e.ContentRootPath).Returns(contentRootPath);

        var cfg = Options.Create(new VendingConfig { FacturaUploadPath = configuredPath });

        var productMatching = new Mock<IProductMatchingService>();

        var uploadProvider = new DefaultUploadPathProvider(envMock.Object, cfg);

        return (new CompraService(context, productMatching.Object, uploadProvider), envMock);
    }

    // ── Regression: step 1 — config path wins ────────────────────────────────

    [Fact]
    public void ResolveFacturaPhysicalPath_WhenConfiguredPath_UsesConfiguredPath()
    {
        var (service, _) = BuildService(configuredPath: "/data/uploads");

        var physical = service.ResolveFacturaPhysicalPath("/uploads/compras/facturas/file.jpg");

        physical.Should().Be("/data/uploads/uploads/compras/facturas/file.jpg");
    }

    // ── Regression: step 2 — WebRootPath fallback ────────────────────────────

    [Fact]
    public void ResolveFacturaPhysicalPath_WhenNoConfiguredPath_UsesWebRootPath()
    {
        var (service, _) = BuildService(configuredPath: null, webRootPath: "/app/wwwroot");

        var physical = service.ResolveFacturaPhysicalPath("/uploads/compras/facturas/file.jpg");

        physical.Should().Be("/app/wwwroot/uploads/compras/facturas/file.jpg");
    }

    // ── Regression: step 3 — ContentRootPath/wwwroot fallback ───────────────

    [Fact]
    public void ResolveFacturaPhysicalPath_WhenNoWebRoot_UsesContentRootWwwroot()
    {
        // WebRootPath null simulates the case where wwwroot dir doesn't exist on disk.
        // Use a temp directory that the test process can create.
        var tempRoot = Path.Combine(Path.GetTempPath(), $"vm_test_{Guid.NewGuid()}");
        try
        {
            var (service, _) = BuildService(
                configuredPath: null,
                webRootPath: null,
                contentRootPath: tempRoot);

            var physical = service.ResolveFacturaPhysicalPath("/uploads/compras/facturas/file.jpg");

            physical.Should().StartWith(Path.Combine(tempRoot, "wwwroot"));
            physical.Should().EndWith("uploads/compras/facturas/file.jpg");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── IUploadPathProvider — extracted interface tests ───────────────────────

    [Fact]
    public void DefaultUploadPathProvider_WhenConfiguredPath_ReturnsConfiguredPath()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.WebRootPath).Returns((string)null!);
        envMock.SetupGet(e => e.ContentRootPath).Returns("/app");

        var cfg = Options.Create(new VendingConfig { FacturaUploadPath = "/mnt/uploads" });

        IUploadPathProvider provider = new DefaultUploadPathProvider(envMock.Object, cfg);

        provider.GetUploadBasePath().Should().Be("/mnt/uploads");
    }

    [Fact]
    public void DefaultUploadPathProvider_WhenNoConfig_UsesWebRootPath()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.WebRootPath).Returns("/var/www");
        envMock.SetupGet(e => e.ContentRootPath).Returns("/app");

        var cfg = Options.Create(new VendingConfig());

        IUploadPathProvider provider = new DefaultUploadPathProvider(envMock.Object, cfg);

        provider.GetUploadBasePath().Should().Be("/var/www");
    }

    [Fact]
    public void DefaultUploadPathProvider_WhenNoWebRoot_UsesContentRootWwwroot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"vm_test_{Guid.NewGuid()}");
        try
        {
            var envMock = new Mock<IWebHostEnvironment>();
            envMock.SetupGet(e => e.WebRootPath).Returns((string)null!);
            envMock.SetupGet(e => e.ContentRootPath).Returns(tempRoot);

            var cfg = Options.Create(new VendingConfig());

            IUploadPathProvider provider = new DefaultUploadPathProvider(envMock.Object, cfg);

            provider.GetUploadBasePath().Should().StartWith(Path.Combine(tempRoot, "wwwroot"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
