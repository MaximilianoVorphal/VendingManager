using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Services;

public class VentasServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly VentasService _service;

    public VentasServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _service = new VentasService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task GetProductosAsync_IncludesPrecioVenta()
    {
        // Arrange
        _context.Productos.Add(new Producto { Id = 1, Nombre = "Snickers", PrecioVenta = 1200 });
        _context.Productos.Add(new Producto { Id = 2, Nombre = "Coca Cola", PrecioVenta = 700 });
        _context.Productos.Add(new Producto { Id = 3, Nombre = "Agua", PrecioVenta = 0 });
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetProductosAsync();

        // Assert
        var snickers = result.First(p => p.Id == 1);
        snickers.PrecioVenta.Should().Be(1200);

        var coca = result.First(p => p.Id == 2);
        coca.PrecioVenta.Should().Be(700);

        var agua = result.First(p => p.Id == 3);
        agua.PrecioVenta.Should().Be(0);
    }

    [Fact]
    public async Task GetProductosAsync_OrdersByNombre()
    {
        // Arrange
        _context.Productos.Add(new Producto { Id = 1, Nombre = "Zapato" });
        _context.Productos.Add(new Producto { Id = 2, Nombre = "Agua" });
        _context.Productos.Add(new Producto { Id = 3, Nombre = "Mesita" });
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetProductosAsync();

        // Assert
        result.Select(p => p.Nombre).Should().Equal("Agua", "Mesita", "Zapato");
    }
}
