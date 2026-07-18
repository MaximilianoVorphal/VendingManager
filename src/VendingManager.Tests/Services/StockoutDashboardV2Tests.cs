namespace VendingManager.Tests.Services;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

public class StockoutDashboardV2Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TemplateRecargaAnalyticsService _templateService;
    private readonly SalesAnalyticsService _salesService;
    private readonly IMemoryCache _cache;

    public StockoutDashboardV2Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"StockoutDashboardV2_{Guid.NewGuid()}");
        _cache = new MemoryCache(new MemoryCacheOptions());
        _templateService = new TemplateRecargaAnalyticsService(
            _context,
            new Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaAnalyticsService>>().Object);
        _salesService = new SalesAnalyticsService(
            _context,
            new Mock<IExcelExportService>().Object,
            _cache,
            Options.Create(AnalyticsThresholds.Default),
            Options.Create(new VendingConfig()));
    }

    public void Dispose()
    {
        _context.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public async Task AnalyzarPorTemplateV2Async_UsesRawChronologicalVentasForMachineProductDepletion()
    {
        var start = new DateTime(2025, 1, 1, 8, 0, 0);
        await SeedTemplateAsync(start);

        await AddSalesAsync(1, "1", new[] { start.AddHours(1), start.AddHours(3) });
        await AddSalesAsync(1, "2", new[] { start.AddHours(2) });

        var result = await _templateService.AnalyzarPorTemplateV2Async(1);

        result.Slots.Should().HaveCount(2);
        var product = result.ProductosMaquina.Should().ContainSingle().Subject;
        product.MaquinaId.Should().Be(1);
        product.StockInicialTotal.Should().Be(3);
        product.CantidadVendidaTotal.Should().Be(3);
        product.FechaAgotamientoEstimada.Should().Be(start.AddHours(3),
            "the third raw Venta, not a slot-summary timestamp, consumes the final unit");
        product.TieneEvidenciaCronologicaIncompleta.Should().BeFalse();
    }

    [Fact]
    public async Task GetStockoutDashboardAnalysisV2Async_SeparatesMachinesUsingRawEvidence()
    {
        var start = new DateTime(2025, 2, 1, 8, 0, 0);
        _context.Maquinas.AddRange(
            TestDataHelpers.CreateMaquina(id: 1, nombre: "M1"),
            TestDataHelpers.CreateMaquina(id: 2, nombre: "M2"));
        _context.Productos.Add(TestDataHelpers.CreateProducto(id: 1, nombre: "P1"));
        _context.ConfiguracionSlots.AddRange(
            TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 0),
            TestDataHelpers.CreateSlot(id: 2, maquinaId: 2, productoId: 1, stockActual: 0));
        _context.TemplatesRecarga.AddRange(
            Template(1, 1, start, 1, "1", 1),
            Template(2, 2, start, 2, "1", 1));
        await AddSalesAsync(1, "1", new[] { start.AddHours(1) });
        await AddSalesAsync(2, "1", new[] { start.AddHours(1) });
        await _context.SaveChangesAsync();

        var result = await _salesService.GetStockoutDashboardAnalysisV2Async(start, start.AddDays(1), 0);

        result.ProductosMaquina.Should().HaveCount(2);
        result.ProductosMaquina.Should().OnlyContain(row => row.FechaAgotamientoEstimada == start.AddHours(1),
            "each machine has its own raw Venta at the one-unit depletion boundary");
        result.ProductosMaquina.Select(row => row.MaquinaId).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task Controllers_ExposeV2BundlesWhileLegacyEndpointsKeepTheirExistingContracts()
    {
        var analytics = new Mock<ITemplateRecargaAnalyticsService>();
        var templateService = new Mock<ITemplateRecargaService>();
        var bundle = new StockoutDashboardAnalysisDto { ProductosMaquina = [new() { MaquinaId = 1, ProductoId = 1 }] };
        templateService.Setup(service => service.GetByIdAsync(7)).ReturnsAsync(new TemplateRecargaDto { Id = 7 });
        templateService.Setup(service => service.AnalyzarPorTemplateAsync(7, 24)).ReturnsAsync([new StockoutAnalysisDto { NumeroSlot = "1" }]);
        analytics.Setup(service => service.AnalyzarPorTemplateV2Async(7, 24)).ReturnsAsync(bundle);
        var templateController = new TemplateRecargaController(templateService.Object, new Mock<ITemplateRecargaLifecycleService>().Object, analytics.Object);

        var legacy = await templateController.AnalyzePorTemplate(7);
        var v2 = await templateController.AnalyzePorTemplateV2(7);

        legacy.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<List<StockoutAnalysisDto>>();
        v2.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(bundle);

        var salesAnalytics = new Mock<ISalesAnalyticsService>();
        salesAnalytics.Setup(service => service.GetStockoutAnalysisAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 1, 24))
            .ReturnsAsync([new StockoutAnalysisDto { NumeroSlot = "1" }]);
        salesAnalytics.Setup(service => service.GetStockoutDashboardAnalysisV2Async(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 1, 24))
            .ReturnsAsync(bundle);
        var ventasController = CreateVentasController(salesAnalytics.Object);

        var oldSales = await ventasController.GetStockoutAnalysis(DateTime.Today, DateTime.Today.AddDays(1), 1, 24);
        var v2Sales = await ventasController.GetStockoutAnalysisV2(DateTime.Today, DateTime.Today.AddDays(1), 1, 24);

        oldSales.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<List<StockoutAnalysisDto>>();
        v2Sales.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(bundle);
    }

    private async Task SeedTemplateAsync(DateTime start)
    {
        _context.Maquinas.Add(TestDataHelpers.CreateMaquina(id: 1, nombre: "M1"));
        _context.Productos.Add(TestDataHelpers.CreateProducto(id: 1, nombre: "P1"));
        _context.TemplatesRecarga.Add(new TemplateRecarga
        {
            Id = 1,
            Nombre = "T1",
            Periodos = [new PeriodoRecarga
            {
                Id = 1,
                MaquinaId = 1,
                FechaRecarga = start,
                SnapshotSlots = [
                    new SnapshotSlot { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 2 },
                    new SnapshotSlot { NumeroSlot = "2", ProductoId = 1, CantidadInicial = 1 }]
            }]
        });
        await _context.SaveChangesAsync();
    }

    private async Task AddSalesAsync(int maquinaId, string slot, IEnumerable<DateTime> timestamps)
    {
        foreach (var timestamp in timestamps)
        {
            _context.Ventas.Add(new Venta
            {
                MaquinaId = maquinaId, ProductoId = 1, NumeroSlot = slot,
                FechaLocal = timestamp, FechaHora = timestamp, PrecioVenta = 1000m,
                CostoVenta = 400m, IdOrdenMaquina = Guid.NewGuid().ToString()
            });
        }
        await _context.SaveChangesAsync();
    }

    private static TemplateRecarga Template(int id, int machineId, DateTime start, int periodId, string slot, int stock) => new()
    {
        Id = id,
        Nombre = $"T{id}",
        Periodos = [new PeriodoRecarga
        {
            Id = periodId,
            MaquinaId = machineId,
            FechaRecarga = start,
            SnapshotSlots = [new SnapshotSlot { NumeroSlot = slot, ProductoId = 1, CantidadInicial = stock }]
        }]
    };

    private static VentasController CreateVentasController(ISalesAnalyticsService analytics) => new(
        new Mock<IVentasService>().Object, new Mock<IInformesService>().Object,
        new Mock<ISyncOrchestratorService>().Object, analytics, new Mock<IPurchasingService>().Object,
        new Mock<ISalesImportService>().Object, new Mock<IAuditService>().Object,
        new LastSyncTracker(new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().Object),
        new Mock<IScraperClient>().Object);
}
