using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Clients;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Services;

public class SyncOrchestratorServiceTests
{
    private static SyncOrchestratorService CreateService(
        Mock<IScraperClient>? scraperMock = null,
        Mock<ISalesImportService>? importMock = null)
    {
        scraperMock ??= new Mock<IScraperClient>();
        importMock ??= new Mock<ISalesImportService>();
        return new SyncOrchestratorService(scraperMock.Object, importMock.Object);
    }

    // ── FIX-3: SalesReportResponse.Status-based classification ────────────

    [Fact]
    public async Task SincronizarDesdePortalApi_StatusOk_WithRows_ReturnsOk()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse
            {
                Status = "ok",
                Total = 5,
                Rows = new List<SalesReportRowDto> { new() }
            });

        var import = new Mock<ISalesImportService>();
        import.Setup(i => i.ImportarVentasDesdeJson(It.IsAny<List<SalesReportRowDto>>(),
                It.IsAny<DateTime?>(), It.IsAny<string?>()))
            .ReturnsAsync("5 rows imported");

        var svc = CreateService(scraper, import);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Ok);
        result.Stats.Should().Contain("5 rows");
    }

    [Fact]
    public async Task SincronizarDesdePortalApi_StatusOk_ZeroRows_ReturnsEmpty()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse
            {
                Status = "ok",
                Total = 0,
                Rows = new List<SalesReportRowDto>()
            });

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Empty);
    }

    [Fact]
    public async Task SincronizarDesdePortalApi_StatusEmpty_ReturnsEmpty()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse { Status = "empty", Reason = "No data in range" });

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Empty);
        result.Details.Should().Contain("No data");
    }

    [Fact]
    public async Task SincronizarDesdePortalApi_StatusBlocked_ReturnsBlocked()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse
            {
                Status = "blocked",
                Reason = "Challenge page detected"
            });

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Blocked);
        result.Details.Should().Contain("Challenge page");
    }

    [Fact]
    public async Task SincronizarDesdePortalApi_StatusError_ReturnsError()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse
            {
                Status = "error",
                Reason = "Internal scraper failure"
            });

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Error);
        result.Details.Should().Contain("Internal scraper");
    }

    [Fact]
    public async Task SincronizarDesdePortalApi_StatusTimeout_ReturnsTimeout()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse
            {
                Status = "timeout",
                Reason = "Browser flow exceeded budget"
            });

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Timeout);
        result.Details.Should().Contain("Browser flow");
    }

    // ── FIX-2: Timeout→WafBlockedException conflation ──────────────────────

    [Fact]
    public async Task SincronizarDesdePortalApi_OperationCanceledException_ReturnsTimeout_NotBlocked()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("The operation was canceled."));

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Timeout,
            "OperationCanceledException must be classified as Timeout, not Blocked");
    }

    [Fact]
    public async Task SincronizarDesdePortalApi_WafBlockedException_ReturnsBlocked_NotTimeout()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WafBlockedException("503 Service Unavailable"));

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Blocked,
            "WafBlockedException must be classified as Blocked, not Timeout");
    }

    // ── FIX-5: CancellationToken propagation ───────────────────────────────

    [Fact]
    public async Task SincronizarDesdePortalApi_CancelledToken_PropagatesToScraper()
    {
        var scraper = new Mock<IScraperClient>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var svc = CreateService(scraper);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow, cts.Token);

        result.Outcome.Should().Be(SyncOutcome.Timeout,
            "cancelled token should result in Timeout outcome");
    }

    // ── Status is null or unrecognised — falls back to row-count ──────────

    [Fact]
    public async Task SincronizarDesdePortalApi_NullStatus_WithRows_ReturnsOk()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse
            {
                Status = null!, // null falls through to row-count
                Total = 3,
                Rows = new List<SalesReportRowDto> { new(), new(), new() }
            });

        var import = new Mock<ISalesImportService>();
        import.Setup(i => i.ImportarVentasDesdeJson(It.IsAny<List<SalesReportRowDto>>(),
                It.IsAny<DateTime?>(), It.IsAny<string?>()))
            .ReturnsAsync("3 rows");

        var svc = CreateService(scraper, import);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Ok);
    }

    [Fact]
    public async Task SincronizarDesdePortalApi_UnknownStatus_WithRows_ReturnsOk()
    {
        var scraper = new Mock<IScraperClient>();
        scraper.Setup(s => s.GetSalesReportAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportResponse
            {
                Status = "processing", // unrecognised — falls back to row-count
                Total = 1,
                Rows = new List<SalesReportRowDto> { new() }
            });

        var import = new Mock<ISalesImportService>();
        import.Setup(i => i.ImportarVentasDesdeJson(It.IsAny<List<SalesReportRowDto>>(),
                It.IsAny<DateTime?>(), It.IsAny<string?>()))
            .ReturnsAsync("1 row");

        var svc = CreateService(scraper, import);
        var result = await svc.SincronizarDesdePortalApi(DateTime.UtcNow, DateTime.UtcNow);

        result.Outcome.Should().Be(SyncOutcome.Ok);
    }
}
