using Microsoft.AspNetCore.Mvc;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaquinasController(IMaquinaService maquinaService, IAuditService auditService) : ControllerBase
    {
        public async Task<ActionResult<List<Maquina>>> GetMaquinas()
        {
            return await maquinaService.GetMaquinasAsync();
        }

        [HttpPost]
        public async Task<ActionResult<Maquina>> PostMaquina(Maquina maquina)
        {
            var created = await maquinaService.CreateMaquinaAsync(maquina);
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Crear Máquina", $"Máquina creada: {created.Nombre} (ID: {created.Id})");
            return CreatedAtAction("GetMaquinas", new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaquina(int id, Maquina maquina)
        {
            if (id != maquina.Id) return BadRequest();

            try
            {
                await maquinaService.UpdateMaquinaAsync(id, maquina);
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Actualizar Máquina", $"Máquina actualizada: {maquina.Nombre} (ID: {id})");
                return NoContent();
            }
            catch (Exception)
            {
                if (await maquinaService.GetMaquinaAsync(id) == null) return NotFound();
                throw;
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaquina(int id)
        {
            var maquina = await maquinaService.GetMaquinaAsync(id);
            if (maquina == null) return NotFound();

            // Note: Service handles deletion logic (and potentially check for orphans if implemented there)
            await maquinaService.DeleteMaquinaAsync(id);
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Eliminar Máquina", $"Máquina eliminada: {maquina.Nombre} (ID: {id})");
            return NoContent();
        }

        [HttpGet("{id}/slots")]
        public async Task<ActionResult<List<ConfiguracionSlotDto>>> GetSlots([FromRoute] int id)
        {
            return await maquinaService.GetSlotsAsync(id);
        }

        [HttpPost("{id}/slots")]
        public async Task<IActionResult> UpdateSlot(int id, [FromBody] ConfiguracionSlotDto slot)
        {
            if (id != slot.MaquinaId) return BadRequest("ID de máquina no coincide.");
            await maquinaService.UpdateSlotAsync(slot);
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Actualizar Slot", $"Actualizado slot {slot.NumeroSlot} en máquina {id}. Capacidad: {slot.CapacidadMaxima}, Precio: {slot.PrecioVenta}");
            return Ok();
        }

        [HttpPost("{id}/batch-actions")]
        public async Task<IActionResult> ProcesarMovimientos(int id, [FromBody] List<SlotActionDto> acciones)
        {
            try
            {
                // Obtener slots y nombre de máquina para logging detallado
                var maquina = await maquinaService.GetMaquinaAsync(id);
                var slots = await maquinaService.GetSlotsAsync(id);
                var slotDict = slots.ToDictionary(s => s.Id, s => s);
                string nombreMaq = maquina?.Nombre ?? $"ID {id}";

                await maquinaService.ProcesarMovimientosLoteAsync(id, acciones);

                // Construir log detallado
                var detalles = new List<string>();
                foreach (var a in acciones)
                {
                    var slot = slotDict.GetValueOrDefault(a.SlotId);
                    string slotNum = slot?.NumeroSlot ?? $"?";
                    string prod = slot?.Producto?.Nombre ?? "N/A";
                    switch (a.ActionType)
                    {
                        case "REFILL":
                            detalles.Add($"Slot {slotNum} ({prod}): +{a.Cantidad}");
                            break;
                        case "EMPTY":
                            detalles.Add($"Slot {slotNum}: Vaciado ({prod})");
                            break;
                        case "SWAP":
                            detalles.Add($"Slot {slotNum}: Cambio {prod} → ProdID {a.NewProductoId}");
                            break;
                    }
                }
                string detalle = $"Máquina {nombreMaq}: {string.Join(", ", detalles)}";
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Movimiento Inventario", detalle);
                Response.Headers["X-Deprecated"] = "use /api/TemplateRecarga/{id}/periodo/{periodoId}/slot-batch";
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
