namespace VendingManager.Tests.Controllers;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Tests.TestData;

/// <summary>
/// Controller-level tests for ProveedoresController.
/// Tests HTTP shape (status codes, routing, error mapping) with mocked service.
/// </summary>
public class ProveedoresControllerTests
{
    private readonly ApplicationDbContext _context;
    private readonly ProveedoresController _controller;

    public ProveedoresControllerTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"ProveedoresControllerTest_{Guid.NewGuid()}");
        _controller = new ProveedoresController(_context);
    }

    [Fact]
    public async Task GetProveedores_ReturnsListOrderedByNombreCanonical()
    {
        // Arrange
        _context.ProveedorCatalog.AddRange(
            new ProveedorCatalog { NombreCanonical = "Zeta Supplier" },
            new ProveedorCatalog { NombreCanonical = "Alpha Supplier" },
            new ProveedorCatalog { NombreCanonical = "Beta Supplier" }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetProveedores();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<ProveedorCatalogDto>>().Subject;
        list.Should().HaveCount(3);
        list[0].NombreCanonical.Should().Be("Alpha Supplier");
        list[1].NombreCanonical.Should().Be("Beta Supplier");
        list[2].NombreCanonical.Should().Be("Zeta Supplier");
    }

    [Fact]
    public async Task GetProveedores_WhenEmpty_ReturnsEmptyList()
    {
        // Act
        var result = await _controller.GetProveedores();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<List<ProveedorCatalogDto>>().Subject;
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateProveedor_WithValidName_Returns201WithDto()
    {
        // Arrange
        var request = new CrearProveedorRequestDto { NombreCanonical = "New Supplier" };

        // Act
        var result = await _controller.CreateProveedor(request);

        // Assert
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(ProveedoresController.GetProveedores));
        var dto = createdResult.Value.Should().BeAssignableTo<ProveedorCatalogDto>().Subject;
        dto.Id.Should().BeGreaterThan(0);
        dto.NombreCanonical.Should().Be("New Supplier");

        // Verify persistence
        var saved = await _context.ProveedorCatalog.FindAsync(dto.Id);
        saved.Should().NotBeNull();
        saved!.NombreCanonical.Should().Be("New Supplier");
    }

    [Fact]
    public async Task CreateProveedor_WithDuplicateName_Returns409Conflict()
    {
        // Arrange
        _context.ProveedorCatalog.Add(new ProveedorCatalog { NombreCanonical = "Existing" });
        await _context.SaveChangesAsync();

        var request = new CrearProveedorRequestDto { NombreCanonical = "Existing" };

        // Act
        var result = await _controller.CreateProveedor(request);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateProveedor_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CrearProveedorRequestDto { NombreCanonical = "" };

        // Act
        var result = await _controller.CreateProveedor(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
