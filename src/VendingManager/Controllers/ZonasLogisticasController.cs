using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Infrastructure.Data;
using VendingManager.Core.Entities;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;

[Authorize]
[ApiController]
[Route("api/zonas-logisticas")]
public class ZonasLogisticasController(ApplicationDbContext context) : ControllerBase
{
    /// <summary>
    /// GET api/zonas-logisticas — returns all zonas ordered by Nombre.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ZonaLogisticaDto>>> GetZonas()
    {
        var list = await context.ZonasLogisticas
            .OrderBy(z => z.Nombre)
            .Select(z => new ZonaLogisticaDto
            {
                Id = z.Id,
                Nombre = z.Nombre,
                CostoBaseViaje = z.CostoBaseViaje
            })
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// POST api/zonas-logisticas — creates a new zona.
    /// Rejects duplicates with 409 Conflict.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ZonaLogisticaDto>> CreateZona([FromBody] CrearZonaRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return BadRequest("Nombre es requerido.");
        }

        var exists = await context.ZonasLogisticas
            .AnyAsync(z => z.Nombre == request.Nombre);

        if (exists)
        {
            return Conflict("Ya existe una zona con ese nombre.");
        }

        var zona = new ZonaLogistica
        {
            Nombre = request.Nombre,
            CostoBaseViaje = request.CostoBaseViaje
        };

        context.ZonasLogisticas.Add(zona);
        await context.SaveChangesAsync();

        var dto = new ZonaLogisticaDto
        {
            Id = zona.Id,
            Nombre = zona.Nombre,
            CostoBaseViaje = zona.CostoBaseViaje
        };

        return CreatedAtAction(nameof(GetZonas), dto);
    }

    /// <summary>
    /// PUT api/zonas-logisticas/{id} — update a zona.
    /// Returns 200 on success, 404 if missing, 400 if name empty, 409 if duplicate.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ZonaLogisticaDto>> UpdateZona(int id, [FromBody] ActualizarZonaRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest("Nombre es requerido.");

        var zona = await context.ZonasLogisticas.FindAsync(id);
        if (zona is null) return NotFound();

        if (zona.Nombre != request.Nombre)
        {
            var duplicate = await context.ZonasLogisticas.AnyAsync(z => z.Nombre == request.Nombre && z.Id != id);
            if (duplicate) return Conflict("Ya existe una zona con ese nombre.");
        }

        zona.Nombre = request.Nombre;
        zona.CostoBaseViaje = request.CostoBaseViaje;
        await context.SaveChangesAsync();

        return Ok(new ZonaLogisticaDto
        {
            Id = zona.Id,
            Nombre = zona.Nombre,
            CostoBaseViaje = zona.CostoBaseViaje
        });
    }

    /// <summary>
    /// DELETE api/zonas-logisticas/{id} — remove a zona.
    /// Maquina FK is SetNull (safe to delete even if machines reference it).
    /// Returns 204 NoContent on success, 404 if missing.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteZona(int id)
    {
        var zona = await context.ZonasLogisticas.FindAsync(id);
        if (zona is null) return NotFound();

        context.ZonasLogisticas.Remove(zona);
        await context.SaveChangesAsync();

        return NoContent();
    }
}
