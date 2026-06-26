using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Infrastructure.Data;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProveedoresController(ApplicationDbContext context, IProveedorMatchingService matchingService) : ControllerBase
{
    /// <summary>
    /// GET api/proveedores — returns catalog entries ordered by NombreCanonical.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProveedorCatalogDto>>> GetProveedores()
    {
        var list = await context.ProveedorCatalog
            .OrderBy(p => p.NombreCanonical)
            .Select(p => new ProveedorCatalogDto
            {
                Id = p.Id,
                NombreCanonical = p.NombreCanonical
            })
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// POST api/proveedores — creates a new catalog entry.
    /// Rejects duplicates with 409 Conflict.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ProveedorCatalogDto>> CreateProveedor([FromBody] CrearProveedorRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.NombreCanonical))
        {
            return BadRequest("NombreCanonical es requerido.");
        }

        // Check for duplicate (case-insensitive, handled by SQL Server CI collation)
        var exists = await context.ProveedorCatalog
            .AnyAsync(p => p.NombreCanonical == request.NombreCanonical);

        if (exists)
        {
            return Conflict("Ya existe un proveedor con ese nombre.");
        }

        var catalog = new ProveedorCatalog
        {
            NombreCanonical = request.NombreCanonical,
            CreatedAt = DateTime.UtcNow
        };

        context.ProveedorCatalog.Add(catalog);
        await context.SaveChangesAsync();

        var dto = new ProveedorCatalogDto
        {
            Id = catalog.Id,
            NombreCanonical = catalog.NombreCanonical
        };

        return CreatedAtAction(nameof(GetProveedores), dto);
    }

    /// <summary>
    /// POST api/proveedores/backfill — batch-match existing Compra records where ProveedorCatalogId IS NULL,
    /// using threshold 0.85. Auto-links only high-confidence matches. Idempotent and re-runnable.
    /// Returns summary counts of processed, auto-linked, and remaining pending compras.
    /// </summary>
    [HttpPost("backfill")]
    public async Task<ActionResult<BackfillResultDto>> Backfill()
    {
        var compras = await context.Compras
            .Where(c => c.ProveedorCatalogId == null)
            .ToListAsync();

        int procesadas = compras.Count;
        int autoVinculadas = 0;

        foreach (var compra in compras)
        {
            var match = await matchingService.MatchAsync(compra.Proveedor, 0.85);
            if (match.ProveedorCatalog != null && match.Confidence >= 0.85)
            {
                compra.ProveedorCatalogId = match.ProveedorCatalog.Id;
                autoVinculadas++;
            }
        }

        await context.SaveChangesAsync();

        return Ok(new BackfillResultDto
        {
            Procesadas = procesadas,
            AutoVinculadas = autoVinculadas,
            Pendientes = procesadas - autoVinculadas
        });
    }
}
