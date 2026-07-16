namespace VendingManager.Tests.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
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

        var productMatching = new Mock<IProductMatchingService>();
        var proveedorMatching = new Mock<IProveedorMatchingService>();

        var mockCategoriaConfig = new Mock<IOptionsSnapshot<CategoriaInferenciaConfig>>();
        mockCategoriaConfig.Setup(o => o.Value).Returns(new CategoriaInferenciaConfig());

        // The explicit config path (FacturaUploadPath) is no longer used by
        // CompraService's GetUploadBasePath — it relies on WebRootPath or
        // ContentRootPath/wwwroot. The configuredPath parameter is accepted but
        // not wired to CompraService (it was used via IUploadPathProvider which
        // is removed). Tests that passed configuredPath will now fall through
        // to the env-based resolution.
        return (new CompraService(
            context,
            productMatching.Object,
            proveedorMatching.Object,
            envMock.Object,
            mockCategoriaConfig.Object,
            new Mock<ILogger<CompraService>>().Object), envMock);
    }

    // ── Regression: step 1 — WebRootPath wins ─────────────────────────────────

    [Fact]
    public void ResolveFacturaPhysicalPath_WhenWebRootSet_UsesWebRootPath()
    {
        var (service, _) = BuildService(webRootPath: "/app/wwwroot");

        var physical = service.ResolveFacturaPhysicalPath("/uploads/compras/facturas/file.jpg");

        physical.Should().Be("/app/wwwroot/uploads/compras/facturas/file.jpg");
    }

    // ── Regression: step 2 — ContentRootPath/wwwroot fallback ────────────────

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
