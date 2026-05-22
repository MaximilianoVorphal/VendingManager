using Microsoft.AspNetCore.Mvc;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TemplateRecargaController(
    ITemplateRecargaService service,
    ITemplateRecargaLifecycleService lifecycleService) : ControllerBase
{
    /// Obtener todos los templates de recarga
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TemplateRecargaDto>>> GetAll()
    {
        var result = await service.GetAllAsync();
        return Ok(result);
    }

    /// <summary>
    /// Termina un template: Pendiente → Terminado.
    /// El template queda como completado, fuente para stock-critico.
    /// </summary>
    [HttpPost("{id}/terminar")]
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
    /// Limpia todos los SnapshotSlots.
    /// </summary>
    [HttpPost("{id}/reabrir")]
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
    public async Task<ActionResult<TemplateRecargaDto>> Create([FromBody] CreateTemplateRecargaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nombre))
            return BadRequest("El nombre del template es requerido");

        if (!dto.Periodos.Any())
            return BadRequest("Debe incluir al menos un período");

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
    public async Task<ActionResult<SyncAllVentasResultDto>> SincronizarTodasVentas([FromQuery] bool actualizarCostos = false)
    {
        var result = await service.SyncAllVentasAsync(actualizarCostos);
        return Ok(result);
    }

    /// <summary>
    /// Sincronizar el producto de un slot específico en las ventas históricas
    /// </summary>
    [HttpPatch("{templateId}/periodo/{periodoId}/slot/{numeroSlot}/sincronizar-producto")]
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