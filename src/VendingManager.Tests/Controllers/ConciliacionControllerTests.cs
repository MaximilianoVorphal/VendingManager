namespace VendingManager.Tests.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

/// <summary>
/// Controller-level tests for the new TASK-11 endpoints in ContabilidadController.
/// Tests the HTTP shape (status codes, routing, error mapping) using mocked services.
/// </summary>
public class ConciliacionControllerTests
{
    private readonly Mock<IContabilidadService> _mockService;
    private readonly Mock<ITransferenciaService> _mockTransferenciaService;
    private readonly ContabilidadController _controller;

    public ConciliacionControllerTests()
    {
        _mockService = new Mock<IContabilidadService>();
        _mockTransferenciaService = new Mock<ITransferenciaService>();
        var mockIntegrityCheck = new Mock<IIntegrityCheckService>();
        var mockLogger = new Mock<ILogger<ContabilidadController>>();
        _controller = new ContabilidadController(
            _mockService.Object,
            _mockTransferenciaService.Object,
            mockIntegrityCheck.Object,
            mockLogger.Object);
    }

    // ── VerificarTransferencia ────────────────────────────────────────────

    [Fact]
    public async Task VerificarTransferencia_CallsService_Returns204()
    {
        // Arrange
        _mockService
            .Setup(s => s.MarcarTransferenciaVerificadaAsync(1, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.VerificarTransferencia(1);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mockService.Verify(s => s.MarcarTransferenciaVerificadaAsync(1, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerificarTransferencia_NotFound_Returns404()
    {
        // Arrange
        _mockService
            .Setup(s => s.MarcarTransferenciaVerificadaAsync(999, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Transferencia 999 no encontrada."));

        // Act
        var result = await _controller.VerificarTransferencia(999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DesverificarTransferencia_CallsService_WithFalse_Returns204()
    {
        // Arrange
        _mockService
            .Setup(s => s.MarcarTransferenciaVerificadaAsync(1, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.DesverificarTransferencia(1);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mockService.Verify(s => s.MarcarTransferenciaVerificadaAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── VerificarCompra ───────────────────────────────────────────────────

    [Fact]
    public async Task VerificarCompra_CallsService_Returns204()
    {
        // Arrange
        _mockService
            .Setup(s => s.MarcarCompraVerificadaAsync(5, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.VerificarCompra(5);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mockService.Verify(s => s.MarcarCompraVerificadaAsync(5, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DesverificarCompra_CallsService_WithFalse_Returns204()
    {
        // Arrange
        _mockService
            .Setup(s => s.MarcarCompraVerificadaAsync(5, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.DesverificarCompra(5);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task VerificarCompra_NotFound_Returns404()
    {
        // Arrange
        _mockService
            .Setup(s => s.MarcarCompraVerificadaAsync(999, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Compra 999 no encontrada."));

        // Act
        var result = await _controller.VerificarCompra(999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── RegistrarDevolucion ───────────────────────────────────────────────

    [Fact]
    public async Task RegistrarDevolucion_ValidRequest_Returns201()
    {
        // Arrange
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = 1,
            Trabajador = "Juan",
            Monto = 500m,
            Fecha = DateTime.Today
        };
        var dto = new DevolucionDto { Id = 42, Monto = 500m, PeriodoId = 1 };

        _mockService
            .Setup(s => s.RegistrarDevolucionAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        // Act
        var result = await _controller.RegistrarDevolucion(request);

        // Assert
        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.Value.Should().BeEquivalentTo(dto);
    }

    [Fact]
    public async Task RegistrarDevolucion_NoPeriodoNorRendicion_Returns400()
    {
        // Arrange — both null → controller early-validates
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = null,
            RendicionId = null,
            Trabajador = "Juan",
            Monto = 100m,
            Fecha = DateTime.Today
        };

        // Act
        var result = await _controller.RegistrarDevolucion(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _mockService.Verify(s => s.RegistrarDevolucionAsync(It.IsAny<RegistrarDevolucionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarDevolucion_MontoZero_Returns400()
    {
        // Arrange — Monto = 0 → controller early-validates
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = 1,
            Trabajador = "Juan",
            Monto = 0m,
            Fecha = DateTime.Today
        };

        // Act
        var result = await _controller.RegistrarDevolucion(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _mockService.Verify(s => s.RegistrarDevolucionAsync(It.IsAny<RegistrarDevolucionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarDevolucion_ServiceThrowsInvalidOp_Returns400()
    {
        // Arrange
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = 1,
            Trabajador = "Juan",
            Monto = 200m,
            Fecha = DateTime.Today
        };
        _mockService
            .Setup(s => s.RegistrarDevolucionAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ya existe una devolución."));

        // Act
        var result = await _controller.RegistrarDevolucion(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RegistrarDevolucion_ServiceThrowsNotFound_Returns404()
    {
        // Arrange
        var request = new RegistrarDevolucionRequest
        {
            PeriodoId = 999,
            Trabajador = "Juan",
            Monto = 200m,
            Fecha = DateTime.Today
        };
        _mockService
            .Setup(s => s.RegistrarDevolucionAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Período 999 no encontrado."));

        // Act
        var result = await _controller.RegistrarDevolucion(request);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── FIX-4 (WARNING-3): 409 concurrency mapping ───────────────────────

    [Fact]
    public async Task VerificarTransferencia_DbUpdateConcurrencyException_Returns409()
    {
        // Arrange — mock MarcarTransferenciaVerificadaAsync to throw concurrency exception
        _mockService
            .Setup(s => s.MarcarTransferenciaVerificadaAsync(1, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                "Row version conflict", new List<Microsoft.EntityFrameworkCore.Update.IUpdateEntry>()));

        // Act
        var result = await _controller.VerificarTransferencia(1);

        // Assert
        result.Should().BeOfType<ConflictObjectResult>();
    }
}
