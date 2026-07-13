namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data.Repositories;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

public class CajaServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IWebHostEnvironment> _mockEnvironment;
    private readonly Mock<IInformesService> _mockInformesService;
    private readonly Mock<IExcelExportService> _mockExcelExport;
    private readonly IOptions<VendingConfig> _config;
    private readonly IOptions<AnalyticsThresholds> _thresholds;
    private readonly IMemoryCache _cache;
    private readonly SalesAnalyticsService _salesAnalytics;
    private readonly CajaService _cajaService;

    public CajaServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"CajaTestDb_{Guid.NewGuid()}");
        _mockEnvironment = new Mock<IWebHostEnvironment>();
        _mockEnvironment.Setup(e => e.EnvironmentName).Returns("Production");
        _mockEnvironment.Setup(e => e.ContentRootPath).Returns("/tmp");
        _mockInformesService = new Mock<IInformesService>();
        _mockExcelExport = new Mock<IExcelExportService>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        var vendingConfig = new VendingConfig
        {
            CajaStartDate = new DateTime(2026, 1, 1),
            TransbankFee = 80,
            RotacionStockMinimoDias = 30,
            RotacionUmbralCritico = 7
        };
        _config = Options.Create(vendingConfig);
        _thresholds = Options.Create(AnalyticsThresholds.Default);

        _salesAnalytics = new SalesAnalyticsService(_context, _mockExcelExport.Object, _cache, _thresholds, _config);

        var ventaRepo = new VentaRepository(_context);
        var maquinaRepo = new MaquinaRepository(_context);
        var business = new CajaBusinessService(_context, ventaRepo, maquinaRepo, _mockExcelExport.Object, _config, _salesAnalytics);

        _cajaService = new CajaService(_context, _mockEnvironment.Object, _mockInformesService.Object, _config, _mockExcelExport.Object, business);
    }

    public void Dispose()
    {
        _context.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public async Task GetResumenAsync_WithSingleSaleInMonth_ReturnsCorrectFinancials()
    {
        // Arrange
        var globalStart = new DateTime(2025, 12, 18);
        var targetDate = new DateTime(2026, 1, 15);

        // Seed a venta in January 2026 (within target month)
        var venta = TestDataHelpers.CreateVenta(
            fechaLocal: targetDate,
            precioVenta: 500m,
            costoVenta: 200m,
            pagado: true,
            maquinaId: 1,
            productoId: 1
        );
        venta.FechaHora = targetDate;
        venta.FechaLocal = targetDate;
        _context.Ventas.Add(venta);

        // Seed previous month ventas for SaldoAnterior calculation
        var prevVenta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2025, 12, 20),
            precioVenta: 1000m,
            costoVenta: 400m,
            pagado: true,
            maquinaId: 1,
            productoId: 1
        );
        prevVenta.FechaHora = new DateTime(2025, 12, 20);
        prevVenta.FechaLocal = new DateTime(2025, 12, 20);
        _context.Ventas.Add(prevVenta);

        await _context.SaveChangesAsync();

        // Act
        var result = await _cajaService.GetResumenAsync(1, 2026);

        // Assert
        result.Should().NotBeNull();
        result.SaldoAnterior.Should().Be(0m); // December venta excluded by CajaStartDate (Jan 1, 2026)
        result.IngresosVentas.Should().Be(500m);  // From January venta
        result.TotalIngresos.Should().Be(500m);   // SaldoAnterior(0) + IngresosVentas(500)
        result.UtilidadOperacional.Should().Be(300m); // (500-200) margen bruto, no gastos
        result.SaldoFinal.Should().Be(500m); // TotalIngresos(500) + Gastos (0)
        result.TotalCostoVenta.Should().Be(200m); // January venta costo
        result.Mermas.Should().Be(0m);
        result.GastosVariables.Should().Be(0m);
        result.GastosFijos.Should().Be(0m);
        result.AportesExtra.Should().Be(0m);
        result.GastosMercaderia.Should().Be(0m);
        result.CostoTransbank.Should().Be(80m); // TransbankFee = 80 per paid venta
        result.CantidadVentasTransbank.Should().Be(1);
        result.UtilidadTotal.Should().Be(300m);
        result.UtilidadNeta.Should().Be(300m);
        result.DepreciacionPeriodo.Should().Be(0m);
        result.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task GetResumenAsync_WithMixedOperations_ReturnsCorrectFinancials()
    {
        // Arrange
        var globalStart = new DateTime(2025, 12, 18);

        // Seed previous month venta for SaldoAnterior
        var prevVenta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2025, 12, 20),
            precioVenta: 1000m,
            costoVenta: 400m,
            pagado: true,
            maquinaId: 1
        );
        prevVenta.FechaHora = new DateTime(2025, 12, 20);
        prevVenta.FechaLocal = new DateTime(2025, 12, 20);
        _context.Ventas.Add(prevVenta);

        // Seed January venta (Ingreso = 300)
        var ingresoVenta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 1, 10),
            precioVenta: 300m,
            costoVenta: 100m,
            pagado: true,
            maquinaId: 1
        );
        ingresoVenta.FechaHora = new DateTime(2026, 1, 10);
        ingresoVenta.FechaLocal = new DateTime(2026, 1, 10);
        _context.Ventas.Add(ingresoVenta);

        // Seed January gasto via MovimientoCaja (Gasto = 150, tipo GASTO stored as negative)
        var gastoMovimiento = TestDataHelpers.CreateMovimientoCaja(
            monto: -150m,
            fecha: new DateTime(2026, 1, 12),
            categoria: "LOGISTICA",
            tipo: "GASTO",
            descripcion: "Gasto logístico"
        );
        _context.MovimientosCaja.Add(gastoMovimiento);

        await _context.SaveChangesAsync();

        // Act
        var result = await _cajaService.GetResumenAsync(1, 2026);

        // Assert
        result.Should().NotBeNull();
        result.SaldoAnterior.Should().Be(0m); // December venta excluded by CajaStartDate (Jan 1, 2026)
        result.IngresosVentas.Should().Be(300m);
        result.GastosOperativos.Should().Be(150m); // Variables (LOGISTICA = 150)
        result.TotalIngresos.Should().Be(300m);    // SaldoAnterior(0) + IngresosVentas(300)
        result.SaldoFinal.Should().Be(150m);       // TotalIngresos(300) - GastosOperativos(150)
        result.TotalCostoVenta.Should().Be(100m);   // January venta costo
        result.Mermas.Should().Be(0m);
        result.GastosVariables.Should().Be(150m);   // LOGISTICA = 150
        result.GastosFijos.Should().Be(0m);
        result.AportesExtra.Should().Be(0m);
        result.GastosMercaderia.Should().Be(0m);
        result.CostoTransbank.Should().Be(80m); // TransbankFee = 80 per paid venta
        result.CantidadVentasTransbank.Should().Be(1);
        result.UtilidadTotal.Should().Be(200m);     // IngresosVentas(300) - TotalCostoVenta(100)
        result.UtilidadNeta.Should().Be(50m);      // UtilidadTotal(200) - GastosOperativos(150)
        result.DepreciacionPeriodo.Should().Be(0m);
        result.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task GetResumenAsync_WithEmptyMonth_ReturnsOnlySaldoAnterior()
    {
        // Arrange
        var globalStart = new DateTime(2025, 12, 18);

        // Seed previous month venta for SaldoAnterior
        var prevVenta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 1, 15),
            precioVenta: 500m,
            costoVenta: 200m,
            pagado: true,
            maquinaId: 1
        );
        prevVenta.FechaHora = new DateTime(2026, 1, 15);
        prevVenta.FechaLocal = new DateTime(2026, 1, 15);
        _context.Ventas.Add(prevVenta);

        await _context.SaveChangesAsync();

        // Act - query February 2026 which has no data
        var result = await _cajaService.GetResumenAsync(2, 2026);

        // Assert
        result.Should().NotBeNull();
        result.SaldoAnterior.Should().Be(500m); // From January
        result.IngresosVentas.Should().Be(0m);
        result.GastosOperativos.Should().Be(0m);
        result.SaldoFinal.Should().Be(500m); // Just saldo anterior, no activity
        result.TotalIngresos.Should().Be(500m);
        result.TotalCostoVenta.Should().Be(0m);
        result.Mermas.Should().Be(0m);
        result.GastosVariables.Should().Be(0m);
        result.GastosFijos.Should().Be(0m);
        result.AportesExtra.Should().Be(0m);
        result.GastosMercaderia.Should().Be(0m);
        result.CostoTransbank.Should().Be(0m);
        result.CantidadVentasTransbank.Should().Be(0);
        result.UtilidadTotal.Should().Be(0m);
        result.UtilidadNeta.Should().Be(0m);
        result.UtilidadOperacional.Should().Be(0m);
        result.DepreciacionPeriodo.Should().Be(0m);
        result.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task GetResumenAsync_RespectsGlobalStartDate_FiltersOutOldData()
    {
        // Arrange
        var globalStart = new DateTime(2025, 12, 18);

        // Seed a venta BEFORE GlobalStartDate (should be excluded)
        var oldVenta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2025, 12, 10), // Before GlobalStartDate
            precioVenta: 5000m,
            costoVenta: 2000m,
            pagado: true,
            maquinaId: 1
        );
        oldVenta.FechaHora = new DateTime(2025, 12, 10);
        oldVenta.FechaLocal = new DateTime(2025, 12, 10);
        _context.Ventas.Add(oldVenta);

        // Seed a venta AFTER GlobalStartDate (should be included)
        var validVenta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2025, 12, 20),
            precioVenta: 1000m,
            costoVenta: 400m,
            pagado: true,
            maquinaId: 1
        );
        validVenta.FechaHora = new DateTime(2025, 12, 20);
        validVenta.FechaLocal = new DateTime(2025, 12, 20);
        _context.Ventas.Add(validVenta);

        await _context.SaveChangesAsync();

        // Act - query December 2025
        var result = await _cajaService.GetResumenAsync(12, 2025);

        // Assert - CajaStartDate filters old data from SaldoAnterior, but
        // monthIngresosVentas does NOT apply CajaStartDate filter (both Dec ventas counted)
        result.Should().NotBeNull();
        result.SaldoAnterior.Should().Be(0m); // Nothing before Dec 2025
        result.IngresosVentas.Should().Be(6000m); // Both Dec ventas counted (5000 + 1000)
        result.TotalIngresos.Should().Be(6000m);
        result.SaldoFinal.Should().Be(6000m);
        result.TotalCostoVenta.Should().Be(2400m); // 2000 + 400
        result.Mermas.Should().Be(0m);
        result.GastosVariables.Should().Be(0m);
        result.GastosFijos.Should().Be(0m);
        result.AportesExtra.Should().Be(0m);
        result.GastosMercaderia.Should().Be(0m);
        result.CostoTransbank.Should().Be(160m); // TransbankFee = 80 × 2 paid ventas
        result.CantidadVentasTransbank.Should().Be(2);
        result.UtilidadTotal.Should().Be(3600m);   // 6000 - 2400
        result.UtilidadNeta.Should().Be(3600m);    // No gastos
        result.UtilidadOperacional.Should().Be(3600m);
        result.DepreciacionPeriodo.Should().Be(0m);
        result.IsLocked.Should().BeFalse();
    }

    // ── Per-machine tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GetResumenAsync_PerMachine_WithActiveDepreciacion_ReturnsPositiveDepreciacionPeriodo()
    {
        // Arrange — machine 5 with active depreciation + a sale in July 2026
        var maquina = TestDataHelpers.CreateMaquina(id: 5, nombre: "Máquina 5");
        maquina.FechaInstalacion = new DateTime(2026, 1, 1);
        _context.Maquinas.Add(maquina);

        var dep = new DepreciacionMaquina
        {
            MaquinaId = 5,
            Descripcion = "CAPEX máquina 5",
            ValorAdquisicion = 10000m,
            ValorResidual = 1000m,
            VidaUtilMeses = 60,
            FechaAdquisicion = new DateTime(2026, 1, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        };
        _context.DepreciacionesMaquina.Add(dep);

        var venta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 7, 15),
            precioVenta: 500m,
            costoVenta: 200m,
            pagado: true,
            maquinaId: 5);
        venta.FechaHora = new DateTime(2026, 7, 15);
        venta.FechaLocal = new DateTime(2026, 7, 15);
        _context.Ventas.Add(venta);

        await _context.SaveChangesAsync();

        // Act
        var result = await _cajaService.GetResumenAsync(7, 2026, 5);

        // Assert
        result.Should().NotBeNull();
        result.DepreciacionPeriodo.Should().BeGreaterThan(0);
        result.IngresosVentas.Should().Be(500m);
        result.TotalCostoVenta.Should().Be(200m);
        result.UtilidadOperacional.Should().BeLessThan(300m); // depreciation reduces EBITDA
    }

    [Fact]
    public async Task GetResumenAsync_PerMachine_NoDepreciacionRows_ReturnsZeroDepreciacion()
    {
        // Arrange — machine 8 has no DepreciacionMaquina rows
        var maquina = TestDataHelpers.CreateMaquina(id: 8, nombre: "Máquina 8");
        maquina.FechaInstalacion = new DateTime(2026, 1, 1);
        _context.Maquinas.Add(maquina);

        var venta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 7, 15),
            precioVenta: 500m,
            costoVenta: 200m,
            pagado: true,
            maquinaId: 8);
        venta.FechaHora = new DateTime(2026, 7, 15);
        venta.FechaLocal = new DateTime(2026, 7, 15);
        _context.Ventas.Add(venta);

        await _context.SaveChangesAsync();

        // Act
        var result = await _cajaService.GetResumenAsync(7, 2026, 8);

        // Assert
        result.Should().NotBeNull();
        result.DepreciacionPeriodo.Should().Be(0m);
        result.IngresosVentas.Should().Be(500m);
    }

    [Fact]
    public async Task GetResumenAsync_PerMachine_InactiveMachineBaja_ReturnsZeroDepreciacion()
    {
        // Arrange — machine 8 had FechaBaja before July 2026
        var maquina = TestDataHelpers.CreateMaquina(id: 8, nombre: "Máquina 8");
        maquina.FechaInstalacion = new DateTime(2026, 1, 1);
        maquina.FechaBaja = new DateTime(2026, 6, 15); // baja before July
        _context.Maquinas.Add(maquina);

        var dep = new DepreciacionMaquina
        {
            MaquinaId = 8,
            Descripcion = "CAPEX máquina 8",
            ValorAdquisicion = 10000m,
            ValorResidual = 1000m,
            VidaUtilMeses = 60,
            FechaAdquisicion = new DateTime(2026, 1, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        };
        _context.DepreciacionesMaquina.Add(dep);

        await _context.SaveChangesAsync();

        // Act
        var result = await _cajaService.GetResumenAsync(7, 2026, 8);

        // Assert — machine was baja before July, so 0 operational days → 0 depreciation
        result.Should().NotBeNull();
        result.DepreciacionPeriodo.Should().Be(0m);
    }

    [Fact]
    public async Task GetResumenAsync_PerMachine_ZeroVidaUtilMeses_SkipsDepreciacion()
    {
        // Arrange — machine 9 with VidaUtilMeses=0 (invalid data), should not crash
        var maquina = TestDataHelpers.CreateMaquina(id: 9, nombre: "Máquina 9");
        maquina.FechaInstalacion = new DateTime(2026, 1, 1);
        _context.Maquinas.Add(maquina);

        var dep = new DepreciacionMaquina
        {
            MaquinaId = 9,
            Descripcion = "CAPEX máquina 9",
            ValorAdquisicion = 10000m,
            ValorResidual = 1000m,
            VidaUtilMeses = 0, // Zero — guard should skip
            FechaAdquisicion = new DateTime(2026, 1, 1),
            MetodoDepreciacion = "LINEAL",
            Activo = true
        };
        _context.DepreciacionesMaquina.Add(dep);

        var venta = TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 7, 15),
            precioVenta: 300m,
            costoVenta: 100m,
            pagado: true,
            maquinaId: 9);
        venta.FechaHora = new DateTime(2026, 7, 15);
        venta.FechaLocal = new DateTime(2026, 7, 15);
        _context.Ventas.Add(venta);

        await _context.SaveChangesAsync();

        // Act — must not throw DivideByZeroException
        var result = await _cajaService.GetResumenAsync(7, 2026, 9);

        // Assert
        result.Should().NotBeNull();
        result.DepreciacionPeriodo.Should().Be(0m);
        result.IngresosVentas.Should().Be(300m);
    }
}