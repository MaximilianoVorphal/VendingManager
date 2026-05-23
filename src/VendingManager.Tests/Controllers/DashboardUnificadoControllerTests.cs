namespace VendingManager.Tests.Controllers;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

public class DashboardUnificadoControllerTests
{
    private readonly Mock<ISalesAnalyticsService> _mockSales;
    private readonly Mock<ITransferenciaService> _mockTransferencia;
    private readonly Mock<ICompraService> _mockCompra;
    private readonly Mock<IGastoRecurrenteService> _mockGastoRecurrente;
    private readonly Mock<ICajaService> _mockCaja;
    private readonly Mock<IPurchasingService> _mockPurchasing;
    private readonly Mock<IVentaRepository> _mockVentaRepo;
    private readonly IMemoryCache _memoryCache;
    private readonly DashboardUnificadoController _controller;

    public DashboardUnificadoControllerTests()
    {
        _mockSales = new Mock<ISalesAnalyticsService>();
        _mockTransferencia = new Mock<ITransferenciaService>();
        _mockCompra = new Mock<ICompraService>();
        _mockGastoRecurrente = new Mock<IGastoRecurrenteService>();
        _mockCaja = new Mock<ICajaService>();
        _mockPurchasing = new Mock<IPurchasingService>();
        _mockVentaRepo = new Mock<IVentaRepository>();

        var cacheOptions = new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions();
        _memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(cacheOptions);

        _controller = new DashboardUnificadoController(
            _mockSales.Object,
            _mockTransferencia.Object,
            _mockCompra.Object,
            _mockGastoRecurrente.Object,
            _mockCaja.Object,
            _mockPurchasing.Object,
            _mockVentaRepo.Object,
            _memoryCache);
    }

    private void SetupDefaultMocks(int maquinaId)
    {
        _mockTransferencia.Setup(s => s.GetTransferenciasPendientesAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.Transferencia>());
        _mockCompra.Setup(s => s.GetComprasAsync(It.IsAny<int?>()))
            .ReturnsAsync(new List<VendingManager.Core.Entities.Compra>());
        _mockGastoRecurrente.Setup(s => s.GetPendientesDelMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<GastoPendienteDto>());
        _mockCaja.Setup(s => s.GetMovimientosAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<VendingManager.Core.Entities.MovimientoCaja>());
        _mockPurchasing.Setup(s => s.GetStockCriticoAsync(maquinaId))
            .ReturnsAsync(new List<StockCriticoDto>());
        _mockTransferencia.Setup(s => s.GetTransferenciasNoVinculadasAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.Transferencia>());
        _mockCompra.Setup(s => s.GetComprasNoVinculadasAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.Compra>());
        _mockCaja.Setup(s => s.GetGastosNoVinculadosAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.MovimientoCaja>());
        _mockVentaRepo.Setup(r => r.GetRecentAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VendingManager.Core.Entities.Venta>());
    }

    // ─── Cache Miss: all 6 services called ─────────────────────────────────────

    [Fact]
    public async Task GetUnificado_CacheMiss_CallsAllSixServices()
    {
        // Arrange
        var maquinaId = 0;

        _mockSales.Setup(s => s.GetDashboardStatsAsync(maquinaId))
            .ReturnsAsync(new DashboardStats { Mes = new PeriodoStats { VentaTotal = 500_000m, CantidadVentas = 120 } });
        SetupDefaultMocks(maquinaId);

        // Act
        var result = await _controller.GetUnificado(maquinaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _mockSales.Verify(s => s.GetDashboardStatsAsync(maquinaId), Times.Once);
        _mockTransferencia.Verify(s => s.GetTransferenciasPendientesAsync(), Times.Once);
        _mockCompra.Verify(s => s.GetComprasAsync(It.IsAny<int?>()), Times.Once);
        _mockGastoRecurrente.Verify(s => s.GetPendientesDelMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        _mockCaja.Verify(s => s.GetMovimientosAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        _mockPurchasing.Verify(s => s.GetStockCriticoAsync(maquinaId), Times.Once);
        _mockTransferencia.Verify(s => s.GetTransferenciasNoVinculadasAsync(), Times.Once);
        _mockCompra.Verify(s => s.GetComprasNoVinculadasAsync(), Times.Once);
        _mockCaja.Verify(s => s.GetGastosNoVinculadosAsync(), Times.Once);
        _mockVentaRepo.Verify(r => r.GetRecentAsync(20, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Cache Hit: returns cached DTO without calling services ──────────────

    [Fact]
    public async Task GetUnificado_CacheHit_ReturnsCachedDtoWithoutCallingServices()
    {
        // Arrange
        var maquinaId = 5;
        var cacheKey = $"dashboard_{maquinaId}";
        var cachedDto = new DashboardUnificadoDto
        {
            Pipeline = new PipelineFinancieroDto { VentasMes = 999m },
            Alertas = new AlertaConsolidadaDto(),
            Actividad = new List<ActividadRecienteDto>()
        };
        _memoryCache.Set(cacheKey, cachedDto);

        // Act
        var result = await _controller.GetUnificado(maquinaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var returnedDto = okResult.Value.Should().BeOfType<DashboardUnificadoDto>().Subject;
        returnedDto.Pipeline.VentasMes.Should().Be(999m);
        _mockSales.Verify(s => s.GetDashboardStatsAsync(It.IsAny<int>()), Times.Never);
    }

    // ─── Single service failure: returns partial data, others intact ──────────

    [Fact]
    public async Task GetUnificado_SingleServiceFailure_ReturnsPartialDataOthersIntact()
    {
        // Arrange
        var maquinaId = 0;

        SetupDefaultMocks(maquinaId);

        _mockSales.Setup(s => s.GetDashboardStatsAsync(maquinaId))
            .ReturnsAsync(new DashboardStats { Mes = new PeriodoStats { VentaTotal = 100_000m } });

        // Simulate TransferenciaService throwing
        _mockTransferencia.Setup(s => s.GetTransferenciasPendientesAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act
        var result = await _controller.GetUnificado(maquinaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var dto = okResult.Value.Should().BeOfType<DashboardUnificadoDto>().Subject;

        // Ventas should be populated (service didn't fail)
        dto.Pipeline.VentasMes.Should().Be(100_000m);
        // TransferenciasActivas should be zero (failed service returned partial/default)
        dto.Pipeline.TransferenciasActivas.Should().Be(0m);
        // Other services still called
        _mockCompra.Verify(s => s.GetComprasAsync(It.IsAny<int?>()), Times.Once);
    }

    // ─── maquinaId=0 vs maquinaId=specific filters correctly ─────────────────

    [Fact]
    public async Task GetUnificado_MaquinaIdZero_QueriesAllMachines()
    {
        // Arrange
        var maquinaId = 0;

        _mockSales.Setup(s => s.GetDashboardStatsAsync(maquinaId))
            .ReturnsAsync(new DashboardStats { Mes = new PeriodoStats { VentaTotal = 1_000_000m } });
        SetupDefaultMocks(maquinaId);

        // Act
        var result = await _controller.GetUnificado(maquinaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _mockSales.Verify(s => s.GetDashboardStatsAsync(0), Times.Once);
        _mockPurchasing.Verify(s => s.GetStockCriticoAsync(0), Times.Once);
        _mockVentaRepo.Verify(r => r.GetRecentAsync(20, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUnificado_MaquinaIdSpecific_FiltersCorrectly()
    {
        // Arrange
        var maquinaId = 3;

        _mockSales.Setup(s => s.GetDashboardStatsAsync(maquinaId))
            .ReturnsAsync(new DashboardStats { Mes = new PeriodoStats { VentaTotal = 300_000m } });
        SetupDefaultMocks(maquinaId);

        // Act
        var result = await _controller.GetUnificado(maquinaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _mockSales.Verify(s => s.GetDashboardStatsAsync(3), Times.Once);
        _mockPurchasing.Verify(s => s.GetStockCriticoAsync(3), Times.Once);
        _mockVentaRepo.Verify(r => r.GetRecentAsync(20, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Conciliacion calculation: Transferencias - Compras - Gastos ─────────

    [Fact]
    public async Task GetUnificado_Conciliacion_CorrectlyCalculated()
    {
        // Arrange
        var maquinaId = 0;

        SetupDefaultMocks(maquinaId);

        // Ventas: $500K
        _mockSales.Setup(s => s.GetDashboardStatsAsync(maquinaId))
            .ReturnsAsync(new DashboardStats
            {
                Mes = new PeriodoStats { VentaTotal = 500_000m, CantidadVentas = 100 }
            });

        // Transferencias: $420K (2 transfers of $210K each)
        _mockTransferencia.Setup(s => s.GetTransferenciasPendientesAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.Transferencia>
            {
                new() { Id = 1, Monto = 210_000m },
                new() { Id = 2, Monto = 210_000m }
            });

        // Compras vinculadas: $300K (linked to transfer 1)
        _mockCompra.Setup(s => s.GetComprasAsync(It.IsAny<int?>()))
            .ReturnsAsync(new List<VendingManager.Core.Entities.Compra>
            {
                new() { Id = 1, MontoTotal = 300_000m, TransferenciaId = 1 }
            });

        // Gastos pendientes: $50K
        _mockGastoRecurrente.Setup(s => s.GetPendientesDelMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<GastoPendienteDto>
            {
                new() { GastoRecurrenteId = 1, MontoEstimado = 50_000m }
            });

        // Act
        var result = await _controller.GetUnificado(maquinaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var dto = ((OkObjectResult)result).Value.Should().BeOfType<DashboardUnificadoDto>().Subject;

        dto.Pipeline.VentasMes.Should().Be(500_000m);
        dto.Pipeline.TransferenciasActivas.Should().Be(420_000m);
        dto.Pipeline.ComprasVinculadas.Should().Be(300_000m);
        dto.Pipeline.GastosVinculados.Should().Be(50_000m);
        // Conciliacion = 420K - 300K - 50K = 70K > 0 → positiva
        dto.Pipeline.Conciliacion.Should().Be(70_000m);
        dto.Pipeline.IsPositiva.Should().BeTrue();
    }

    // ─── Progressive loading fields populated ───────────────────────────────

    [Fact]
    public async Task GetUnificado_ProgressiveLoading_AllSectionsPopulated()
    {
        // Arrange
        var maquinaId = 0;
        var today = DateTime.Now;

        _mockSales.Setup(s => s.GetDashboardStatsAsync(maquinaId))
            .ReturnsAsync(new DashboardStats
            {
                Mes = new PeriodoStats { VentaTotal = 750_000m, CantidadVentas = 200 },
                CantidadStockCritico = 3
            });

        _mockTransferencia.Setup(s => s.GetTransferenciasPendientesAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.Transferencia>
            {
                new() { Id = 1, Monto = 100_000m, Fecha = today.AddDays(-1) },
                new() { Id = 2, Monto = 200_000m, Fecha = today.AddDays(-2) }
            });

        _mockCompra.Setup(s => s.GetComprasAsync(It.IsAny<int?>()))
            .ReturnsAsync(new List<VendingManager.Core.Entities.Compra>
            {
                new() { Id = 1, FechaCompra = today.AddDays(-1), MontoTotal = 80_000m, Proveedor = "Acme", TransferenciaId = 1 }
            });

        _mockGastoRecurrente.Setup(s => s.GetPendientesDelMesAsync(today.Month, today.Year))
            .ReturnsAsync(new List<GastoPendienteDto>
            {
                new() { Descripcion = "Arriendo", MontoEstimado = 150_000m }
            });

        _mockCaja.Setup(s => s.GetMovimientosAsync(today.Month, today.Year))
            .ReturnsAsync(new List<VendingManager.Core.Entities.MovimientoCaja>
            {
                new() { Id = 1, Fecha = today.AddDays(-3), Monto = 50_000m, Descripcion = "Pago proveedor" }
            });

        _mockPurchasing.Setup(s => s.GetStockCriticoAsync(maquinaId))
            .ReturnsAsync(new List<StockCriticoDto>
            {
                new() { SlotId = 1, Producto = "Chocolate", StockActual = 2 },
                new() { SlotId = 2, Producto = "Bebida", StockActual = 1 }
            });

        _mockTransferencia.Setup(s => s.GetTransferenciasNoVinculadasAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.Transferencia>());

        _mockCompra.Setup(s => s.GetComprasNoVinculadasAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.Compra>());

        _mockCaja.Setup(s => s.GetGastosNoVinculadosAsync())
            .ReturnsAsync(new List<VendingManager.Core.Entities.MovimientoCaja>());

        _mockVentaRepo.Setup(r => r.GetRecentAsync(20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VendingManager.Core.Entities.Venta>
            {
                new() { Id = 1, FechaHora = today.AddHours(-2), PrecioVenta = 1500m, MaquinaId = 1, NumeroSlot = "101" }
            });

        // Act
        var result = await _controller.GetUnificado(maquinaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var dto = ((OkObjectResult)result).Value.Should().BeOfType<DashboardUnificadoDto>().Subject;

        // Pipeline
        dto.Pipeline.Should().NotBeNull();
        dto.Pipeline.VentasMes.Should().Be(750_000m);
        dto.Pipeline.CantidadVentasMes.Should().Be(200);
        dto.Pipeline.TransferenciasActivas.Should().Be(300_000m);
        dto.Pipeline.CantidadTransferencias.Should().Be(2);
        dto.Pipeline.ComprasVinculadas.Should().Be(80_000m);
        dto.Pipeline.GastosVinculados.Should().Be(150_000m);
        dto.Pipeline.Conciliacion.Should().Be(70_000m);

        // Alertas
        dto.Alertas.Should().NotBeNull();
        dto.Alertas.Items.Should().HaveCount(3); // 2 stock critico + 1 gasto recurrente
        dto.Alertas.Items.Any(i => i.Tipo == "stock-critico").Should().BeTrue();
        dto.Alertas.Items.Any(i => i.Tipo == "gastos-fijos-no-registrados").Should().BeTrue();

        // Actividad — should now include ventas entries
        dto.Actividad.Should().NotBeNull();
        dto.Actividad.Should().NotBeEmpty();
        dto.Actividad.Any(a => a.Tipo == "venta").Should().BeTrue();
    }
}