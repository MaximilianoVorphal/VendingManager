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
using VendingManager.Shared.Constants;
using VendingManager.Tests.TestData;

/// <summary>
/// Tests for SalesAnalyticsService.GetInformeFinancieroAsync.
///
/// CALCULATION CONTRACT (post-fix):
///   - Sales included: Pagado=true AND IdOrdenMaquina NOT IN ('TB-EXTRA', 'TB-SIN-VENTA')
///     (i.e., only confirmed real sales; phantoms excluded)
///   - CostoVentas: v.CostoVenta, fallback to v.Producto.CostoPromedio if CostoVenta == 0
///   - GastosOperativos (only when maquinaId == 0):
///       sum of MovimientosCaja where:
///         - Fecha >= CajaStartDate
///         - Fecha in [inicio, fin]
///         - Monto < 0
///         - Categoria in [LOGISTICA, PEAJES, INSUMOS, MANTENCION,
///                          INFRA, ARRIENDO_POS, INTERNET, COMISIONES,
///                          SUELDOS, GASTOS GENERALES, OTROS, SERVICIOS]
///       then Math.Abs of the (negative) sum
///   - UtilidadNeta = MargenBruto - GastosOperativos
///   - MargenPorcentaje = (MargenBruto / VentasTotales) * 100
///
/// Categories NOT counted as GastosOperativos: GENERAL, MERCADERIA, MERMA, LOTES, APORTE.
/// Same filter array as CajaBusinessService.GetResumenAsync (lines 88-98).
/// </summary>
public class SalesAnalyticsService_GetInformeFinancieroAsync_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IExcelExportService> _mockExcelExport;
    private readonly IMemoryCache _cache;
    private readonly IOptions<AnalyticsThresholds> _thresholds;
    private readonly IOptions<VendingConfig> _config;
    private readonly SalesAnalyticsService _analyticsService;

    public SalesAnalyticsService_GetInformeFinancieroAsync_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"FinancieroTestDb_{Guid.NewGuid()}");
        _mockExcelExport = new Mock<IExcelExportService>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _thresholds = Options.Create(AnalyticsThresholds.Default);
        // CajaStartDate = 2026-01-01 default — matches VendingConfig.cs:5
        _config = Options.Create(new VendingConfig());
        _analyticsService = new SalesAnalyticsService(
            _context, _mockExcelExport.Object, _cache, _thresholds, _config);
    }

    public void Dispose()
    {
        _context.Dispose();
        _cache.Dispose();
    }

    // =========================================================================
    // TEST 1 — RED: Phantoms (TB-EXTRA, TB-SIN-VENTA) must be excluded.
    //
    // Phantoms are Pagado=true with CostoVenta=0 and Producto=null
    // (SalesImportService.cs:370-381 for TB-SIN-VENTA). They DO satisfy
    // the v.Pagado filter, so without an explicit exclusion they pollute
    // the financial KPI with 100%-margin ghost revenue.
    //
    // RED: VentasTotales = 5000 (phantom only), MargenPorcentaje = 100%
    // GREEN: VentasTotales = 1000 (real sale), MargenPorcentaje = 60%
    // =========================================================================
    [Fact]
    public async Task GetInformeFinancieroAsync_WithPhantoms_ShouldExcludeThem_RED()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine A");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(
            id: 1, nombre: "Real Product", costoPromedio: 400m);
        _context.Productos.Add(producto);

        // Real sale — Pagado=true (post-Transbank reconciliation)
        _context.Ventas.Add(TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 6, 30, 10, 0, 0),
            precioVenta: 1000m,
            costoVenta: 400m,
            pagado: true,           // ← Pagado=true → should be included
            maquinaId: 1,
            productoId: 1
        ));

        // Phantom — Pagado=true, CostoVenta=0, Producto=null
        // (Matches SalesImportService.cs:370-381 for TB-SIN-VENTA)
        _context.Ventas.Add(new Venta
        {
            FechaHora = new DateTime(2026, 6, 30, 12, 0, 0),
            FechaLocal = new DateTime(2026, 6, 30, 12, 0, 0),
            PrecioVenta = 5000m,
            Pagado = true,
            NumeroSlot = "ERR",
            IdOrdenMaquina = VentaConstants.TbSinVenta,
            ProductoId = null,
            CostoVenta = 0,
            MaquinaId = 1
        });

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            inicio: new DateTime(2026, 6, 29),
            fin: new DateTime(2026, 7, 1),
            maquinaId: 1);

        // Assert
        result.Should().NotBeNull();

        // RED (current code): includes phantom + excludes real sale
        //   VentasTotales = 5000, CostoVentas = 0, MargenPorcentaje = 100%
        //
        // GREEN (after fix): only the real sale counts
        result.VentasTotales.Should().Be(1000m, "VentasTotales debe excluir phantoms");
        result.CostoVentas.Should().Be(400m,    "CostoVentas debe reflejar solo la venta real");
        result.MargenBruto.Should().Be(600m,    "MargenBruto = 1000 - 400");
        result.MargenPorcentaje.Should().Be(60m, "MargenPorcentaje = (600/1000)*100 = 60% (no 100%)");
    }

    // =========================================================================
    // TEST 2 — RED: Full bug scenario from user report.
    //
    // Reproduces the user's reported bug:
    //   - Confirmed sales (Pagado=true) with real margin
    //   - 1 MovimientoCaja with Categoria='GENERAL' and Monto=-$1
    //   - maquinaId = 0 (todas las unidades → gastosOperativos calculated)
    //
    // User saw:
    //   Ventas Confirmadas: $29.850 (27 TX)  ← from _reporte
    //   Utilidad: -$1                        ← from _financiero
    //   Margen: 0.0%                         ← from _financiero
    //
    // Root cause: gastosOperativos query in SalesAnalyticsService summed ALL
    // MovimientosCaja with Monto<0 (no category filter), so the Categoria='GENERAL'
    // -$1 leak made UtilidadNeta negative.
    //
    // CajaBusinessService.GetResumenAsync already filters by operational
    // categories — we just align this method with that contract.
    //
    // RED:   UtilidadNeta = -$1
    // GREEN: UtilidadNeta = 1800 (CATEGORIA='GENERAL' no cuenta como gasto)
    // =========================================================================
    [Fact]
    public async Task GetInformeFinancieroAsync_FullBugScenario_ReturnsCorrectFinancials_RED()
    {
        // Arrange
        var maquina1 = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine A");
        var maquina2 = TestDataHelpers.CreateMaquina(id: 2, nombre: "Machine B");
        _context.Maquinas.AddRange(maquina1, maquina2);

        var producto1 = TestDataHelpers.CreateProducto(
            id: 1, nombre: "Product A", costoPromedio: 400m);
        var producto2 = TestDataHelpers.CreateProducto(
            id: 2, nombre: "Product B", costoPromedio: 600m);
        _context.Productos.AddRange(producto1, producto2);

        // ── 3 confirmed sales (Pagado=true, post-Transbank reconciliation) ──
        // Sale 1: $1,000 → cost $400 → margin $600 (60%)
        _context.Ventas.Add(TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 6, 30, 10, 0, 0),
            precioVenta: 1000m,
            costoVenta: 400m,
            pagado: true,
            maquinaId: 1,
            productoId: 1
        ));

        // Sale 2: $1,500 → cost $600 → margin $900 (60%)
        _context.Ventas.Add(TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 6, 30, 11, 0, 0),
            precioVenta: 1500m,
            costoVenta: 600m,
            pagado: true,
            maquinaId: 1,
            productoId: 2
        ));

        // Sale 3: $500 → cost $200 → margin $300 (60%)
        _context.Ventas.Add(TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 7, 1, 9, 0, 0),
            precioVenta: 500m,
            costoVenta: 200m,
            pagado: true,
            maquinaId: 2,
            productoId: 1
        ));

        // ── 1 MovimientoCaja with -$1, Categoria=GENERAL ──
        // This is the leak: GENERAL is NOT an operational category, so it
        // should not be counted as GastoOperativo.
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -1m,
            fecha: new DateTime(2026, 6, 30),
            categoria: "GENERAL",
            tipo: "GASTO",
            descripcion: "Test expense (not operational)"
        ));

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            inicio: new DateTime(2026, 6, 29),
            fin: new DateTime(2026, 7, 1),
            maquinaId: 0);

        // Assert
        result.Should().NotBeNull();

        // RED (current code → category filter missing):
        //   VentasTotales = 3000, CostoVentas = 1200, MargenBruto = 1800
        //   GastosOperativos = 1            (GENERAL wrongly counted)
        //   UtilidadNeta = 1800 - 1 = 1799
        //   MargenPorcentaje = 60%          (this part is OK)
        //
        // GREEN (after fix → category filter applied):
        result.VentasTotales.Should().Be(3000m,   "VentasTotales = 1000+1500+500 = 3000");
        result.CostoVentas.Should().Be(1200m,     "CostoVentas = 400+600+200 = 1200");
        result.MargenBruto.Should().Be(1800m,     "MargenBruto = 3000-1200 = 1800");
        result.GastosOperativos.Should().Be(0m,   "GENERAL no es categoría operacional → no cuenta como gasto");
        result.UtilidadNeta.Should().Be(1800m,    "UtilidadNeta = 1800 - 0 = 1800 (no -$1)");
        result.MargenPorcentaje.Should().Be(60m,  "MargenPorcentaje = (1800/3000)*100 = 60%");
    }

    // =========================================================================
    // TEST 3 — RED: Multi-machine, phantoms excluded, operational gastos included
    // =========================================================================
    [Fact]
    public async Task GetInformeFinancieroAsync_MultipleMachines_ExcludesPhantoms_IncludesGastos_RED()
    {
        // Arrange
        var maquina1 = TestDataHelpers.CreateMaquina(id: 1, nombre: "Machine A");
        var maquina2 = TestDataHelpers.CreateMaquina(id: 2, nombre: "Machine B");
        _context.Maquinas.AddRange(maquina1, maquina2);

        var prod = TestDataHelpers.CreateProducto(id: 1, nombre: "Product", costoPromedio: 300m);
        _context.Productos.Add(prod);

        // Real sales (Pagado=true)
        _context.Ventas.Add(TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 6, 30), precioVenta: 2000m,
            costoVenta: 600m, pagado: true, maquinaId: 1, productoId: 1));
        _context.Ventas.Add(TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 7, 1), precioVenta: 3000m,
            costoVenta: 900m, pagado: true, maquinaId: 2, productoId: 1));

        // Phantoms (Pagado=true) — must be excluded
        _context.Ventas.Add(new Venta
        {
            FechaLocal = new DateTime(2026, 6, 30), FechaHora = new DateTime(2026, 6, 30),
            PrecioVenta = 1000m, Pagado = true, NumeroSlot = "ERR",
            IdOrdenMaquina = VentaConstants.TbExtra, ProductoId = null, CostoVenta = 0, MaquinaId = 1
        });
        _context.Ventas.Add(new Venta
        {
            FechaLocal = new DateTime(2026, 7, 1), FechaHora = new DateTime(2026, 7, 1),
            PrecioVenta = 2000m, Pagado = true, NumeroSlot = "ERR",
            IdOrdenMaquina = VentaConstants.TbSinVenta, ProductoId = null, CostoVenta = 0, MaquinaId = 2
        });

        // Operational gastos (LOGISTICA + INFRA) — must be counted
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -200m, fecha: new DateTime(2026, 6, 30), categoria: "LOGISTICA"));
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -300m, fecha: new DateTime(2026, 7, 1), categoria: "INFRA"));

        // Non-operational gastos (MERCADERIA, MERMA, GENERAL) — must NOT be counted
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -500m, fecha: new DateTime(2026, 6, 30), categoria: "MERCADERIA"));
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -50m, fecha: new DateTime(2026, 7, 1), categoria: "MERMA"));
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -1m, fecha: new DateTime(2026, 6, 30), categoria: "GENERAL"));

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            inicio: new DateTime(2026, 6, 29),
            fin: new DateTime(2026, 7, 1),
            maquinaId: 0);

        // Assert
        result.Should().NotBeNull();
        result.VentasTotales.Should().Be(5000m,   "2000+3000, phantoms excluidos");
        result.CostoVentas.Should().Be(1500m,     "600+900");
        result.MargenBruto.Should().Be(3500m,     "5000-1500");
        result.GastosOperativos.Should().Be(500m, "Solo LOGISTICA(200) + INFRA(300); MERCADERIA/MERMA/GENERAL excluidos");
        result.UtilidadNeta.Should().Be(2950m,    "3500-50(mermas)-500(gastos) — mermas subtracted via unified formula");
        result.MargenPorcentaje.Should().Be(70m,  "(3500/5000)*100=70%");
    }

    // =========================================================================
    // TEST 4 — GREEN: GastosOperativos is 0 when maquinaId > 0
    // =========================================================================
    [Fact]
    public async Task GetInformeFinancieroAsync_WithSingleMachine_GastosOperativosIsZero_GREEN()
    {
        // Arrange
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -500m, fecha: new DateTime(2026, 6, 30), categoria: "LOGISTICA"));

        await _context.SaveChangesAsync();

        // Act — maquinaId=1 → gastosOperativos should be 0
        var result = await _analyticsService.GetInformeFinancieroAsync(
            inicio: new DateTime(2026, 6, 29),
            fin: new DateTime(2026, 7, 1),
            maquinaId: 1);

        // Assert
        result.GastosOperativos.Should().Be(0m, "GastosOperativos solo se calcula para maquinaId=0");
    }

    // =========================================================================
    // TEST 5 — GREEN: GastosOperativos sums only operational-category negatives
    //
    // Verifies the full operational category list is honored.
    // Categories: LOGISTICA, PEAJES, INSUMOS, MANTENCION, INFRA, ARRIENDO_POS,
    //             INTERNET, COMISIONES, SUELDOS, GASTOS GENERALES, OTROS, SERVICIOS
    // =========================================================================
    [Fact]
    public async Task GetInformeFinancieroAsync_GastosOperativos_AllOperationalCategories_GREEN()
    {
        // Arrange — 1 negative per each operational category
        var operationalCategories = new[]
        {
            "LOGISTICA", "PEAJES", "INSUMOS", "MANTENCION",
            "INFRA", "ARRIENDO_POS", "INTERNET", "COMISIONES",
            "SUELDOS", "GASTOS GENERALES", "OTROS", "SERVICIOS"
        };
        int i = 1;
        foreach (var cat in operationalCategories)
        {
            _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
                monto: -100m * i, fecha: new DateTime(2026, 6, 30), categoria: cat));
            i++;
        }

        // Non-operational — must be ignored
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -9999m, fecha: new DateTime(2026, 6, 30), categoria: "GENERAL"));
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -9999m, fecha: new DateTime(2026, 6, 30), categoria: "MERCADERIA"));
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -9999m, fecha: new DateTime(2026, 6, 30), categoria: "MERMA"));
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -9999m, fecha: new DateTime(2026, 6, 30), categoria: "LOTES"));

        // Positive — must be ignored
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: 5000m, fecha: new DateTime(2026, 6, 30), categoria: "LOGISTICA"));

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            inicio: new DateTime(2026, 6, 29),
            fin: new DateTime(2026, 7, 1),
            maquinaId: 0);

        // Assert
        // Sum: 100*1 + 100*2 + ... + 100*12 = 100 * 78 = 7800
        result.GastosOperativos.Should().Be(7800m,
            "Solo las 12 categorías operacionales suman; GENERAL/MERCADERIA/MERMA/LOTES excluidos, positivos excluidos");
    }

    // =========================================================================
    // TEST 6 — RED: MovimientosCaja before CajaStartDate are excluded.
    //
    // CajaStartDate default is 2026-01-01. Movements before that date (legacy
    // data, pre-cuadre era) should NOT count as gastos of the current period.
    //
    // CajaBusinessService already filters by CajaStartDate; this method didn't.
    // =========================================================================
    [Fact]
    public async Task GetInformeFinancieroAsync_GastosOperativos_RespectsCajaStartDate_RED()
    {
        // Arrange
        // Pre-CajaStartDate: must be excluded
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -9999m, fecha: new DateTime(2025, 12, 31), categoria: "LOGISTICA"));

        // In-range operational: must be counted
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -250m, fecha: new DateTime(2026, 6, 30), categoria: "INFRA"));

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            inicio: new DateTime(2026, 6, 29),
            fin: new DateTime(2026, 7, 1),
            maquinaId: 0);

        // Assert
        result.GastosOperativos.Should().Be(250m,
            "Solo el movimiento post-CajaStartDate (2026-01-01) cuenta; el de 2025-12-31 se ignora");
    }

    // =========================================================================
    // TEST 7 — RED: When there are no Pagado=true sales, Margen and Utilidad
    // are 0 — NOT -gastosOperativos.
    //
    // The user's bug: with no confirmed sales, the screen showed
    // Utilidad = -$1 instead of $0, because gastosOperativos leaked into the
    // calculation even though there was no income to subtract from.
    //
    // Mathematically: UtilidadNeta = MargenBruto - GastosOperativos.
    //   If MargenBruto = 0 (no sales), UtilidadNeta = -GastosOperativos.
    //   The user expectation is UtilidadNeta = 0 when there are no confirmed sales.
    //
    // This is a SEMANTIC question — see TEST 8 for the alternative interpretation.
    // For now we lock the user's expected behavior: UtilidadNeta = 0 when no sales.
    // =========================================================================
    [Fact]
    public async Task GetInformeFinancieroAsync_NoConfirmedSales_UtilidadIsZero_RED()
    {
        // Arrange
        _context.MovimientosCaja.Add(TestDataHelpers.CreateMovimientoCaja(
            monto: -1m, fecha: new DateTime(2026, 6, 30),
            categoria: "GENERAL", tipo: "GASTO", descripcion: "Leak"));

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetInformeFinancieroAsync(
            inicio: new DateTime(2026, 6, 29),
            fin: new DateTime(2026, 7, 1),
            maquinaId: 0);

        // Assert
        result.VentasTotales.Should().Be(0m,        "No hay ventas confirmadas");
        result.MargenBruto.Should().Be(0m,          "Sin ingresos, margen bruto = 0");
        result.GastosOperativos.Should().Be(0m,     "GENERAL no es operacional → no se cuenta");
        result.UtilidadNeta.Should().Be(0m,         "Sin ingresos, no hay pérdida aunque haya gastos");
        result.MargenPorcentaje.Should().Be(0m,     "ingresosVentas=0 → 0%");
    }
}
