namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

public class PurchasingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IExcelExportService> _mockExcelExport;
    private readonly IMemoryCache _cache;
    private readonly PurchasingService _purchasingService;

    public PurchasingServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"PurchasingTestDb_{Guid.NewGuid()}");

        var vendingConfig = new VendingConfig
        {
            CajaStartDate = new DateTime(2025, 12, 18),
            TransbankFee = 80,
            RotacionStockMinimoDias = 30,
            RotacionUmbralCritico = 7
        };
        var config = Options.Create(vendingConfig);
        _mockExcelExport = new Mock<IExcelExportService>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _purchasingService = new PurchasingService(_context, config, _mockExcelExport.Object, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    [Fact]
    public async Task GetPurchaseSuggestionAsync_WithNoSuggestionsNeeded_ReturnsEmptyList()
    {
        // Arrange - all products have stock >= minimum, no sales history
        var producto1 = TestDataHelpers.CreateProducto(id: 1, nombre: "Product A", stockBodega: 10);
        var producto2 = TestDataHelpers.CreateProducto(id: 2, nombre: "Product B", stockBodega: 15);

        // Add slots indicating products are in machines with sufficient stock
        var slot1 = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 10, capacidadMaxima: 20);
        var slot2 = TestDataHelpers.CreateSlot(id: 2, maquinaId: 1, productoId: 2, stockActual: 8, capacidadMaxima: 20);

        _context.Productos.AddRange(producto1, producto2);
        _context.ConfiguracionSlots.AddRange(slot1, slot2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _purchasingService.GetPurchaseSuggestionAsync(dias: 30, maquinaId: 0);

        // Assert
        result.Should().NotBeNull();
        // All products have enough stock (machine + bodega >= sales), so suggestions should be 0 or empty
        var suggestionsWithQuantity = result.Where(s => s.CantidadSugerida > 0).ToList();
        suggestionsWithQuantity.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPurchaseSuggestionAsync_WithSomeProductsNeedingRestock_ReturnsCorrectSuggestions()
    {
        // Arrange
        // ProductA: StockMaquina=2, StockBodega=0, Sales=5 → need 3 more (5 - 2 - 0 = 3)
        // ProductB: StockMaquina=10, StockBodega=5, Sales=8 → need 0 (8 - 10 - 5 < 0)
        var productoA = TestDataHelpers.CreateProducto(id: 1, nombre: "Product A", stockBodega: 0);
        var productoB = TestDataHelpers.CreateProducto(id: 2, nombre: "Product B", stockBodega: 5);

        // Seed sales history for ProductA (5 sales in 30 days)
        var baseDate = DateTime.Now.Date.AddDays(-5);
        for (int i = 0; i < 5; i++)
        {
            var venta = TestDataHelpers.CreateVenta(
                fechaLocal: baseDate.AddDays(i),
                precioVenta: 100m,
                costoVenta: 40m,
                maquinaId: 1,
                productoId: 1
            );
            venta.FechaHora = baseDate.AddDays(i);
            _context.Ventas.Add(venta);
        }

        // Add slots with stock
        var slot1 = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 2, capacidadMaxima: 10);
        var slot2 = TestDataHelpers.CreateSlot(id: 2, maquinaId: 1, productoId: 2, stockActual: 10, capacidadMaxima: 10);

        _context.Productos.AddRange(productoA, productoB);
        _context.ConfiguracionSlots.AddRange(slot1, slot2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _purchasingService.GetPurchaseSuggestionAsync(dias: 30, maquinaId: 0);

        // Assert
        result.Should().NotBeNull();
        var productASuggestion = result.FirstOrDefault(s => s.ProductoId == 1);
        productASuggestion.Should().NotBeNull();
        productASuggestion!.CantidadSugerida.Should().Be(3); // 5 sales - 2 machine - 0 bodega = 3
    }

    [Fact]
    public async Task GetPurchaseSuggestionAsync_WithZeroStock_ReturnsFullSuggestion()
    {
        // Arrange
        // ProductX: StockMaquina=0, StockBodega=0, Sales=10 → need 10
        var productoX = TestDataHelpers.CreateProducto(id: 1, nombre: "Product X", stockBodega: 0);

        // Seed sales history (10 sales)
        var baseDate = DateTime.Now.Date.AddDays(-5);
        for (int i = 0; i < 10; i++)
        {
            var venta = TestDataHelpers.CreateVenta(
                fechaLocal: baseDate.AddDays(i),
                precioVenta: 100m,
                costoVenta: 40m,
                maquinaId: 1,
                productoId: 1
            );
            venta.FechaHora = baseDate.AddDays(i);
            _context.Ventas.Add(venta);
        }

        // Slot with zero stock
        var slot = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 0, capacidadMaxima: 10);
        _context.Productos.Add(productoX);
        _context.ConfiguracionSlots.Add(slot);
        await _context.SaveChangesAsync();

        // Act
        var result = await _purchasingService.GetPurchaseSuggestionAsync(dias: 30, maquinaId: 0);

        // Assert
        result.Should().NotBeNull();
        var productXSuggestion = result.FirstOrDefault(s => s.ProductoId == 1);
        productXSuggestion.Should().NotBeNull();
        productXSuggestion!.CantidadSugerida.Should().Be(10); // 10 sales - 0 machine - 0 bodega = 10
    }
}