namespace VendingManager.Tests.Controllers;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VendingManager.Controllers;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

public class TemplateRecargaController_LifecycleTests
{
    private readonly Mock<ITemplateRecargaService> _mockFacade;
    private readonly Mock<ITemplateRecargaLifecycleService> _mockLifecycle;
    private readonly Mock<ITemplateRecargaAnalyticsService> _mockAnalytics;
    private readonly TemplateRecargaController _controller;

    public TemplateRecargaController_LifecycleTests()
    {
        _mockFacade = new Mock<ITemplateRecargaService>();
        _mockLifecycle = new Mock<ITemplateRecargaLifecycleService>();
        _mockAnalytics = new Mock<ITemplateRecargaAnalyticsService>();
        _controller = new TemplateRecargaController(_mockFacade.Object, _mockLifecycle.Object, _mockAnalytics.Object);
    }

    private static TemplateRecargaDto MakeDto(int id, EstadoTemplate estado)
    {
        return new TemplateRecargaDto
        {
            Id = id,
            Nombre = "Template " + id,
            Estado = estado,
            FechaCreacion = DateTime.Now,
            Periodos = new List<PeriodoRecargaDto>()
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Task 3.1: State transition endpoints
    // StartLoadingAsync (iniciar-carga) and FinalizeAsync (finalizar-carga) removed
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Terminar_ValidTransition_Returns200WithUpdatedTemplate()
    {
        var dto = MakeDto(1, EstadoTemplate.Terminado);
        _mockLifecycle.Setup(s => s.TerminarAsync(1)).ReturnsAsync(dto);

        var result = await _controller.Terminar(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeOfType<TemplateRecargaDto>();
        ((TemplateRecargaDto)ok.Value!).Estado.Should().Be(EstadoTemplate.Terminado);
    }

    [Fact]
    public async Task Terminar_InvalidState_Returns400()
    {
        _mockLifecycle.Setup(s => s.TerminarAsync(1))
            .ThrowsAsync(new InvalidOperationException("No se puede terminar: el template está en estado Terminado"));

        var result = await _controller.Terminar(1);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Terminar_NotFound_Returns404()
    {
        _mockLifecycle.Setup(s => s.TerminarAsync(999))
            .ThrowsAsync(new InvalidOperationException("Template con ID 999 no encontrado"));

        var result = await _controller.Terminar(999);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Reabrir_ValidTransition_Returns200WithUpdatedTemplate()
    {
        var dto = MakeDto(1, EstadoTemplate.Pendiente);
        _mockLifecycle.Setup(s => s.ReabrirAsync(1)).ReturnsAsync(dto);

        var result = await _controller.Reabrir(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeOfType<TemplateRecargaDto>();
        ((TemplateRecargaDto)ok.Value!).Estado.Should().Be(EstadoTemplate.Pendiente);
    }

    [Fact]
    public async Task Reabrir_NotFound_Returns404()
    {
        _mockLifecycle.Setup(s => s.ReabrirAsync(999))
            .ThrowsAsync(new InvalidOperationException("Template con ID 999 no encontrado"));

        var result = await _controller.Reabrir(999);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // IniciarCarga and FinalizarCarga endpoints removed
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IniciarCarga_Endpoint_Removed()
    {
        // The controller should NOT have an IniciarCarga method
        typeof(TemplateRecargaController).GetMethod("IniciarCarga").Should().BeNull();
    }

    [Fact]
    public void FinalizarCarga_Endpoint_Removed()
    {
        // The controller should NOT have a FinalizarCarga method
        typeof(TemplateRecargaController).GetMethod("FinalizarCarga").Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Task 3.2: Slot batch actions endpoint
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SlotBatch_ValidRefill_ReturnsOkWithUpdatedSlots()
    {
        var request = new SlotBatchRequest
        {
            Actions = new List<SlotActionDto>
            {
                new() { SlotId = 10, ActionType = "REFILL", Cantidad = 50 }
            }
        };

        var response = new SlotBatchResponse { ProcessedCount = 1, Errors = new List<string>() };
        var actionsList = request.Actions;
        _mockFacade.Setup(s => s.ApplySlotBatchAsync(1, 100, actionsList)).ReturnsAsync(response);

        var result = await _controller.SlotBatch(1, 100, request);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeOfType<SlotBatchResponse>();
        ((SlotBatchResponse)ok.Value!).ProcessedCount.Should().Be(1);
    }

    [Fact]
    public async Task SlotBatch_EmptyActionsList_ReturnsBadRequest()
    {
        var request = new SlotBatchRequest { Actions = new List<SlotActionDto>() };

        var result = await _controller.SlotBatch(1, 100, request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SlotBatch_TemplateNotFound_Returns404()
    {
        var request = new SlotBatchRequest
        {
            Actions = new List<SlotActionDto>
            {
                new() { SlotId = 10, ActionType = "REFILL", Cantidad = 50 }
            }
        };

        _mockFacade.Setup(s => s.ApplySlotBatchAsync(999, 100, It.IsAny<List<SlotActionDto>>()))
            .ThrowsAsync(new KeyNotFoundException("Template con ID 999 no encontrado"));

        var result = await _controller.SlotBatch(999, 100, request);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SlotBatch_InvalidActionType_ReturnsBadRequest()
    {
        var request = new SlotBatchRequest
        {
            Actions = new List<SlotActionDto>
            {
                new() { SlotId = 10, ActionType = "INVALID", Cantidad = 50 }
            }
        };

        _mockFacade.Setup(s => s.ApplySlotBatchAsync(1, 100, It.IsAny<List<SlotActionDto>>()))
            .ThrowsAsync(new InvalidOperationException("Tipo de acción inválido: INVALID. Valores válidos: REFILL, EMPTY, SWAP"));

        var result = await _controller.SlotBatch(1, 100, request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Task 3.3: DTO naming convention tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SlotBatchRequest_HasCorrectStructure()
    {
        var request = new SlotBatchRequest();
        request.Actions.Should().NotBeNull();
        request.Actions.Should().BeEmpty();
    }

    [Fact]
    public void SlotBatchResponse_HasCorrectStructure()
    {
        var response = new SlotBatchResponse();
        response.ProcessedCount.Should().Be(0);
        response.Errors.Should().NotBeNull();
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TemplateEstadoResponse_HasCorrectStructure()
    {
        var response = new TemplateEstadoResponse { TemplateId = 1, Estado = EstadoTemplate.Terminado };
        response.TemplateId.Should().Be(1);
        response.Estado.Should().Be(EstadoTemplate.Terminado);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // UC-3: EMPTY tool clears slot (API-level slot-batch with ActionType="EMPTY")
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SlotBatch_EmptyAction_ResetsSlot()
    {
        var request = new SlotBatchRequest
        {
            Actions = new List<SlotActionDto>
            {
                new() { SlotId = 10, ActionType = "EMPTY", Cantidad = 0 }
            }
        };

        var response = new SlotBatchResponse { ProcessedCount = 1, Errors = new List<string>() };
        _mockFacade.Setup(s => s.ApplySlotBatchAsync(1, 100, It.IsAny<List<SlotActionDto>>()))
            .ReturnsAsync(response);

        var result = await _controller.SlotBatch(1, 100, request);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var batchResponse = (SlotBatchResponse)ok.Value!;
        batchResponse.ProcessedCount.Should().Be(1);
        batchResponse.Errors.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // UC-4: SWAP tool changes product (API-level slot-batch with ActionType="SWAP")
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SlotBatch_SwapAction_ChangesProduct()
    {
        var request = new SlotBatchRequest
        {
            Actions = new List<SlotActionDto>
            {
                new() { SlotId = 10, ActionType = "SWAP", NewProductoId = 42, Cantidad = 0 }
            }
        };

        var response = new SlotBatchResponse { ProcessedCount = 1, Errors = new List<string>() };
        _mockFacade.Setup(s => s.ApplySlotBatchAsync(1, 100, It.IsAny<List<SlotActionDto>>()))
            .ReturnsAsync(response);

        var result = await _controller.SlotBatch(1, 100, request);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var batchResponse = (SlotBatchResponse)ok.Value!;
        batchResponse.ProcessedCount.Should().Be(1);
    }
}