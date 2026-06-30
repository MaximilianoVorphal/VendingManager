using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using Xunit;

namespace VendingManager.Tests.Controllers;

public class VentasControllerLastSyncTests
{
    private readonly Mock<IVentasService> _mockVentasService = new();
    private readonly Mock<IInformesService> _mockInformesService = new();
    private readonly Mock<ISyncOrchestratorService> _mockSyncService = new();
    private readonly Mock<ISalesAnalyticsService> _mockSalesAnalytics = new();
    private readonly Mock<IPurchasingService> _mockPurchasing = new();
    private readonly Mock<ISalesImportService> _mockSalesImport = new();
    private readonly Mock<IAuditService> _mockAudit = new();
    private readonly LastSyncTracker _tracker = new();

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
            _tracker);
    }

    [Fact]
    public void GetLastSync_WhenNoSync_ReturnsOkWithNull()
    {
        var controller = CreateController();

        var result = controller.GetLastSync();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        // The anonymous object has a 'lastSync' property with null value
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        json.Should().Contain("\"lastSync\":null");
    }

    [Fact]
    public void GetLastSync_AfterSetLastSync_ReturnsCorrectDate()
    {
        var controller = CreateController();
        var expected = new DateTime(2026, 6, 30, 14, 30, 0, DateTimeKind.Local);
        _tracker.SetLastSync(expected);

        var result = controller.GetLastSync();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        json.Should().Contain("2026-06-30");
        json.Should().Contain("14:30");
    }
}
