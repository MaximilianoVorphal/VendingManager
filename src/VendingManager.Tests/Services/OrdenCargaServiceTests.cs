namespace VendingManager.Tests.Services;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

/// <summary>
/// Draft (BORRADOR) state + stock deduction bug fix tests for OrdenCargaService.
/// TDD RED phase — these tests MUST fail before Phase 2 implementation.
/// </summary>
public class OrdenCargaServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly OrdenCargaService _service;

    public OrdenCargaServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"OrdenCargaSvc_{Guid.NewGuid()}");
        _service = new OrdenCargaService(_context);
    }

    public void Dispose() => _context.Dispose();

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task SeedProducto(Producto p)
    {
        _context.Productos.Add(p);
        await _context.SaveChangesAsync();
    }

    private async Task SeedMaquina(Maquina m)
    {
        _context.Maquinas.Add(m);
        await _context.SaveChangesAsync();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Task 1.1 — RED: CrearOrdenBorradorAsync tests
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public async Task CrearBorrador_DebeCrearOrdenConEstadoBORRADOR()
    {
        // Arrange
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Galletas", stockBodega: 10);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            Nombre = "Draft Test",
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 3 }
            }
        };

        // Act
        var result = await _service.CrearOrdenBorradorAsync(dto);

        // Assert
        result.Estado.Should().Be("BORRADOR");
        result.Nombre.Should().Be("Draft Test");

        // Verify persisted state
        var persisted = await _context.OrdenesCarga
            .Include(o => o.Detalles)
            .FirstAsync(o => o.Id == result.Id);
        persisted.Estado.Should().Be("BORRADOR");
    }

    [Fact]
    public async Task CrearBorrador_NoDebeDescontarStockBodega()
    {
        // Arrange
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Galletas", stockBodega: 10);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 5 }
            }
        };

        // Act
        await _service.CrearOrdenBorradorAsync(dto);

        // Assert — stock unchanged
        var productoActualizado = await _context.Productos.FindAsync(1);
        productoActualizado!.StockBodega.Should().Be(10);
    }

    [Fact]
    public async Task CrearBorrador_ConIgnorarStockTrue_NoValidaStock()
    {
        // Arrange — low stock (stockBodega=2) but requesting 5, IgnorarStock=true
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Escaso", stockBodega: 2);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            IgnorarStock = true,
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 5 }
            }
        };

        // Act — should NOT throw even though stock is insufficient
        var result = await _service.CrearOrdenBorradorAsync(dto);

        // Assert
        result.Estado.Should().Be("BORRADOR");
        var productoActualizado = await _context.Productos.FindAsync(1);
        productoActualizado!.StockBodega.Should().Be(2, "stock should NOT be deducted for draft orders");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Task 1.2 — RED: ConfirmarOrdenAsync tests
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public async Task ConfirmarOrden_ConEstadoBORRADOR_TransicionaAPENDIENTE_Y_DescuentaStock()
    {
        // Arrange — create BORRADOR order first
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Galletas", stockBodega: 10);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 3 }
            }
        };

        // Crear orden borrador
        var borrador = await _service.CrearOrdenBorradorAsync(dto);

        // Guardar stock antes del Confirmar (debe seguir siendo 10)
        var productoAntes = await _context.Productos.FindAsync(1);
        productoAntes!.StockBodega.Should().Be(10, "stock unchanged by draft creation");

        // Act
        var result = await _service.ConfirmarOrdenAsync(borrador.Id);

        // Assert
        result.Estado.Should().Be("PENDIENTE");
        var productoDespues = await _context.Productos.FindAsync(1);
        productoDespues!.StockBodega.Should().Be(7, "stock deducted on confirmation (10 - 3 = 7)");
    }

    [Fact]
    public async Task ConfirmarOrden_ConStockInsuficiente_Falla_Y_NoDescuentaParcialmente()
    {
        // Arrange
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Escaso", stockBodega: 2);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 5 }
            }
        };

        var borrador = await _service.CrearOrdenBorradorAsync(dto);
        var stockInicial = (await _context.Productos.FindAsync(1))!.StockBodega;

        // Act
        var act = () => _service.ConfirmarOrdenAsync(borrador.Id);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*insuficiente*");

        // Estado should remain BORRADOR
        var orden = await _context.OrdenesCarga
            .Include(o => o.Detalles)
            .FirstAsync(o => o.Id == borrador.Id);
        orden.Estado.Should().Be("BORRADOR");

        // No partial deduction
        var productoFinal = await _context.Productos.FindAsync(1);
        productoFinal!.StockBodega.Should().Be(stockInicial,
            "no partial stock deduction on failed confirmation");
    }

    [Fact]
    public async Task ConfirmarOrden_OrdenNoBORRADOR_Falla()
    {
        // Arrange — create a PENDIENTE order directly
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Normal", stockBodega: 10);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 2 }
            }
        };

        var orden = await _service.CrearOrdenAsync(dto); // PENDIENTE

        // Act
        var act = () => _service.ConfirmarOrdenAsync(orden.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ConfirmarOrden_IdNoExistente_Falla()
    {
        // Act
        var act = () => _service.ConfirmarOrdenAsync(9999);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Task 1.3 — RED: Bug fix — IgnorarStock=true skips deduction
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public async Task CrearOrden_ConIgnorarStockTrue_NoDescuentaStock()
    {
        // Arrange
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Escaso", stockBodega: 2);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            IgnorarStock = true,
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 5 }
            }
        };

        // Act — standard creation with IgnorarStock=true should NOT deduct
        var result = await _service.CrearOrdenAsync(dto);

        // Assert
        result.Estado.Should().Be("PENDIENTE");
        var productoActualizado = await _context.Productos.FindAsync(1);
        productoActualizado!.StockBodega.Should().Be(2,
            "IgnorarStock=true should skip stock deduction (bug fix: guard line 43)");
    }

    [Fact]
    public async Task CrearOrden_ConIgnorarStockFalse_DescuentaStock()
    {
        // Arrange — sufficient stock
        var producto = TestDataHelpers.CreateProducto(id: 1, nombre: "Normal", stockBodega: 10);
        await SeedProducto(producto);

        var dto = new CrearOrdenDto
        {
            IgnorarStock = false,
            Items = new List<DetalleOrdenCargaItemDto>
            {
                new() { ProductoId = 1, Cantidad = 3 }
            }
        };

        // Act
        var result = await _service.CrearOrdenAsync(dto);

        // Assert
        result.Estado.Should().Be("PENDIENTE");
        var productoActualizado = await _context.Productos.FindAsync(1);
        productoActualizado!.StockBodega.Should().Be(7, "10 - 3 = 7 — normal deduction");
    }
}
