using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Clients;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Services;
using Xunit;

namespace VendingManager.Tests.Controllers;

/// <summary>
/// Controller-level tests for VentasController's H-3 exception-propagation remediation (REQ-ERR-01).
/// Verifies the local try/catch around SubirVentasMaquina/SubirTransbank was removed so unhandled
/// exceptions propagate to GlobalProblemDetailsMiddleware instead of leaking ex.Message.
/// </summary>
public class VentasControllerTests
{
    private readonly Mock<IVentasService> _mockVentasService = new();
    private readonly Mock<IInformesService> _mockInformesService = new();
    private readonly Mock<ISyncOrchestratorService> _mockSyncService = new();
    private readonly Mock<ISalesAnalyticsService> _mockSalesAnalytics = new();
    private readonly Mock<IPurchasingService> _mockPurchasing = new();
    private readonly Mock<ISalesImportService> _mockSalesImport = new();
    private readonly Mock<IAuditService> _mockAudit = new();
    private readonly Mock<IScraperClient> _mockScraper = new();
    private readonly LastSyncTracker _tracker = CreateTracker();

    private static LastSyncTracker CreateTracker()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var providerMock = new Mock<IServiceProvider>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        providerMock.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns(db);
        scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        return new LastSyncTracker(scopeFactoryMock.Object);
    }

    private VentasController CreateController()
    {
        return new VentasController(
            _mockVentasService.Object,
            _mockInformesService.Object,
            _mockSyncService.Object,
            _mockSalesAnalytics.Object,
            _mockPurchasing.Object,
            _mockSalesImport.Object,
            _mockAudit.Object,
            _tracker,
            _mockScraper.Object);
    }

    private static IFormFile CreateMockFormFile(byte[] content, string contentType, string fileName)
    {
        var stream = new MemoryStream(content);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(content.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream s, CancellationToken ct) => stream.CopyToAsync(s, ct));
        return fileMock.Object;
    }

    // ─── SubirVentasMaquina ─────────────────────────────────────────────

    [Fact]
    public async Task SubirVentasMaquina_ServiceThrows_ExceptionPropagates()
    {
        var controller = CreateController();
        var file = CreateMockFormFile(new byte[] { 1, 2, 3 }, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ventas.xlsx");

        _mockInformesService
            .Setup(s => s.SubirInformeAsync(It.IsAny<VendingManager.Core.Entities.Informe>()))
            .ReturnsAsync(new VendingManager.Core.Entities.Informe());
        _mockSalesImport
            .Setup(s => s.ImportarVentasMaquina(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = async () => await controller.SubirVentasMaquina(file, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── SubirTransbank ─────────────────────────────────────────────────

    [Fact]
    public async Task SubirTransbank_ServiceThrows_ExceptionPropagates()
    {
        var controller = CreateController();
        var file = CreateMockFormFile(new byte[] { 1, 2, 3 }, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "transbank.xlsx");

        _mockInformesService
            .Setup(s => s.SubirInformeAsync(It.IsAny<VendingManager.Core.Entities.Informe>()))
            .ReturnsAsync(new VendingManager.Core.Entities.Informe());
        _mockSalesImport
            .Setup(s => s.ImportarTransbank(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = async () => await controller.SubirTransbank(file, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
