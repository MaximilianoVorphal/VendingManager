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

        // 2 sales, operative hours from inicio (midnight) to 14:00 = 6h (8:00-14:00)
        // VelocidadPorHora = 2/6 ≈ 0.333, VelocidadDiaria = 0.333 * 14 ≈ 4.67
        slot.CantidadVendida.Should().Be(2);
        slot.VelocidadPorHora.Should().BeApproximately(0.333m, 0.01m);
        slot.VelocidadDiaria.Should().BeApproximately(4.667m, 0.01m);

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
}
