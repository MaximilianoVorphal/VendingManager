namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Moq;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

public class SalesAnalyticsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly SalesAnalyticsService _analyticsService;

    public SalesAnalyticsServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"AnalyticsTestDb_{Guid.NewGuid()}");
        _analyticsService = new SalesAnalyticsService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithNoSales_ReturnsZeroStats()
    {
        // Arrange - no sales, seed some slots with stock levels
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Test Machine");
        var producto1 = TestDataHelpers.CreateProducto(id: 1, nombre: "Product A", stockBodega: 10);
        var producto2 = TestDataHelpers.CreateProducto(id: 2, nombre: "Product B", stockBodega: 5);

        var slot1 = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 5, capacidadMaxima: 10);
        var slot2 = TestDataHelpers.CreateSlot(id: 2, maquinaId: 1, productoId: 2, stockActual: 1, capacidadMaxima: 5);

        _context.Maquinas.Add(maquina);
        _context.Productos.AddRange(producto1, producto2);
        _context.ConfiguracionSlots.AddRange(slot1, slot2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetDashboardStatsAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Hoy.VentaTotal.Should().Be(0m);
        result.Hoy.CantidadVentas.Should().Be(0);
        result.Semana.VentaTotal.Should().Be(0m);
        result.Semana.CantidadVentas.Should().Be(0);
        result.Mes.VentaTotal.Should().Be(0m);
        result.Mes.CantidadVentas.Should().Be(0);
        // StockCriticoCount: slots where StockActual <= 2
        result.CantidadStockCritico.Should().Be(1); // slot2 has StockActual=1
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithSalesAcrossPeriods_ReturnsCorrectStats()
    {
        // Arrange
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Test Machine");
        _context.Maquinas.Add(maquina);

        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (startOfWeek > today) startOfWeek = startOfWeek.AddDays(-7);
        var startOfMonth = new DateTime(today.Year, today.Month, 1);

        // 2 sales today (100 + 200)
        var venta1 = TestDataHelpers.CreateVenta(fechaLocal: today, precioVenta: 100m, costoVenta: 40m, maquinaId: 1, productoId: 1);
        venta1.FechaHora = today;
        var venta2 = TestDataHelpers.CreateVenta(fechaLocal: today, precioVenta: 200m, costoVenta: 80m, maquinaId: 1, productoId: 1);
        venta2.FechaHora = today;

        // 1 sale yesterday (but still in week total)
        var venta3 = TestDataHelpers.CreateVenta(fechaLocal: yesterday, precioVenta: 100m, costoVenta: 40m, maquinaId: 1, productoId: 1);
        venta3.FechaHora = yesterday;

        // 2 more sales in this month (outside current week)
        var dayOfMonthEarly = startOfMonth.AddDays(5);
        var venta4 = TestDataHelpers.CreateVenta(fechaLocal: dayOfMonthEarly, precioVenta: 300m, costoVenta: 120m, maquinaId: 1, productoId: 1);
        venta4.FechaHora = dayOfMonthEarly;
        var venta5 = TestDataHelpers.CreateVenta(fechaLocal: dayOfMonthEarly.AddDays(3), precioVenta: 300m, costoVenta: 120m, maquinaId: 1, productoId: 1);
        venta5.FechaHora = dayOfMonthEarly.AddDays(3);

        _context.Ventas.AddRange(venta1, venta2, venta3, venta4, venta5);

        // Seed slot to avoid null navigation
        var slot = TestDataHelpers.CreateSlot(id: 1, maquinaId: 1, productoId: 1, stockActual: 5);
        _context.ConfiguracionSlots.Add(slot);

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetDashboardStatsAsync(1);

        // Assert
        result.Should().NotBeNull();
        // Today: 2 sales, 100 + 200 = 300
        result.Hoy.CantidadVentas.Should().Be(2);
        result.Hoy.VentaTotal.Should().Be(300m);
        // This week: 3 sales (today + yesterday), 100 + 200 + 100 = 400
        result.Semana.CantidadVentas.Should().Be(3);
        result.Semana.VentaTotal.Should().Be(400m);
        // This month: 5 sales, 100 + 200 + 100 + 300 + 300 = 1000
        result.Mes.CantidadVentas.Should().Be(5);
        result.Mes.VentaTotal.Should().Be(1000m);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithStockCritico_ReturnsCorrectCount()
    {
        // Arrange - 10 products, 3 below StockMinimo (using StockActual <= 2 as proxy)
        var maquina = TestDataHelpers.CreateMaquina(id: 1, nombre: "Test Machine");
        _context.Maquinas.Add(maquina);

        // Add 10 slots, 3 with stock <= 2 (critical)
        for (int i = 1; i <= 7; i++)
        {
            _context.ConfiguracionSlots.Add(TestDataHelpers.CreateSlot(
                id: i, maquinaId: 1, productoId: i, stockActual: 5, capacidadMaxima: 10));
        }
        // Critical stock slots (StockActual <= 2)
        _context.ConfiguracionSlots.Add(TestDataHelpers.CreateSlot(id: 8, maquinaId: 1, productoId: 8, stockActual: 1, capacidadMaxima: 10));
        _context.ConfiguracionSlots.Add(TestDataHelpers.CreateSlot(id: 9, maquinaId: 1, productoId: 9, stockActual: 2, capacidadMaxima: 10));
        _context.ConfiguracionSlots.Add(TestDataHelpers.CreateSlot(id: 10, maquinaId: 1, productoId: 10, stockActual: 0, capacidadMaxima: 10));

        await _context.SaveChangesAsync();

        // Act
        var result = await _analyticsService.GetDashboardStatsAsync(1);

        // Assert
        result.Should().NotBeNull();
        // 3 slots have StockActual <= 2
        result.CantidadStockCritico.Should().Be(3);
    }
}