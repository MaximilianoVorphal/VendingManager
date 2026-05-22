namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Tests.TestData;
using FluentAssertions;

public class PurchasingService_GetStockCritico_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IExcelExportService> _mockExcelExport;
    private readonly Mock<ITemplateRecargaLifecycleService> _mockLifecycleService;
    private readonly Mock<ILogger<PurchasingService>> _mockLogger;
    private readonly IMemoryCache _cache;

    public PurchasingService_GetStockCritico_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"StockCriticoTestDb_{Guid.NewGuid()}");
        _mockExcelExport = new Mock<IExcelExportService>();
        _mockLifecycleService = new Mock<ITemplateRecargaLifecycleService>();
        _mockLogger = new Mock<ILogger<PurchasingService>>();
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    private PurchasingService CreateService(bool useTemplateInventory = false)
    {
        var vendingConfig = new VendingConfig
        {
            UseTemplateInventoryForStockCritico = useTemplateInventory
        };
        var config = Options.Create(vendingConfig);

        return new PurchasingService(
            _context, config, _mockExcelExport.Object, _cache,
            _mockLifecycleService.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    [Fact]
    public async Task GetStockCriticoAsync_FlagDisabled_FallsBackToConfiguracionSlots()
    {
        // Arrange
        var service = CreateService(useTemplateInventory: false);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Coca Cola", stockBodega: 10);
        var maquina = new Maquina { Id = 1, Nombre = "MAQUINA 2410280012" };
        var slot = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 1, capacidadMaxima: 10, stockMinimo: 2);

        _context.Productos.Add(producto);
        _context.Maquinas.Add(maquina);
        _context.ConfiguracionSlots.Add(slot);
        await _context.SaveChangesAsync();

        // Act
        var result = await service.GetStockCriticoAsync(maquinaId: 1);

        // Assert
        result.Should().HaveCount(1);
        result[0].Producto.Should().Be("Coca Cola");
        result[0].StockActual.Should().Be(1);
        result[0].Fuente.Should().Be("configuracion");

        // Verify lifecycle service was NOT called
        _mockLifecycleService.Verify(
            s => s.GetLatestActivoTemplateSlotsAsync(It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task GetStockCriticoAsync_FlagEnabled_WithTerminadoTemplate_ReturnsTemplateSlots()
    {
        // Arrange
        var service = CreateService(useTemplateInventory: true);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Sprite", stockBodega: 5);
        var maquina = new Maquina { Id = 2, Nombre = "MAQUINA 2410280002" };

        _context.Productos.Add(producto);
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var templateSlots = new List<SnapshotSlotDto>
        {
            new SnapshotSlotDto { Id = 100, NumeroSlot = "1", ProductoId = 1, ProductoNombre = "Sprite", CantidadInicial = 1, CapacidadSlot = 10, Estado = EstadoSlot.Lleno },
            new SnapshotSlotDto { Id = 101, NumeroSlot = "2", ProductoId = 1, ProductoNombre = "Sprite", CantidadInicial = 2, CapacidadSlot = 10, Estado = EstadoSlot.Lleno },
            new SnapshotSlotDto { Id = 102, NumeroSlot = "3", ProductoId = 1, ProductoNombre = "Sprite", CantidadInicial = 5, CapacidadSlot = 10, Estado = EstadoSlot.Lleno }
        };

        _mockLifecycleService
            .Setup(s => s.GetLatestActivoTemplateSlotsAsync(2))
            .ReturnsAsync(templateSlots);

        // Act
        var result = await service.GetStockCriticoAsync(maquinaId: 2);

        // Assert
        // Only slots with CantidadInicial <= 2 should be returned (slots 1 and 2)
        result.Should().HaveCount(2);
        result.All(r => r.Fuente == "template").Should().BeTrue();
        result.All(r => r.StockActual <= 2).Should().BeTrue();
    }

    [Fact]
    public async Task GetStockCriticoAsync_FlagEnabled_NoTerminadoTemplate_FallsBackToConfiguracionSlots()
    {
        // Arrange
        var service = CreateService(useTemplateInventory: true);

        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Fanta", stockBodega: 3);
        var maquina = new Maquina { Id = 3, Nombre = "MAQUINA 2410280003" };
        var slot = TestDataHelpers.CreateSlot(id: 5, maquinaId: 3, productoId: 1, stockActual: 1, capacidadMaxima: 8, stockMinimo: 2);

        _context.Productos.Add(producto);
        _context.Maquinas.Add(maquina);
        _context.ConfiguracionSlots.Add(slot);
        await _context.SaveChangesAsync();

        // No Activo template (returns empty list)
        _mockLifecycleService
            .Setup(s => s.GetLatestActivoTemplateSlotsAsync(3))
            .ReturnsAsync(new List<SnapshotSlotDto>());

        // Act
        var result = await service.GetStockCriticoAsync(maquinaId: 3);

        // Assert - should fall back to ConfiguracionSlots
        result.Should().HaveCount(1);
        result[0].Fuente.Should().Be("configuracion");
        result[0].Producto.Should().Be("Fanta");
    }

    [Fact]
    public async Task GetStockCriticoAsync_WithMultipleMachines_ReturnsAllCriticalSlots()
    {
        // Arrange
        var service = CreateService(useTemplateInventory: false);

        var producto1 = TestDataHelpers.CreateProducto(id: 1, nombre: "Producto A", stockBodega: 5);
        var producto2 = TestDataHelpers.CreateProducto(id: 2, nombre: "Producto B", stockBodega: 5);
        var maquina1 = new Maquina { Id = 10, Nombre = "MAQUINA 2410280010" };
        var maquina2 = new Maquina { Id = 11, Nombre = "MAQUINA 2410280011" };

        var slot1 = TestDataHelpers.CreateSlot(id: 10, maquinaId: 10, productoId: 1, stockActual: 1, capacidadMaxima: 10, stockMinimo: 2);
        var slot2 = TestDataHelpers.CreateSlot(id: 11, maquinaId: 11, productoId: 2, stockActual: 0, capacidadMaxima: 10, stockMinimo: 2);
        var slot3 = TestDataHelpers.CreateSlot(id: 12, maquinaId: 10, productoId: 2, stockActual: 10, capacidadMaxima: 10, stockMinimo: 2); // Not critical

        _context.Productos.AddRange(producto1, producto2);
        _context.Maquinas.AddRange(maquina1, maquina2);
        _context.ConfiguracionSlots.AddRange(slot1, slot2, slot3);
        await _context.SaveChangesAsync();

        // Act - query all machines (maquinaId = 0)
        var result = await service.GetStockCriticoAsync(maquinaId: 0);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Maquina == "MAQUINA 2410280010" && r.Producto == "Producto A");
        result.Should().Contain(r => r.Maquina == "MAQUINA 2410280011" && r.Producto == "Producto B");
    }

    [Fact]
    public async Task GetStockCriticoAsync_TemplateSlots_SlotsAboveThreshold_AreExcluded()
    {
        // Arrange
        var service = CreateService(useTemplateInventory: true);

        var maquina = new Maquina { Id = 5, Nombre = "MAQUINA 2410280005" };
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var templateSlots = new List<SnapshotSlotDto>
        {
            new SnapshotSlotDto { Id = 1, NumeroSlot = "1", ProductoId = 1, ProductoNombre = "Agua", CantidadInicial = 0, CapacidadSlot = 10, Estado = EstadoSlot.Vacio },
            new SnapshotSlotDto { Id = 2, NumeroSlot = "2", ProductoId = 1, ProductoNombre = "Agua", CantidadInicial = 1, CapacidadSlot = 10, Estado = EstadoSlot.Lleno },
            new SnapshotSlotDto { Id = 3, NumeroSlot = "3", ProductoId = 1, ProductoNombre = "Agua", CantidadInicial = 2, CapacidadSlot = 10, Estado = EstadoSlot.Lleno },
            new SnapshotSlotDto { Id = 4, NumeroSlot = "4", ProductoId = 1, ProductoNombre = "Agua", CantidadInicial = 3, CapacidadSlot = 10, Estado = EstadoSlot.Lleno }
        };

        _mockLifecycleService.Setup(s => s.GetLatestActivoTemplateSlotsAsync(5)).ReturnsAsync(templateSlots);

        // Act
        var result = await service.GetStockCriticoAsync(maquinaId: 5);

        // Assert - only slots with CantidadInicial <= 2 (slots 1, 2, 3)
        result.Should().HaveCount(3);
        result.Should().NotContain(r => r.NumeroSlot == "4");
    }

    [Fact]
    public async Task GetStockCriticoAsync_TemplateSlots_NullProductoId_AreExcluded()
    {
        // Arrange
        var service = CreateService(useTemplateInventory: true);

        var maquina = new Maquina { Id = 6, Nombre = "MAQUINA 2410280006" };
        _context.Maquinas.Add(maquina);
        await _context.SaveChangesAsync();

        var templateSlots = new List<SnapshotSlotDto>
        {
            new SnapshotSlotDto { Id = 1, NumeroSlot = "1", ProductoId = null, ProductoNombre = "", CantidadInicial = 0, CapacidadSlot = 10, Estado = EstadoSlot.Vacio },
            new SnapshotSlotDto { Id = 2, NumeroSlot = "2", ProductoId = 1, ProductoNombre = "Papas", CantidadInicial = 1, CapacidadSlot = 10, Estado = EstadoSlot.Lleno }
        };

        _mockLifecycleService.Setup(s => s.GetLatestActivoTemplateSlotsAsync(6)).ReturnsAsync(templateSlots);

        // Act
        var result = await service.GetStockCriticoAsync(maquinaId: 6);

        // Assert - only slot with non-null productoId
        result.Should().HaveCount(1);
        result[0].NumeroSlot.Should().Be("2");
    }
}