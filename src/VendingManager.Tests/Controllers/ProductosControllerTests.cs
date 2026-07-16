using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Controllers;

/// <summary>
/// Controller-level tests for ProductosController's H-3 exception-propagation remediation (REQ-ERR-01).
/// SubirCatalogo/ExportarCatalogo: local try/catch removed, exceptions must propagate.
/// AjustarStock: the "not found" NotFound flow-control branch is preserved, but the generic
/// StatusCode(500, ex.Message) tail is replaced by rethrow.
/// </summary>
public class ProductosControllerTests
{
    private readonly Mock<IInventarioService> _mockInventarioService = new();
    private readonly Mock<IAuditService> _mockAudit = new();
    private readonly Mock<IProductoEANRepository> _mockEanRepo = new();

    private ProductosController CreateController()
    {
        return new ProductosController(
            _mockInventarioService.Object,
            _mockAudit.Object,
            _mockEanRepo.Object);
    }

    private static IFormFile CreateMockFormFile(byte[] content, string contentType, string fileName)
    {
        var stream = new MemoryStream(content);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(content.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        return fileMock.Object;
    }

    // ─── SubirCatalogo ──────────────────────────────────────────────────

    [Fact]
    public async Task SubirCatalogo_ServiceThrows_ExceptionPropagates()
    {
        var controller = CreateController();
        var file = CreateMockFormFile(new byte[] { 1, 2, 3 }, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "catalogo.xlsx");

        _mockInventarioService
            .Setup(s => s.ImportarCatalogoAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = async () => await controller.SubirCatalogo(file);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── ExportarCatalogo ───────────────────────────────────────────────

    [Fact]
    public async Task ExportarCatalogo_ServiceThrows_ExceptionPropagates()
    {
        var controller = CreateController();

        _mockInventarioService
            .Setup(s => s.ExportarCatalogoAsync())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = async () => await controller.ExportarCatalogo();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── AjustarStock ───────────────────────────────────────────────────

    [Fact]
    public async Task AjustarStock_NotFoundMessage_ReturnsNotFound()
    {
        var controller = CreateController();
        var dto = new StockUpdateDto { ProductoId = 1, NuevoStock = 10 };
        var producto = new Producto { Id = 1, Nombre = "Test", StockBodega = 5 };

        _mockInventarioService.Setup(s => s.GetProductoAsync(1)).ReturnsAsync(producto);
        _mockInventarioService
            .Setup(s => s.AjustarStockAsync(1, 10))
            .ThrowsAsync(new InvalidOperationException("Producto not found in bodega"));

        var result = await controller.AjustarStock(dto);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task AjustarStock_GenericException_ExceptionPropagates()
    {
        var controller = CreateController();
        var dto = new StockUpdateDto { ProductoId = 1, NuevoStock = 10 };
        var producto = new Producto { Id = 1, Nombre = "Test", StockBodega = 5 };

        _mockInventarioService.Setup(s => s.GetProductoAsync(1)).ReturnsAsync(producto);
        _mockInventarioService
            .Setup(s => s.AjustarStockAsync(1, 10))
            .ThrowsAsync(new InvalidOperationException("unexpected failure"));

        var act = async () => await controller.AjustarStock(dto);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
