namespace VendingManager.Tests.Controllers;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

/// <summary>
/// Controller-level tests for OrdenCargaController.Confirmar endpoint.
/// TDD RED phase — tests MUST fail before endpoint implementation.
/// </summary>
public class OrdenCargaControllerTests
{
    private readonly Mock<IOrdenCargaService> _mockService;
    private readonly OrdenCargaController _controller;

    public OrdenCargaControllerTests()
    {
        _mockService = new Mock<IOrdenCargaService>();
        // The controller requires IOrdenCargaExcelService and IRecargaOcrService too.
        // Those are only used for Excel/Ocr endpoints and not needed for confirmar tests,
        // but we must provide non-null mocks to satisfy the constructor.
        var mockExcelService = new Mock<IOrdenCargaExcelService>();
        var mockOcrService = new Mock<IRecargaOcrService>();

        _controller = new OrdenCargaController(
            _mockService.Object,
            mockExcelService.Object,
            mockOcrService.Object);
    }

    // ─── Confirmar endpoint (task 1.4) ─────────────────────────────────────

    [Fact]
    public async Task Confirmar_OrdenValida_Retorna200()
    {
        // Arrange
        var dtoEsperado = new OrdenCargaDto
        {
            Id = 1,
            Estado = "PENDIENTE",
            Nombre = "Test Order"
        };
        _mockService.Setup(s => s.ConfirmarOrdenAsync(1))
                    .ReturnsAsync(dtoEsperado);

        // Act
        var result = await _controller.Confirmar(1);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        okResult.Value.Should().Be(dtoEsperado);
    }

    [Fact]
    public async Task Confirmar_OrdenNoBORRADOR_Retorna400()
    {
        // Arrange
        _mockService.Setup(s => s.ConfirmarOrdenAsync(5))
                    .ThrowsAsync(new InvalidOperationException("La orden no está en estado BORRADOR."));

        // Act
        var result = await _controller.Confirmar(5);

        // Assert
        var badResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Confirmar_OrdenNoExistente_Retorna404()
    {
        // Arrange
        _mockService.Setup(s => s.ConfirmarOrdenAsync(999))
                    .ThrowsAsync(new Exception("Orden no encontrada."));

        // Act
        var result = await _controller.Confirmar(999);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}
