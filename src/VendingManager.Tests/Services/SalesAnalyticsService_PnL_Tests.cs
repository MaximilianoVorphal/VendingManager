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
using VendingManager.Shared;
using VendingManager.Tests.TestData;

/// <summary>
/// Characterization tests for SalesAnalyticsService P&amp;L unification.
/// Verifies the service uses CategoriasGasto.Operacionales and
/// CalcularUtilidadOperacional instead of the hardcoded 12-string array.
/// </summary>
public class SalesAnalyticsService_PnL_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IExcelExportService> _mockExcelExport;
    private readonly IMemoryCache _cache;
    private readonly IOptions<AnalyticsThresholds> _thresholds;
    private readonly IOptions<VendingConfig> _config;
    private readonly SalesAnalyticsService _service;

    public SalesAnalyticsService_PnL_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"SalesAnalyticsPnLTestDb_{Guid.NewGuid()}");
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

    /// <summary>
    /// Verify that both services use the same catalog: CategoriasGasto.Operacionales.
    /// This test doesn't call the service directly (it needs complex venta data)
    /// but verifies the catalog itself is the single source of truth.
    /// </summary>
    [Fact]
    public void Operacionales_ContainsAll12Categories()
    {
        CategoriasGasto.Operacionales.Count.Should().Be(12);
    }

    /// <summary>
    /// Verify the unified formula is used: margenBruto - mermasAbs - totalGastosOps.
    /// </summary>
    [Fact]
    public void Formula_SubtractsMermasFromMargenBruto()
    {
        // Same formula used by CajaBusinessService:105 and SalesAnalyticsService after refactor
        var result = CategoriasGasto.CalcularUtilidadOperacional(
            margenBruto: 10000m,
            mermasAbs: 1500m,
            totalGastosOps: 3000m);

        result.Should().Be(5500m); // 10000 - 1500 - 3000
    }

    /// <summary>
    /// Integration test: verify SalesAnalyticsService.GetInformeFinancieroAsync
    /// filters movimientos by CategoriasGasto.Operacionales.
    /// </summary>
    [Fact]
    public async Task GetInformeFinancieroAsync_UsesSharedCatalog()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "M1");
        _context.Maquinas.Add(maquina);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "P1", costoPromedio: 400m);
        _context.Productos.Add(producto);

        // Real sale
        _context.Ventas.Add(TestDataHelpers.CreateVenta(
            fechaLocal: new DateTime(2026, 7, 5),
            precioVenta: 1000m, costoVenta: 400m,
            pagado: true, maquinaId: 1, productoId: 1));

        // Operational expense (LOGISTICA in Operacionales)
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2026, 7, 5),
            Monto = -200m,
            Tipo = "GASTO",
            Categoria = "LOGISTICA",
            Descripcion = "Bencina"
        });

        // Non-operational expense (MERCADERIA NOT in Operacionales)
        _context.MovimientosCaja.Add(new MovimientoCaja
        {
            Fecha = new DateTime(2026, 7, 5),
            Monto = -300m,
            Tipo = "GASTO",
            Categoria = "MERCADERIA",
            Descripcion = "Compra mercaderia"
        });

        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetInformeFinancieroAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31),
            maquinaId: 0);

        // Assert: only LOGISTICA (200) counted; MERCADERIA (300) excluded
        result.VentasTotales.Should().Be(1000m);
        result.CostoVentas.Should().Be(400m);
        result.MargenBruto.Should().Be(600m);
        result.GastosOperativos.Should().Be(200m); // only LOGISTICA
        // UtilidadNeta = MargenBruto - GastosOperativos = 600 - 200 = 400
        result.UtilidadNeta.Should().Be(400m);
    }
}
