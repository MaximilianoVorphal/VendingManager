using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Web.Controllers;
using Xunit;

namespace VendingManager.Tests.Controllers;

/// <summary>
/// Controller-level tests for CajaController's H-3 exception-propagation remediation (REQ-ERR-01).
/// RegistrarMovimiento keeps its typed ArgumentException/InvalidOperationException -> BadRequest
/// catches; only the generic catch(Exception) tail was removed. UploadImage and ExportarCaja had
/// their try/catch wrappers removed entirely, so exceptions now propagate to
/// GlobalProblemDetailsMiddleware instead of leaking ex.Message.
/// </summary>
public class CajaControllerTests
{
    private readonly Mock<ICajaService> _mockCajaService = new();
    private readonly Mock<IInventarioService> _mockInventarioService = new();
    private readonly Mock<IAuditService> _mockAudit = new();

    private CajaController CreateController()
    {
        return new CajaController(_mockCajaService.Object, _mockInventarioService.Object, _mockAudit.Object);
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

    // ─── RegistrarMovimiento ────────────────────────────────────────────

    [Fact]
    public async Task RegistrarMovimiento_ArgumentException_ReturnsBadRequest()
    {
        var controller = CreateController();
        var mov = new MovimientoCaja { Descripcion = "Test", Monto = 100 };

        _mockCajaService
            .Setup(s => s.RegistrarMovimientoAsync(It.IsAny<MovimientoCaja>()))
            .ThrowsAsync(new ArgumentException("invalid"));

        var result = await controller.RegistrarMovimiento(mov);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RegistrarMovimiento_InvalidOperationException_ReturnsBadRequest()
    {
        var controller = CreateController();
        var mov = new MovimientoCaja { Descripcion = "Test", Monto = 100 };

        _mockCajaService
            .Setup(s => s.RegistrarMovimientoAsync(It.IsAny<MovimientoCaja>()))
            .ThrowsAsync(new InvalidOperationException("month locked"));

        var result = await controller.RegistrarMovimiento(mov);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RegistrarMovimiento_GenericException_ExceptionPropagates()
    {
        var controller = CreateController();
        var mov = new MovimientoCaja { Descripcion = "Test", Monto = 100 };

        _mockCajaService
            .Setup(s => s.RegistrarMovimientoAsync(It.IsAny<MovimientoCaja>()))
            .ThrowsAsync(new InvalidCastException("boom"));

        var act = async () => await controller.RegistrarMovimiento(mov);

        await act.Should().ThrowAsync<InvalidCastException>();
    }

    // ─── UploadImage ────────────────────────────────────────────────────

    [Fact]
    public async Task UploadImage_ServiceThrows_ExceptionPropagates()
    {
        var controller = CreateController();
        var file = CreateMockFormFile(new byte[] { 1, 2, 3 }, "image/jpeg", "comprobante.jpg");

        _mockCajaService
            .Setup(s => s.UploadComprobanteAsync(It.IsAny<Stream>(), It.IsAny<string>(), null, It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = async () => await controller.UploadImage(file, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── ExportarCaja ───────────────────────────────────────────────────

    [Fact]
    public async Task ExportarCaja_ServiceThrows_ExceptionPropagates()
    {
        var controller = CreateController();

        _mockCajaService
            .Setup(s => s.ExportarCajaAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = async () => await controller.ExportarCaja(null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
