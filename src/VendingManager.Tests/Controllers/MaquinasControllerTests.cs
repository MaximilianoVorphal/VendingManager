using FluentAssertions;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Controllers;

/// <summary>
/// Controller-level tests for MaquinasController's H-3 exception-propagation remediation (REQ-ERR-01).
/// ProcesarMovimientos's local try/catch was removed so unhandled exceptions propagate to
/// GlobalProblemDetailsMiddleware instead of leaking ex.Message.
/// </summary>
public class MaquinasControllerTests
{
    private readonly Mock<IMaquinaService> _mockMaquinaService = new();
    private readonly Mock<IAuditService> _mockAudit = new();

    private MaquinasController CreateController()
    {
        return new MaquinasController(_mockMaquinaService.Object, _mockAudit.Object);
    }

    [Fact]
    public async Task ProcesarMovimientos_ServiceThrows_ExceptionPropagates()
    {
        var controller = CreateController();
        var maquina = new Maquina { Id = 1, Nombre = "Test" };

        _mockMaquinaService.Setup(s => s.GetMaquinaAsync(1)).ReturnsAsync(maquina);
        _mockMaquinaService.Setup(s => s.GetSlotsAsync(1)).ReturnsAsync(new List<ConfiguracionSlotDto>());
        _mockMaquinaService
            .Setup(s => s.ProcesarMovimientosLoteAsync(1, It.IsAny<List<SlotActionDto>>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var acciones = new List<SlotActionDto>
        {
            new() { SlotId = 1, ActionType = "REFILL", Cantidad = 5 }
        };

        var act = async () => await controller.ProcesarMovimientos(1, acciones);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
