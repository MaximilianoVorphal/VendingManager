namespace VendingManager.Tests.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using VendingManager.Core.Configuration;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

/// <summary>
/// Characterization tests for CompraService CPP calculations.
/// Freezes the expected numeric output — must pass byte-identical after
/// delegating to CalculadoraCostos.
/// </summary>
public class CompraService_CppCentralization_Tests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly CompraService _service;

    public CompraService_CppCentralization_Tests()
    {
        _context = TestDataHelpers.CreateInMemoryContext(
            $"CppCentralizationTestDb_{Guid.NewGuid()}");

        var mockEnv = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        mockEnv.SetupGet(e => e.WebRootPath).Returns("/tmp/wwwroot");
        mockEnv.SetupGet(e => e.ContentRootPath).Returns("/tmp");

        var config = Options.Create(new VendingConfig());

        var mockProductMatching = new Mock<IProductMatchingService>();
        mockProductMatching
            .Setup(m => m.SaveMappingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        var mockProveedorMatching = new Mock<IProveedorMatchingService>();
        mockProveedorMatching
            .Setup(m => m.SaveAliasAsync(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var uploadProvider = new DefaultUploadPathProvider(mockEnv.Object, config);

        _service = new CompraService(
            _context,
            mockProductMatching.Object,
            uploadProvider,
            mockProveedorMatching.Object);
    }

    public void Dispose() => _context.Dispose();

    // ── RegistrarCompraAsync ──────────────────────────────────────────

    [Fact]
    public async Task RegistrarCompraAsync_FirstPurchase_SetsCppToUnitCost()
    {
        var producto = TestDataHelpers.CreateProducto(id: 1, costoPromedio: 0, stockBodega: 0);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var compra = CreateCompraWithDetalle(productoId: 1, cantidad: 10, costoUnitario: 400);

        // Use the non-mapping overload to avoid async issues
        var result = await RegistrarCompraDirectAsync(compra);

        var updatedProducto = await _context.Productos.FindAsync(1);
        updatedProducto!.StockBodega.Should().Be(10);
        updatedProducto.CostoPromedio.Should().BeApproximately(400m, 0.001m);
    }

    [Fact]
    public async Task RegistrarCompraAsync_SubsequentPurchase_WeightedAvgCpp()
    {
        var producto = TestDataHelpers.CreateProducto(id: 1, costoPromedio: 500, stockBodega: 10);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var compra = CreateCompraWithDetalle(productoId: 1, cantidad: 5, costoUnitario: 600);

        var result = await RegistrarCompraDirectAsync(compra);

        var updatedProducto = await _context.Productos.FindAsync(1);
        updatedProducto!.StockBodega.Should().Be(15);
        // (10*500 + 5*600) / 15 = (5000 + 3000) / 15 = 533.333...
        updatedProducto.CostoPromedio.Should().BeApproximately(533.333m, 0.001m);
    }

    [Fact]
    public async Task RevertirImpactoInventario_StockStillPositive_RecalculatesCpp()
    {
        var producto = TestDataHelpers.CreateProducto(id: 1, costoPromedio: 533.333m, stockBodega: 15);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        // Delete a compra that was responsible for 5 units @ 600 c/u
        var compra = CreateCompraWithDetalle(productoId: 1, cantidad: 5, costoUnitario: 600);
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        // EliminarCompraAsync → RevertirImpactoInventario
        await _service.EliminarCompraAsync(compra.Id);

        var updatedProducto = await _context.Productos.FindAsync(1);
        updatedProducto!.StockBodega.Should().Be(10);
        // Revert 5@600: (15*533.333 - 5*600) / 10 = (8000 - 3000) / 10 = 500
        updatedProducto.CostoPromedio.Should().BeApproximately(500m, 0.01m);
    }

    [Fact]
    public async Task RevertirImpactoInventario_StockReachesZero_ResetsToZero()
    {
        var producto = TestDataHelpers.CreateProducto(id: 1, costoPromedio: 533.333m, stockBodega: 5);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var compra = CreateCompraWithDetalle(productoId: 1, cantidad: 10, costoUnitario: 600);
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        await _service.EliminarCompraAsync(compra.Id);

        var updatedProducto = await _context.Productos.FindAsync(1);
        updatedProducto!.StockBodega.Should().Be(0);
        updatedProducto.CostoPromedio.Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private Compra CreateCompraWithDetalle(
        int productoId,
        int cantidad,
        decimal costoUnitario,
        string estado = "PENDIENTE")
    {
        return new Compra
        {
            Proveedor = "Test Supplier",
            NumeroDocumento = "TEST-001",
            FechaCompra = new DateTime(2026, 7, 1),
            Estado = estado,
            TipoFactura = "MERCADERIA",
            PagadaCaja = false,
            Detalles = new List<DetalleCompra>
            {
                new DetalleCompra
                {
                    ProductoId = productoId,
                    DescripcionItem = "Test Item",
                    Cantidad = cantidad,
                    CostoUnitario = costoUnitario,
                    Subtotal = cantidad * costoUnitario,
                    EsPendiente = false
                }
            }
        };
    }

    /// <summary>
    /// Calls RegistrarCompraAsync the same way the controller does.
    /// We need to bypass the service's internal SaveChangesAsync calls
    /// in a way that works with in-memory EF.
    /// </summary>
    private async Task<Compra> RegistrarCompraDirectAsync(Compra compra)
    {
        // The service calls SaveChangesAsync internally multiple times.
        // In SQLite in-memory this is fine — the context supports it.
        var result = await _service.RegistrarCompraAsync(compra);
        return result;
    }
}
