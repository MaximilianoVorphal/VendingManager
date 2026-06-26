namespace VendingManager.Tests.Controllers;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

/// <summary>
/// Controller-level tests for ComprasController.
/// Tests HTTP shape (status codes, routing, error mapping) with mocked services.
/// </summary>
public class ComprasControllerTests
{
    private readonly Mock<ICompraService> _mockCompraService;
    private readonly Mock<IFacturaOcrService> _mockOcrService;
    private readonly ComprasController _controller;

    public ComprasControllerTests()
    {
        _mockCompraService = new Mock<ICompraService>();
        _mockOcrService = new Mock<IFacturaOcrService>();
        _controller = new ComprasController(_mockCompraService.Object, _mockOcrService.Object);
    }

    // ─── ReasignarProveedor (T30/T31) ──────────────────────────────────

    [Fact]
    public async Task ReasignarProveedor_WithExistingCatalogId_ReturnsNoContent()
    {
        // Arrange
        var compra = new Compra
        {
            Id = 1,
            Proveedor = "Raw Supplier",
            ProveedorCatalog = new ProveedorCatalog { Id = 10, NombreCanonical = "Supplier" }
        };

        _mockCompraService
            .Setup(s => s.ReasignarProveedorAsync(1, It.IsAny<ReasignarProveedorRequestDto>()))
            .ReturnsAsync(compra);

        var request = new ReasignarProveedorRequestDto
        {
            ProveedorCatalogId = 10
        };

        // Act
        var result = await _controller.ReasignarProveedor(1, request);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mockCompraService.Verify(
            s => s.ReasignarProveedorAsync(1, It.Is<ReasignarProveedorRequestDto>(
                r => r.ProveedorCatalogId == 10)),
            Times.Once);
    }

    [Fact]
    public async Task ReasignarProveedor_WithNewCanonicalName_ReturnsNoContent()
    {
        // Arrange
        var compra = new Compra
        {
            Id = 2,
            Proveedor = "Unknown Supplier",
            ProveedorCatalog = new ProveedorCatalog { Id = 20, NombreCanonical = "New Supplier" }
        };

        _mockCompraService
            .Setup(s => s.ReasignarProveedorAsync(2, It.IsAny<ReasignarProveedorRequestDto>()))
            .ReturnsAsync(compra);

        var request = new ReasignarProveedorRequestDto
        {
            NuevoNombreCanonical = "New Supplier"
        };

        // Act
        var result = await _controller.ReasignarProveedor(2, request);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mockCompraService.Verify(
            s => s.ReasignarProveedorAsync(2, It.Is<ReasignarProveedorRequestDto>(
                r => r.NuevoNombreCanonical == "New Supplier")),
            Times.Once);
    }

    [Fact]
    public async Task ReasignarProveedor_WithNonExistentCompra_Returns404()
    {
        // Arrange
        _mockCompraService
            .Setup(s => s.ReasignarProveedorAsync(999, It.IsAny<ReasignarProveedorRequestDto>()))
            .ThrowsAsync(new KeyNotFoundException("Compra 999 no encontrada."));

        var request = new ReasignarProveedorRequestDto
        {
            ProveedorCatalogId = 10
        };

        // Act
        var result = await _controller.ReasignarProveedor(999, request);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
