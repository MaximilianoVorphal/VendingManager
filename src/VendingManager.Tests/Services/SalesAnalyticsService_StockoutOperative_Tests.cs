namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Helpers;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for stockout analysis using operative hours (14h/day) instead of
/// wall-clock math. Intentional user-visible metric change.
/// </summary>
public class SalesAnalyticsService_StockoutOperative_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IExcelExportService> _mockExcelExport;
    private readonly IMemoryCache _cache;
    private readonly IOptions<AnalyticsThresholds> _thresholds;
    private readonly IOptions<VendingConfig> _config;
    private readonly SalesAnalyticsService _service;

    public SalesAnalyticsService_StockoutOperative_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"StockoutOperativeTestDb_{Guid.NewGuid()}");
        _mockExcelExport = new Mock<IExcelExportService>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _thresholds = Options.Create(AnalyticsThresholds.Default);
        _config = Options.Create(new VendingConfig());
        _service = new SalesAnalyticsService(
            _context, _mockExcelExport.Object, _cache, _thresholds, _config);
    }

    public void Dispose()
    {
        _context.Dispose();
        _cache.Dispose();
    }

    // ── Operative hours constant ─────────────────────────────────────

    [Fact]
    public void HorasOperativasPorDia_Is14()
    {
        HorarioOperativoHelper.HorasOperativasPorDia.Should().Be(14);
    }

    [Fact]
    public void HorasEnRangoOperativo_ExcludesClosedHours()
    {
        // Sale at 20:00, next activity at 10:00 next day
        var desde = new DateTime(2026, 7, 1, 20, 0, 0);
        var hasta = new DateTime(2026, 7, 2, 10, 0, 0);

        var horas = HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

        // 20:00-22:00 (2h) + 08:00-10:00 (2h) = 4h
        // Wall-clock would be 14h
        horas.Should().BeApproximately(4.0, 0.01);
    }

    [Fact]
    public void HorasEnRangoOperativo_FullOperativeDay_Returns14()
    {
        var desde = new DateTime(2026, 7, 1, 8, 0, 0);
        var hasta = new DateTime(2026, 7, 1, 22, 0, 0);

        var horas = HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

        horas.Should().BeApproximately(14.0, 0.01);
    }

    // ── VelocidadDiaria with operative hours ─────────────────────────

    [Fact]
    public void VelocidadDiaria_IsPer14Hours()
    {
        // velocidadPorHora = 10 units/h
        // With wall-clock: 10 * 24 = 240/day
        // With operative:  10 * 14 = 140/day
        var velocidadPorHora = 10m;
        var operativa = velocidadPorHora * HorarioOperativoHelper.HorasOperativasPorDia;

        operativa.Should().Be(140m);
    }

    // ── Integration: stockout analysis uses operative hours ──────────

    [Fact]
    public async Task GetStockoutAnalysisAsync_UsesOperativeHours()
    {
        // Arrange: one machine, one product, two sales
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "M1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "P1");
        _context.Productos.Add(producto);

        var prod = await _context.Productos.FindAsync(1);

        // Sale at 10:00 AM, activity until 18:00 (both inside operative window)
        var primeraVenta = new DateTime(2026, 7, 1, 10, 0, 0);
        var ultimaVenta = new DateTime(2026, 7, 1, 14, 0, 0);

        _context.Ventas.Add(new Venta
        {
            FechaLocal = primeraVenta,
            FechaHora = primeraVenta,
            MaquinaId = 1,
            ProductoId = 1,
            NumeroSlot = "1",
            PrecioVenta = 1000m,
            CostoVenta = 400m,
            Pagado = true,
            IdOrdenMaquina = "TEST-001"
        });

        _context.Ventas.Add(new Venta
        {
            FechaLocal = ultimaVenta,
            FechaHora = ultimaVenta,
            MaquinaId = 1,
            ProductoId = 1,
            NumeroSlot = "1",
            PrecioVenta = 1000m,
            CostoVenta = 400m,
            Pagado = true,
            IdOrdenMaquina = "TEST-002"
        });

        await _context.SaveChangesAsync();

        // Act
        var results = await _service.GetStockoutAnalysisAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2),
            maquinaId: 1,
            umbralHorasSilencio: 14);

        // Assert
        results.Should().NotBeEmpty();
        var slot = results[0];

        // The requested report end is exact midnight on Jul 2 (not expanded to end-of-day).
        // Therefore the two sales have 14 operating hours of exposure: 2 / 14.
        slot.CantidadVendida.Should().Be(2);
        slot.VelocidadPorHora.Should().BeApproximately(2m / 14m, 0.01m);
        slot.VelocidadDiaria.Should().BeApproximately(2m, 0.01m);

        // HorasSinStock computed with operative hours
        slot.HorasActivas.Should().BeGreaterThan(0);
    }

    // ── Default umbralHorasSilencio = 14 operative hours ─────────────
    // The method signature default changes from 24 to 14.

    [Fact]
    public async Task GetStockoutAnalysisAsync_DefaultUmbral_Is14()
    {
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "M1");
        _context.Maquinas.Add(maquina);

        var prod = TestDataHelpers.CreateProducto(id: 1, nombre: "P1");
        _context.Productos.Add(prod);

        _context.Ventas.Add(new Venta
        {
            FechaLocal = new DateTime(2026, 7, 1, 10, 0, 0),
            FechaHora = new DateTime(2026, 7, 1, 10, 0, 0),
            MaquinaId = 1,
            ProductoId = 1,
            NumeroSlot = "1",
            PrecioVenta = 1000m,
            CostoVenta = 400m,
            Pagado = true,
            IdOrdenMaquina = "TEST-001"
        });

        await _context.SaveChangesAsync();

        // Call WITHOUT explicit umbral — should default to 14
        var results = await _service.GetStockoutAnalysisAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2),
            maquinaId: 1);

        results.Should().NotBeEmpty();
        // With 14h default, a single sale at 10am with very long gap
        // to end of period should trigger PosibleQuiebre
    }

    [Fact]
    public async Task GetStockoutAnalysisAsync_UsesPreDepletionEvidenceAndExactReportEnd()
    {
        var start = new DateTime(2025, 1, 1, 8, 0, 0);
        var end = new DateTime(2025, 1, 3, 12, 0, 0);
        _context.Maquinas.Add(TestDataHelpers.CreateMaquina(id: 1, nombre: "M1"));
        _context.Productos.Add(TestDataHelpers.CreateProducto(id: 1, nombre: "P1"));
        var configuredSlot = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 0);
        configuredSlot.NumeroSlot = "1";
        _context.ConfiguracionSlots.Add(configuredSlot);
        _context.TemplatesRecarga.Add(new TemplateRecarga
        {
            Id = 1,
            Nombre = "T1",
            Periodos = new List<PeriodoRecarga>
            {
                new()
                {
                    Id = 1,
                    MaquinaId = 1,
                    FechaRecarga = start,
                    SnapshotSlots = new List<SnapshotSlot>
                    {
                        new() { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 10 }
                    }
                }
            }
        });
        _context.TemplatesRecarga.Add(new TemplateRecarga
        {
            Id = 2,
            Nombre = "T2",
            Periodos = new List<PeriodoRecarga>
            {
                new() { Id = 2, MaquinaId = 1, FechaRecarga = end }
            }
        });

        var sales = Enumerable.Range(0, 9).Select(hour => start.AddHours(1 + hour)).ToList();
        sales.Add(start.AddDays(2)); // tenth sale: 28 operating hours after start
        sales.Add(start.AddDays(2).AddHours(1));
        sales.Add(start.AddDays(2).AddHours(2));
        foreach (var at in sales)
        {
            _context.Ventas.Add(new Venta
            {
                FechaLocal = at, FechaHora = at, MaquinaId = 1, ProductoId = 1, NumeroSlot = "1",
                PrecioVenta = 1000m, CostoVenta = 400m, Pagado = true, IdOrdenMaquina = "TEST"
            });
        }
        await _context.SaveChangesAsync();

        var slot = (await _service.GetStockoutAnalysisAsync(start, end, 1)).Single();

        slot.FechaAgotamientoEstimada.Should().Be(start.AddDays(2));
        slot.PrimeraVentaPosteriorAlAgotamiento.Should().Be(start.AddDays(2).AddHours(1));
        slot.TieneVentasPosterioresAlAgotamiento.Should().BeTrue();
        slot.VentasOperativasObservadas.Should().Be(10);
        slot.HorasExposicionOperativas.Should().BeApproximately(28, 0.01);
        slot.OrigenVelocidad.Should().Be(OrigenVelocidad.ProductoMaquina);
        slot.VelocidadObservadaSlotPorHora.Should().Be(10m / 28m);
        slot.VelocidadEfectivaPorHora.Should().Be(10m / 28m);
        slot.FinReporte.Should().Be(end);
        slot.DineroPerdidoEstimado.Should().BeApproximately(1000m * 10m / 28m, 0.000001m,
            "the first post-depletion sale caps the conservative loss window at one operating hour");
        slot.UnidadesNoAtendidasEstimadas.Should().BeApproximately(10m / 28m, 0.000001m);

        var templateService = new TemplateRecargaAnalyticsService(
            _context, new Mock<Microsoft.Extensions.Logging.ILogger<TemplateRecargaAnalyticsService>>().Object);
        var templateSlot = (await templateService.AnalyzarPorTemplateAsync(1)).Single();
        templateSlot.FechaAgotamientoEstimada.Should().Be(slot.FechaAgotamientoEstimada);
        templateSlot.PrimeraVentaPosteriorAlAgotamiento.Should().Be(slot.PrimeraVentaPosteriorAlAgotamiento);
        templateSlot.VentasOperativasObservadas.Should().Be(10);
        templateSlot.HorasExposicionOperativas.Should().BeApproximately(28, 0.01);
        templateSlot.FinReporte.Should().Be(end);
        templateSlot.DineroPerdidoEstimado.Should().BeApproximately(slot.DineroPerdidoEstimado, 0.000001m);
        templateSlot.UnidadesNoAtendidasEstimadas.Should().BeApproximately(slot.UnidadesNoAtendidasEstimadas, 0.000001m);
    }

    [Fact]
    public async Task GetStockoutAnalysisAsync_CapsFutureEndAndKeepsSparseAndDeadSlotContracts()
    {
        var nowBefore = DateTime.Now;
        var saleAt = nowBefore.AddMinutes(-30);
        _context.Maquinas.Add(TestDataHelpers.CreateMaquina(id: 1, nombre: "M1"));
        _context.Productos.Add(TestDataHelpers.CreateProducto(id: 1, nombre: "P1"));
        var configured = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 0);
        configured.NumeroSlot = "1";
        _context.ConfiguracionSlots.AddRange(configured, TestDataHelpers.CreateSlot(id: 2, maquinaId: 1, productoId: 1, stockActual: 0));
        _context.TemplatesRecarga.Add(new TemplateRecarga
        {
            Id = 1,
            Nombre = "T1",
            Periodos = [new PeriodoRecarga
            {
                Id = 1,
                MaquinaId = 1,
                FechaRecarga = saleAt.AddMinutes(-1),
                SnapshotSlots = [new SnapshotSlot { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 1 }]
            }]
        });
        _context.Ventas.Add(Sale(saleAt, "1"));
        await _context.SaveChangesAsync();

        var slots = await _service.GetStockoutAnalysisAsync(saleAt.AddMinutes(-1), DateTime.Now.AddDays(2), 1);

        var sold = slots.Single(slot => slot.NumeroSlot == "1");
        sold.FinReporte.Should().BeOnOrAfter(nowBefore).And.BeOnOrBefore(DateTime.Now);
        sold.HorasActivas.Should().Be(1);
        sold.QualityFlags.Should().HaveFlag(StockoutQualityFlags.SparseVelocity);
        sold.EstimateConfidence.Should().Be(EstimateConfidence.Low);
        slots.Single(slot => slot.EsDeadSlot).DineroPerdidoEstimado.Should().Be(0m);
    }

    [Fact]
    public async Task GetStockoutAnalysisAsync_DetectsWeekendPostDepletionSale()
    {
        var friday = new DateTime(2026, 7, 3, 21, 30, 0);
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
                FechaRecarga = friday.AddHours(-1),
                SnapshotSlots = [new SnapshotSlot { NumeroSlot = "1", ProductoId = 1, CantidadInicial = 1 }]
            }]
        });
        _context.Ventas.AddRange(Sale(friday, "1"), Sale(new DateTime(2026, 7, 6, 9, 15, 0), "1"));
        await _context.SaveChangesAsync();

        var slot = (await _service.GetStockoutAnalysisAsync(friday.AddHours(-1), new DateTime(2026, 7, 6, 10, 0, 0), 1)).Single();

        slot.TieneVentasPosterioresAlAgotamiento.Should().BeTrue();
        slot.PrimeraVentaPosteriorAlAgotamiento.Should().Be(new DateTime(2026, 7, 6, 9, 15, 0));
    }

    private static Venta Sale(DateTime at, string slot) => new()
    {
        FechaLocal = at, FechaHora = at, MaquinaId = 1, ProductoId = 1, NumeroSlot = slot,
        PrecioVenta = 1000m, CostoVenta = 400m, Pagado = true, IdOrdenMaquina = "TEST"
    };
}
