using Microsoft.AspNetCore.Mvc;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TemplateRecargaController(ITemplateRecargaService service) : ControllerBase
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

        var result = await service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
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
}
