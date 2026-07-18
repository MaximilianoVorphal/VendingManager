using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendingManager.Shared.Constants;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class TemplateRecargaController(
    ITemplateRecargaService service,
    ITemplateRecargaLifecycleService lifecycleService,
    ITemplateRecargaAnalyticsService analyticsService) : ControllerBase
{
    /// <summary>Obtener todos los templates de recarga</summary>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TemplateRecargaDto>>> GetAll()
    {
        var result = await service.GetAllAsync();
        return Ok(result);
    }

    /// <summary>
    /// Obtener lista ligera de templates (sin periodos anidados).
    /// Para la vista de lista del módulo RecargaMovil.
    /// </summary>
    [HttpGet("list")]
    public async Task<ActionResult<List<TemplateRecargaListItemDto>>> GetList()
    {
        var result = await service.GetAllListAsync();
        return Ok(result);
    }

    /// <summary>
    /// Termina un template: Pendiente (0) → Terminado (2).
    /// El template queda como completado, fuente para stock-critico.
    /// </summary>
    [HttpPost("{id}/terminar")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<TemplateRecargaDto>> Terminar(int id)
    {
        try
        {
            var template = await lifecycleService.TerminarAsync(id);
            return Ok(template);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(" no encontrado"))
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reabre un template: Terminado → Pendiente.
    /// Preserva los SnapshotSlots para permitir edición.
    /// </summary>
    [HttpPost("{id}/reabrir")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<TemplateRecargaDto>> Reabrir(int id)
    {
        try
        {
            var template = await lifecycleService.ReabrirAsync(id);
            return Ok(template);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(" no encontrado"))
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Aplica acciones en lote a los slots de un período.
    /// Soporta: REFILL (actualizar cantidad), EMPTY (cantidad=0), SWAP (cambiar producto).
    /// </summary>
    [HttpPost("{templateId}/periodo/{periodoId}/slot-batch")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<SlotBatchResponse>> SlotBatch(
        int templateId,
        int periodoId,
        [FromBody] SlotBatchRequest request)
    {
        if (request.Actions == null || !request.Actions.Any())
        {
            return BadRequest(new { error = "Debe enviar al menos una acción", code = "EMPTY_ACTIONS" });
        }

        try
        {
            var result = await service.ApplySlotBatchAsync(templateId, periodoId, request.Actions);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = "NOT_FOUND" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message, code = "INVALID_ACTION" });
        }
    }

    /// <summary>
    /// Obtener un template por ID con sus períodos
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TemplateRecargaDto>> GetById(int id)
    {
        var result = await service.GetByIdAsync(id);
        if (result == null)
            return NotFound($"Template con ID {id} no encontrado");
        return Ok(result);
    }

    /// <summary>
    /// Crear nuevo template de recarga
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<TemplateRecargaDto>> Create([FromBody] CreateTemplateRecargaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nombre))
            return BadRequest("El nombre del template es requerido");

        try
        {
            var result = await service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("FechaRecarga"))
        {
            return Conflict(new { error = ex.Message, code = "CHAIN_CONFLICT" });
        }
    }

    /// <summary>
    /// Actualizar template existente
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<TemplateRecargaDto>> Update(int id, [FromBody] UpdateTemplateRecargaDto dto)
    {
        try
        {
            var result = await service.UpdateAsync(id, dto);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("FechaRecarga"))
        {
            return Conflict(new { error = ex.Message, code = "CHAIN_CONFLICT" });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Eliminar template
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Analizar stockout usando los períodos del template
    /// </summary>
    [HttpGet("{id}/analyze")]
    public async Task<ActionResult<List<StockoutAnalysisDto>>> AnalyzePorTemplate(
        int id,
        [FromQuery] double umbralHoras = 24)
    {
        var template = await service.GetByIdAsync(id);
        if (template == null)
            return NotFound($"Template con ID {id} no encontrado");

        var result = await service.AnalyzarPorTemplateAsync(id, umbralHoras);
        return Ok(result);
    }

    /// <summary>Analizar stockout con el bundle máquina-producto de v2.</summary>
    [HttpGet("{id}/analyze-v2")]
    public async Task<ActionResult<StockoutDashboardAnalysisDto>> AnalyzePorTemplateV2(
        int id,
        [FromQuery] double umbralHoras = 24)
    {
        var template = await service.GetByIdAsync(id);
        if (template == null)
            return NotFound($"Template con ID {id} no encontrado");

        return Ok(await analyticsService.AnalyzarPorTemplateV2Async(id, umbralHoras));
    }

    /// <summary>
    /// Obtiene el timeline de ventas para un slot específico bajo demanda.
    /// Lazy-loaded: solo se invoca cuando el usuario interactúa con el scrubber en el dashboard.
    /// </summary>
    [HttpGet("{templateId}/slot-timeline")]
    public async Task<ActionResult<SlotTimelineDto>> GetSlotTimeline(
        int templateId,
        [FromQuery] int maquinaId,
        [FromQuery] string numeroSlot)
    {
        var result = await analyticsService.GetSlotTimelineAsync(templateId, maquinaId, numeroSlot);
        if (result == null)
            return NotFound(new { error = "Slot no encontrado en el período" });

        return Ok(result);
    }

    /// <summary>
    /// Obtener la configuración actual de slots de una máquina (para crear snapshot)
    /// </summary>
    [HttpGet("maquina/{maquinaId}/slots")]
    public async Task<ActionResult<List<SnapshotSlotDto>>> GetSlotsForMaquina(int maquinaId)
    {
        var slots = await service.GetSlotsForMaquinaAsync(maquinaId);
        return Ok(slots);
    }

    /// <summary>
    /// Sincronizar histórico de ventas con la configuración de este template
    /// </summary>
    [HttpPost("{id}/sincronizar-ventas")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<object>> SincronizarVentas(int id, [FromQuery] bool actualizarCostos = false)
    {
        var template = await service.GetByIdAsync(id);
        if (template == null)
            return NotFound($"Template con ID {id} no encontrado");

        var amountUpdated = await service.SyncVentasWithTemplateAsync(id, actualizarCostos);
        
        return Ok(new { 
            message = "Ventas sincronizadas exitosamente",
            count = amountUpdated 
        });
    }

    /// <summary>
    /// Sincronizar TODOS los templates contra las ventas históricas (global)
    /// </summary>
    [HttpPost("sincronizar-todas-ventas")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<SyncAllVentasResultDto>> SincronizarTodasVentas([FromQuery] bool actualizarCostos = false)
    {
        var result = await service.SyncAllVentasAsync(actualizarCostos);
        return Ok(result);
    }

    /// <summary>
    /// Sincronizar el producto de un slot específico en las ventas históricas
    /// </summary>
    [HttpPatch("{templateId}/periodo/{periodoId}/slot/{numeroSlot}/sincronizar-producto")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<SyncSlotProductoResultDto>> SyncSlotProducto(
        int templateId,
        int periodoId,
        string numeroSlot,
        [FromBody] SyncSlotProductoRequestDto request)
    {
        if (request.ProductoId <= 0)
            return BadRequest("productoId es requerido y debe ser mayor a 0");

        try
        {
            var result = await service.SyncSlotProductoAsync(templateId, periodoId, numeroSlot, request.ProductoId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

    /// <summary>
    /// Subir o reemplazar la foto guía de un período
    /// </summary>
    [HttpPut("{templateId}/periodo/{periodoId}/foto-guia")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UploadFotoGuia(int templateId, int periodoId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Archivo requerido", code = "FILE_MISSING" });

        if (file.Length > 10 * 1024 * 1024)
            return StatusCode(413, new { error = "La foto guía excede 10 MB", code = "FOTO_TOO_LARGE" });

        var contentType = file.ContentType.ToLowerInvariant();
        if (!AllowedImageTypes.Contains(contentType))
            return StatusCode(415, new { error = "Formato no soportado. Usá JPG, PNG, GIF o WebP.", code = "FOTO_INVALID_TYPE" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);

        try
        {
            await service.SaveFotoGuiaAsync(periodoId, ms.ToArray(), contentType);
            return Ok(new { message = "Foto guía guardada", sizeBytes = ms.Length });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = "PERIODO_NOT_FOUND" });
        }
    }

    /// <summary>
    /// Obtener la foto guía de un período
    /// </summary>
    [HttpGet("{templateId}/periodo/{periodoId}/foto-guia")]
    public async Task<IActionResult> GetFotoGuia(int templateId, int periodoId)
    {
        try
        {
            var (data, _) = await service.GetFotoGuiaAsync(periodoId);
            if (data == null)
                return NotFound(new { error = "No hay foto guía guardada", code = "FOTO_NOT_FOUND" });

            return File(data, "image/jpeg");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = "PERIODO_NOT_FOUND" });
        }
    }

    /// <summary>
    /// Eliminar la foto guía de un período
    /// </summary>
    [HttpDelete("{templateId}/periodo/{periodoId}/foto-guia")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteFotoGuia(int templateId, int periodoId)
    {
        try
        {
            await service.DeleteFotoGuiaAsync(periodoId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = "PERIODO_NOT_FOUND" });
        }
    }

    /// <summary>
    /// Subir o reemplazar la foto OCR de un período
    /// </summary>
    [HttpPut("{templateId}/periodo/{periodoId}/foto-ocr")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UploadFotoOcr(int templateId, int periodoId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Archivo requerido", code = "FILE_MISSING" });

        if (file.Length > 5 * 1024 * 1024)
            return StatusCode(413, new { error = "La foto OCR excede 5 MB", code = "FOTO_TOO_LARGE" });

        var contentType = file.ContentType.ToLowerInvariant();
        if (!AllowedImageTypes.Contains(contentType))
            return StatusCode(415, new { error = "Formato no soportado. Usá JPG, PNG, GIF o WebP.", code = "FOTO_INVALID_TYPE" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);

        try
        {
            await service.SaveFotoOcrAsync(periodoId, ms.ToArray(), contentType);
            return Ok(new { message = "Foto OCR guardada", sizeBytes = ms.Length });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = "PERIODO_NOT_FOUND" });
        }
    }

    /// <summary>
    /// Obtener la foto OCR de un período
    /// </summary>
    [HttpGet("{templateId}/periodo/{periodoId}/foto-ocr")]
    public async Task<IActionResult> GetFotoOcr(int templateId, int periodoId)
    {
        try
        {
            var (data, _) = await service.GetFotoOcrAsync(periodoId);
            if (data == null)
                return NotFound(new { error = "No hay foto OCR guardada", code = "FOTO_NOT_FOUND" });

            return File(data, "image/jpeg");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = "PERIODO_NOT_FOUND" });
        }
    }

    /// <summary>
    /// Eliminar la foto OCR de un período
    /// </summary>
    [HttpDelete("{templateId}/periodo/{periodoId}/foto-ocr")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteFotoOcr(int templateId, int periodoId)
    {
        try
        {
            await service.DeleteFotoOcrAsync(periodoId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message, code = "PERIODO_NOT_FOUND" });
        }
    }
}
