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
    private readonly TemplateRecargaController _controller;

    public TemplateRecargaController_LifecycleTests()
    {
        _mockFacade = new Mock<ITemplateRecargaService>();
        _mockLifecycle = new Mock<ITemplateRecargaLifecycleService>();
        _controller = new TemplateRecargaController(_mockFacade.Object, _mockLifecycle.Object);
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
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IniciarCarga_ValidTransition_Returns200WithUpdatedTemplate()
    {
        var dto = MakeDto(1, EstadoTemplate.EnCarga);
        _mockLifecycle.Setup(s => s.StartLoadingAsync(1)).ReturnsAsync(dto);

        var result = await _controller.IniciarCarga(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeOfType<TemplateRecargaDto>();
        ((TemplateRecargaDto)ok.Value!).Estado.Should().Be(EstadoTemplate.EnCarga);
    }

    [Fact]
    public async Task IniciarCarga_InvalidState_Returns400()
    {
        _mockLifecycle.Setup(s => s.StartLoadingAsync(1))
            .ThrowsAsync(new InvalidOperationException("No se puede iniciar carga: el template está en estado Activo"));

        var result = await _controller.IniciarCarga(1);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task IniciarCarga_NotFound_Returns404()
    {
        _mockLifecycle.Setup(s => s.StartLoadingAsync(999))
            .ThrowsAsync(new InvalidOperationException("Template con ID 999 no encontrado"));

        var result = await _controller.IniciarCarga(999);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task FinalizarCarga_ValidTransition_Returns200WithUpdatedTemplate()
    {
        var dto = MakeDto(1, EstadoTemplate.Activo);
        _mockLifecycle.Setup(s => s.FinalizeAsync(1)).ReturnsAsync(dto);

        var result = await _controller.FinalizarCarga(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeOfType<TemplateRecargaDto>();
        ((TemplateRecargaDto)ok.Value!).Estado.Should().Be(EstadoTemplate.Activo);
    }

    [Fact]
    public async Task FinalizarCarga_InvalidState_Returns400()
    {
        _mockLifecycle.Setup(s => s.FinalizeAsync(1))
            .ThrowsAsync(new InvalidOperationException("No se puede finalizar carga: el template está en estado Borrador"));

        var result = await _controller.FinalizarCarga(1);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task FinalizarCarga_NotFound_Returns404()
    {
        _mockLifecycle.Setup(s => s.FinalizeAsync(999))
            .ThrowsAsync(new InvalidOperationException("Template con ID 999 no encontrado"));

        var result = await _controller.FinalizarCarga(999);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Cerrar_ValidTransition_Returns200WithUpdatedTemplate()
    {
        var dto = MakeDto(1, EstadoTemplate.Cerrado);
        _mockLifecycle.Setup(s => s.CloseAsync(1)).ReturnsAsync(dto);

        var result = await _controller.Cerrar(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeOfType<TemplateRecargaDto>();
        ((TemplateRecargaDto)ok.Value!).Estado.Should().Be(EstadoTemplate.Cerrado);
    }

    [Fact]
    public async Task Cerrar_InvalidState_Returns400()
    {
        _mockLifecycle.Setup(s => s.CloseAsync(1))
            .ThrowsAsync(new InvalidOperationException("No se puede cerrar: el template está en estado Borrador"));

        var result = await _controller.Cerrar(1);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Reabrir_ValidTransition_Returns200WithUpdatedTemplate()
    {
        var dto = MakeDto(1, EstadoTemplate.Borrador);
        _mockLifecycle.Setup(s => s.ResetToDraftAsync(1)).ReturnsAsync(dto);

        var result = await _controller.Reabrir(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeOfType<TemplateRecargaDto>();
        ((TemplateRecargaDto)ok.Value!).Estado.Should().Be(EstadoTemplate.Borrador);
    }

    [Fact]
    public async Task Reabrir_NotFound_Returns404()
    {
        _mockLifecycle.Setup(s => s.ResetToDraftAsync(999))
            .ThrowsAsync(new InvalidOperationException("Template con ID 999 no encontrado"));

        var result = await _controller.Reabrir(999);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
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
        var response = new TemplateEstadoResponse { TemplateId = 1, Estado = EstadoTemplate.EnCarga };
        response.TemplateId.Should().Be(1);
        response.Estado.Should().Be(EstadoTemplate.EnCarga);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TL-6: SLOTS_REQUIRED — zero slots → 400 Bad Request
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IniciarCarga_NoSlotsConfigured_ReturnsBadRequest()
    {
        // Template has periods but zero SnapshotSlots → should reject Borrador→EnCarga
        var dto = MakeDto(1, EstadoTemplate.Borrador);
        _mockLifecycle.Setup(s => s.StartLoadingAsync(1))
            .ThrowsAsync(new InvalidOperationException(
                "No se puede iniciar carga: el template no tiene slots configurados. Agregue al menos un slot antes de iniciar."));

        var result = await _controller.IniciarCarga(1);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().NotBeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TL-7: RowVersion concurrency — second finalize gets 409 Conflict
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FinalizarCarga_ConcurrentRequest_ReturnsConflict()
    {
        // First request succeeds (EnCarga → Activo), second concurrent request on
        // same template should get 409 Conflict due to RowVersion mismatch.
        // The controller maps DbUpdateConcurrencyException → 409 Conflict.
        var dto = MakeDto(1, EstadoTemplate.Activo);
        _mockLifecycle.Setup(s => s.FinalizeAsync(1))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                "RowVersion conflict: el registro fue modificado por otro usuario."));

        var result = await _controller.FinalizarCarga(1);

        result.Result.Should().BeOfType<ConflictObjectResult>();
        var conflict = (ConflictObjectResult)result.Result!;
        conflict.Value.Should().NotBeNull();
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