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
using VendingManager.Tests.TestData;

/// <summary>
/// DB-dependent tests for per-machine EBITDA:
///   Task 2.3 — CalcularDepreciacionAsync
///   Task 2.4 — CalcularOpexPorMaquinaAsync
///
/// Daily depreciation rate: (ValorAdquisicion - ValorResidual) / VidaUtilMeses / 30.4167
/// </summary>
public class SalesAnalyticsService_EbitdaDb_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly SalesAnalyticsService _analyticsService;

    public SalesAnalyticsService_EbitdaDb_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"EbitdaDb_{Guid.NewGuid()}");
        var mockExcel = new Mock<IExcelExportService>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var thresholds = Options.Create(AnalyticsThresholds.Default);
        var config = Options.Create(new VendingConfig());
        _analyticsService = new SalesAnalyticsService(
            _context, mockExcel.Object, cache, thresholds, config);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // =========================================================================
    // TASK 2.3 — CalcularDepreciacionAsync
    // =========================================================================

    /// <summary>
    /// Spec: Full-month depreciation for Jan 2025 (31 days).
    /// ValorAdquisicion=2,000,000, ValorResidual=200,000, VidaUtilMeses=36.
    /// dailyRate = (2,000,000 - 200,000) / 36 / 30.4167 ≈ 1,643.8833
    /// depreciation = 1,643.8833 × 31 ≈ 50,960.38
    /// </summary>
    [Fact]
    public async Task CalcularDepreciacion_FullMonth_ReturnsCorrectAmount()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1, Nombre = "M1",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = null
        });
        _context.DepreciacionesMaquina.Add(new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "CAPEX M1",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        });
        await _context.SaveChangesAsync();

        // Act
        decimal result = await _analyticsService.CalcularDepreciacionAsync(
            1, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        // Assert: dailyRate = 1,800,000 / 36 / 30.4167 ≈ 1,643.8833
        // 31 days: 1,643.8833 × 31 ≈ 50,960.38
        decimal expectedDailyRate = (2_000_000m - 200_000m) / 36m / 30.4167m;
        decimal expected = expectedDailyRate * 31m;
        result.Should().BeApproximately(expected, 0.01m,
            "31 full days × daily rate");
    }

    /// <summary>
    /// Spec: Partial-month installation (Jan 15 → 17 days).
    /// </summary>
    [Fact]
    public async Task CalcularDepreciacion_PartialInstall_ReturnsCorrectAmount()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 2, Nombre = "M2",
            FechaInstalacion = new DateTime(2025, 1, 15),
            FechaBaja = null
        });
        _context.DepreciacionesMaquina.Add(new DepreciacionMaquina
        {
            MaquinaId = 2,
            Descripcion = "CAPEX M2",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 15),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        });
        await _context.SaveChangesAsync();

        decimal result = await _analyticsService.CalcularDepreciacionAsync(
            2, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        decimal expectedDailyRate = (2_000_000m - 200_000m) / 36m / 30.4167m;
        decimal expected = expectedDailyRate * 17m; // 17 days
        result.Should().BeApproximately(expected, 0.01m,
            "17 operational days (Jan 15–31)");
    }

    /// <summary>
    /// Spec: Partial-month baja (Mar 10 → 10 days).
    /// </summary>
    [Fact]
    public async Task CalcularDepreciacion_PartialBaja_ReturnsCorrectAmount()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 3, Nombre = "M3",
            FechaInstalacion = new DateTime(2024, 6, 1),
            FechaBaja = new DateTime(2025, 3, 10)
        });
        _context.DepreciacionesMaquina.Add(new DepreciacionMaquina
        {
            MaquinaId = 3,
            Descripcion = "CAPEX M3",
            ValorAdquisicion = 1_500_000m,
            ValorResidual = 150_000m,
            VidaUtilMeses = 24,
            FechaAdquisicion = new DateTime(2024, 6, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        });
        await _context.SaveChangesAsync();

        decimal result = await _analyticsService.CalcularDepreciacionAsync(
            3, new DateTime(2025, 3, 1), new DateTime(2025, 3, 31));

        decimal expectedDailyRate = (1_500_000m - 150_000m) / 24m / 30.4167m;
        decimal expected = expectedDailyRate * 10m; // 10 days
        result.Should().BeApproximately(expected, 0.01m,
            "10 operational days (Mar 1–10)");
    }

    /// <summary>
    /// Spec: Both-ended month (Jun 5–20 → 16 days, installed+baja same month).
    /// </summary>
    [Fact]
    public async Task CalcularDepreciacion_BothEnded_ReturnsCorrectAmount()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 4, Nombre = "M4",
            FechaInstalacion = new DateTime(2025, 6, 5),
            FechaBaja = new DateTime(2025, 6, 20)
        });
        _context.DepreciacionesMaquina.Add(new DepreciacionMaquina
        {
            MaquinaId = 4,
            Descripcion = "CAPEX M4",
            ValorAdquisicion = 3_000_000m,
            ValorResidual = 300_000m,
            VidaUtilMeses = 48,
            FechaAdquisicion = new DateTime(2025, 6, 5),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        });
        await _context.SaveChangesAsync();

        decimal result = await _analyticsService.CalcularDepreciacionAsync(
            4, new DateTime(2025, 6, 1), new DateTime(2025, 6, 30));

        decimal expectedDailyRate = (3_000_000m - 300_000m) / 48m / 30.4167m;
        decimal expected = expectedDailyRate * 16m; // 16 days
        result.Should().BeApproximately(expected, 0.01m,
            "16 operational days (Jun 5–20)");
    }

    /// <summary>
    /// Spec: Inactive machine (baja before range → 0 operational days → $0).
    /// </summary>
    [Fact]
    public async Task CalcularDepreciacion_InactiveMachine_ReturnsZero()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 5, Nombre = "M5",
            FechaInstalacion = new DateTime(2024, 1, 1),
            FechaBaja = new DateTime(2024, 12, 31)
        });
        _context.DepreciacionesMaquina.Add(new DepreciacionMaquina
        {
            MaquinaId = 5,
            Descripcion = "CAPEX M5",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 0,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2024, 1, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        });
        await _context.SaveChangesAsync();

        decimal result = await _analyticsService.CalcularDepreciacionAsync(
            5, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        result.Should().Be(0m, "machine inactive during range → 0 depreciation");
    }

    /// <summary>
    /// Spec: No DepreciacionMaquina rows → $0.
    /// </summary>
    [Fact]
    public async Task CalcularDepreciacion_NoRows_ReturnsZero()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 6, Nombre = "M6",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = null
        });
        await _context.SaveChangesAsync();

        decimal result = await _analyticsService.CalcularDepreciacionAsync(
            6, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        result.Should().Be(0m, "no DepreciacionMaquina rows → $0 depreciation");
    }

    // =========================================================================
    // TASK 2.4 — CalcularOpexPorMaquinaAsync
    // =========================================================================

    /// <summary>
    /// Direct OPEX: machine 1 has -$50K internet (INFRA = fixed).
    /// Fleet has no OPEX. Expected: GastosFijos ≈ 50,000, GastosVariables = 0.
    /// </summary>
    [Fact]
    public async Task CalcularOpex_DirectOnly()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 1, Nombre = "M1",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = null
        });
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2025, 1, 15),
            Descripcion = "Internet M1",
            Monto = -50_000m,
            Tipo = "GASTO",
            Categoria = "INFRA",
            MaquinaId = 1
        });
        await _context.SaveChangesAsync();

        var (gastosFijos, gastosVariables) = await _analyticsService.CalcularOpexPorMaquinaAsync(
            1, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        gastosFijos.Should().Be(50_000m, "INFRA is fixed → 50K direct");
        gastosVariables.Should().Be(0m, "no variable OPEX seeded");
    }

    /// <summary>
    /// Fleet proration: 2 machines, M1 has 31 days, M2 has 17 days (total 48).
    /// Fleet OPEX = -$100K INFRA (fixed).
    /// M1 share = 100,000 × 31/48 ≈ 64,583.33.
    /// </summary>
    [Fact]
    public async Task CalcularOpex_FleetProration()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 1, Nombre = "M1",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = null
        });
        _context.Maquinas.Add(new Maquina
        {
            Id = 2, Nombre = "M2",
            FechaInstalacion = new DateTime(2025, 1, 15),
            FechaBaja = null
        });
        // Fleet-level OPEX (MaquinaId = null)
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2025, 1, 10),
            Descripcion = "Admin general",
            Monto = -100_000m,
            Tipo = "GASTO",
            Categoria = "INFRA",
            MaquinaId = null
        });
        await _context.SaveChangesAsync();

        var (gastosFijos, gastosVariables) = await _analyticsService.CalcularOpexPorMaquinaAsync(
            1, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        // M1: 31/48 = 0.645833... × 100,000 = 64,583.33...
        decimal expectedShare = 100_000m * 31m / 48m;
        gastosFijos.Should().BeApproximately(expectedShare, 0.01m,
            "Fleet INFRA prorated by operational days");
        gastosVariables.Should().Be(0m, "no variable OPEX");
    }

    /// <summary>
    /// Zero operational days guard: all machines inactive → fleet OPEX = 0.
    /// Machine has FechaBaja before the range start.
    /// </summary>
    [Fact]
    public async Task CalcularOpex_ZeroOpDaysGuard()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 1, Nombre = "M1",
            FechaInstalacion = new DateTime(2024, 1, 1),
            FechaBaja = new DateTime(2024, 12, 31)  // inactive for Jan 2025
        });
        _context.Maquinas.Add(new Maquina
        {
            Id = 2, Nombre = "M2",
            FechaInstalacion = new DateTime(2025, 3, 1),  // installed after range
            FechaBaja = null
        });
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2025, 1, 15),
            Descripcion = "Fleet OPEX",
            Monto = -100_000m,
            Tipo = "GASTO",
            Categoria = "INFRA",
            MaquinaId = null
        });
        await _context.SaveChangesAsync();

        var (gastosFijos, gastosVariables) = await _analyticsService.CalcularOpexPorMaquinaAsync(
            1, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        // Both machines have 0 operational days → fleet share should be 0
        gastosFijos.Should().Be(0m, "zero operational days → fleet OPEX = 0");
        gastosVariables.Should().Be(0m);
    }

    /// <summary>
    /// Category classification: LOGISTICA counts as variable, INFRA as fixed.
    /// Direct OPEX for M1: -$30K LOGISTICA (variable) + -$40K INFRA (fixed).
    /// </summary>
    [Fact]
    public async Task CalcularOpex_FixedVsVariable()
    {
        _context.Maquinas.Add(new Maquina
        {
            Id = 1, Nombre = "M1",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = null
        });
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2025, 1, 10),
            Descripcion = "Logística M1",
            Monto = -30_000m,
            Tipo = "GASTO",
            Categoria = "LOGISTICA",
            MaquinaId = 1
        });
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2025, 1, 20),
            Descripcion = "Internet M1",
            Monto = -40_000m,
            Tipo = "GASTO",
            Categoria = "INFRA",
            MaquinaId = 1
        });
        // Non-operational category — must be excluded
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2025, 1, 15),
            Descripcion = "Merma",
            Monto = -999_999m,
            Tipo = "GASTO",
            Categoria = "MERMA",
            MaquinaId = 1
        });
        await _context.SaveChangesAsync();

        var (gastosFijos, gastosVariables) = await _analyticsService.CalcularOpexPorMaquinaAsync(
            1, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        gastosFijos.Should().Be(40_000m, "INFRA is fixed → 40K");
        gastosVariables.Should().Be(30_000m, "LOGISTICA is variable → 30K");
    }

    // =========================================================================
    // TASK 2.5 — GetInformeFinancieroAsync per-machine branch
    // =========================================================================

    /// <summary>
    /// Integration: per-machine report returns all 4 new fields with
    /// computed values > 0. Seeds a sale, OPEX, and depreciation for machine 1.
    ///
    /// Machine 1: installed Jan 1, CAPEX 2M, 36 months, 200K residual.
    /// Daily rate = (2M - 200K) / 36 / 30.4167 ≈ 1,643.88
    /// 31 days in Jan → depreciation ≈ 50,960.38
    ///
    /// Sale: $5,000 revenue, $2,000 cost → margin $3,000
    /// Direct OPEX: -$30,000 INFRA (fixed)
    ///
    /// Ebitda = 3,000 - 30,000 - 50,960.38 ≈ -77,960.38
    /// </summary>
    [Fact]
    public async Task GetInformeFinanciero_PerMachine_ReturnsAllNewFields()
    {
        // Arrange
        _context.Maquinas.Add(new Maquina
        {
            Id = 1, Nombre = "M1",
            FechaInstalacion = new DateTime(2025, 1, 1),
            FechaBaja = null
        });
        _context.DepreciacionesMaquina.Add(new DepreciacionMaquina
        {
            MaquinaId = 1,
            Descripcion = "CAPEX M1",
            ValorAdquisicion = 2_000_000m,
            ValorResidual = 200_000m,
            VidaUtilMeses = 36,
            FechaAdquisicion = new DateTime(2025, 1, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        });
        // Direct OPEX for M1
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2025, 1, 15),
            Descripcion = "Internet M1",
            Monto = -30_000m,
            Tipo = "GASTO",
            Categoria = "INFRA",
            MaquinaId = 1
        });
        // A sale
        _context.Productos.Add(new Core.Entities.Producto
        {
            Id = 1, Nombre = "Test Product", CostoPromedio = 2000m, SKU = "TST-001"
        });
        _context.Ventas.Add(new Core.Entities.Venta
        {
            FechaLocal = new DateTime(2025, 1, 15),
            FechaHora = new DateTime(2025, 1, 15, 10, 0, 0),
            PrecioVenta = 5000m,
            CostoVenta = 2000m,
            Pagado = true,
            MaquinaId = 1,
            ProductoId = 1,
            NumeroSlot = "1",
            IdOrdenMaquina = "TST-001"
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            new DateTime(2025, 1, 1), new DateTime(2025, 1, 31), 1);

        // Assert
        result.VentasTotales.Should().Be(5000m);
        result.CostoVentas.Should().Be(2000m);
        result.MargenBruto.Should().Be(3000m);

        // OPEX: INFRA is fixed → GastosFijos
        result.GastosFijos.Should().BeGreaterThan(0, "direct OPEX should produce non-zero GastosFijos");
        result.GastosVariables.Should().Be(0, "no variable OPEX seeded");

        result.GastosOperativos.Should().Be(result.GastosFijos + result.GastosVariables);

        // Depreciation > 0
        result.DepreciacionPeriodo.Should().BeGreaterThan(0, "active machine with CAPEX should have depreciation");

        // Ebitda = MargenBruto - GastosOperativos - DepreciacionPeriodo
        result.Ebitda.Should().Be(result.MargenBruto - result.GastosOperativos - result.DepreciacionPeriodo);
        result.UtilidadNeta.Should().Be(result.Ebitda);
    }

    /// <summary>
    /// Backward compat: fleet-level (maquinaId=0) produces same existing fields
    /// and Ebitda = UtilidadNeta. New fields present but zeroed for fleet.
    /// </summary>
    [Fact]
    public async Task GetInformeFinanciero_FleetLevel_BackwardCompat()
    {
        // Arrange: seed a sale and operational gasto
        _context.Productos.Add(new Core.Entities.Producto
        {
            Id = 1, Nombre = "Product", CostoPromedio = 300m, SKU = "TST-002"
        });
        _context.Ventas.Add(new Core.Entities.Venta
        {
            FechaLocal = new DateTime(2026, 6, 30),
            FechaHora = new DateTime(2026, 6, 30, 10, 0, 0),
            PrecioVenta = 2000m,
            CostoVenta = 600m,
            Pagado = true,
            MaquinaId = 1,
            ProductoId = 1,
            NumeroSlot = "1",
            IdOrdenMaquina = "TST-002"
        });
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2026, 6, 30),
            Descripcion = "Fleet OPEX",
            Monto = -500m,
            Tipo = "GASTO",
            Categoria = "LOGISTICA",
            MaquinaId = null
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            new DateTime(2026, 6, 29), new DateTime(2026, 7, 1), 0);

        // Assert — existing fields unchanged
        result.VentasTotales.Should().Be(2000m);
        result.CostoVentas.Should().Be(600m);
        result.MargenBruto.Should().Be(1400m);
        result.MargenPorcentaje.Should().Be(70m);

        // Fleet-level: GastosOperativos = 500 (LOGISTICA)
        result.GastosOperativos.Should().Be(500m);

        // New fields: zero at fleet level (no per-machine breakdown)
        result.GastosFijos.Should().Be(0m);
        result.GastosVariables.Should().Be(0m);
        result.DepreciacionPeriodo.Should().Be(0m);

        // Ebitda = UtilidadNeta at fleet level
        result.UtilidadNeta.Should().Be(1400m - 500m); // 900
        result.Ebitda.Should().Be(result.UtilidadNeta,
            "at fleet level, Ebitda equals UtilidadNeta (no per-machine breakdown)");
    }
}
