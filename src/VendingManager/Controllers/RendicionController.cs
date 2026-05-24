using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RendicionController(
    IRendicionService rendicionService,
    ITransferenciaService transferenciaService,
    ApplicationDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RendicionDto>>> GetRendiciones()
    {
        var rendiciones = await rendicionService.GetAllAsync();
        var dto = rendiciones.Select(r => MapToDto(r)).ToList();
        return Ok(dto);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RendicionDto>> GetRendicion(int id)
    {
        var rendicion = await rendicionService.GetByIdAsync(id);
        if (rendicion == null) return NotFound();
        return Ok(MapToDto(rendicion));
    }

    [HttpPost]
    public async Task<ActionResult<RendicionDto>> CreateRendicion([FromBody] CreateRendicionRequest request)
    {
        var rendicion = new Rendicion
        {
            Trabajador = request.Trabajador,
            FechaInicio = request.FechaInicio == DateTime.MinValue ? DateTime.Now : request.FechaInicio,
            Observaciones = request.Observaciones
        };

        var created = await rendicionService.CreateAsync(rendicion);

        // Si se indicó transferencia inicial, vincularla
        if (request.TransferenciaId.HasValue)
        {
            var transferencia = await transferenciaService.GetByIdAsync(request.TransferenciaId.Value);
            if (transferencia != null)
            {
                transferencia.RendicionId = created.Id;
                await transferenciaService.UpdateAsync(transferencia.Id, transferencia);
                created = await rendicionService.GetByIdAsync(created.Id);
            }
        }

        return CreatedAtAction(nameof(GetRendicion), new { id = created!.Id }, MapToDto(created));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRendicion(int id, [FromBody] CreateRendicionRequest request)
    {
        var rendicion = new Rendicion
        {
            Trabajador = request.Trabajador,
            FechaInicio = request.FechaInicio,
            Observaciones = request.Observaciones
        };

        await rendicionService.UpdateAsync(id, rendicion);
        return NoContent();
    }

    [HttpPost("{id}/cerrar")]
    public async Task<IActionResult> CerrarRendicion(int id)
    {
        try
        {
            await rendicionService.CerrarAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("{id}/link-compra")]
    public async Task<IActionResult> LinkCompra(int id, [FromBody] LinkCompraRequest request)
    {
        try
        {
            await rendicionService.LinkCompraAsync(request.CompraId, request.TransferenciaId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("{id}/unlink-compra/{compraId}")]
    public async Task<IActionResult> UnlinkCompra(int id, int compraId)
    {
        try
        {
            await rendicionService.UnlinkCompraAsync(compraId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("{id}/link-gasto")]
    public async Task<IActionResult> LinkGasto(int id, [FromBody] LinkGastoRequest request)
    {
        try
        {
            await rendicionService.LinkGastoAsync(request.GastoId, id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("{id}/unlink-gasto/{gastoId}")]
    public async Task<IActionResult> UnlinkGasto(int id, int gastoId)
    {
        try
        {
            await rendicionService.UnlinkGastoAsync(gastoId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("{id}/resumen")]
    public async Task<ActionResult<RendicionResumenDto>> GetResumen(int id)
    {
        try
        {
            var resumen = await rendicionService.GetResumenAsync(id);
            return Ok(resumen);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("{id}/full")]
    public async Task<ActionResult<RendicionFullDto>> GetRendicionFull(int id)
    {
        var rendicion = await rendicionService.GetByIdAsync(id);
        if (rendicion == null) return NotFound();
        var resumen = await rendicionService.GetResumenAsync(id);
        var dto = MapToFullDto(rendicion, resumen);
        return Ok(dto);
    }

    private static RendicionDto MapToDto(Rendicion r)
    {
        return new RendicionDto
        {
            Id = r.Id,
            Trabajador = r.Trabajador,
            FechaInicio = r.FechaInicio,
            FechaFin = r.FechaFin,
            Estado = r.Estado,
            Observaciones = r.Observaciones,
            Transferencias = r.Transferencias?.Select(t => new Shared.DTOs.TransferenciaDto
            {
                Id = t.Id,
                Fecha = t.Fecha,
                Monto = t.Monto,
                Descripcion = t.Descripcion,
                Trabajador = t.Trabajador,
                Estado = t.Estado,
                RendicionId = t.RendicionId,
                MovimientoCajaId = t.MovimientoCajaId,
                Compras = t.Compras?.Select(c => new CompraDto
                {
                    Id = c.Id,
                    FechaCompra = c.FechaCompra,
                    Proveedor = c.Proveedor,
                    NumeroDocumento = c.NumeroDocumento,
                    MontoTotal = c.MontoTotal,
                    Estado = c.Estado,
                    TipoFactura = c.TipoFactura,
                    PagadaCaja = c.PagadaCaja
                }).ToList() ?? new()
            }).ToList() ?? new(),
            Gastos = r.Gastos?.Select(g => new MovimientoCajaDto
            {
                Id = g.Id,
                Fecha = g.Fecha,
                Descripcion = g.Descripcion,
                Monto = g.Monto,
                Tipo = g.Tipo,
                Categoria = g.Categoria,
                ImagenPath = g.ImagenPath,
                ProductoId = g.ProductoId,
                Cantidad = g.Cantidad,
                OrdenCargaId = g.OrdenCargaId,
                CompraId = g.CompraId,
                GastoRecurrenteId = g.GastoRecurrenteId
            }).ToList() ?? new()
        };
    }

    private static RendicionFullDto MapToFullDto(Rendicion r, RendicionResumenDto resumen)
    {
        return new RendicionFullDto
        {
            Resumen = resumen,
            Id = r.Id,
            Trabajador = r.Trabajador,
            FechaInicio = r.FechaInicio,
            FechaFin = r.FechaFin,
            Estado = r.Estado,
            Observaciones = r.Observaciones,
            Transferencias = r.Transferencias?.Select(t => new Shared.DTOs.TransferenciaDto
            {
                Id = t.Id,
                Fecha = t.Fecha,
                Monto = t.Monto,
                Descripcion = t.Descripcion,
                Trabajador = t.Trabajador,
                Estado = t.Estado,
                RendicionId = t.RendicionId,
                MovimientoCajaId = t.MovimientoCajaId,
                Compras = t.Compras?.Select(c => new CompraDto
                {
                    Id = c.Id,
                    FechaCompra = c.FechaCompra,
                    Proveedor = c.Proveedor,
                    NumeroDocumento = c.NumeroDocumento,
                    MontoTotal = c.MontoTotal,
                    Estado = c.Estado,
                    TipoFactura = c.TipoFactura,
                    PagadaCaja = c.PagadaCaja
                }).ToList() ?? new()
            }).ToList() ?? new(),
            Gastos = r.Gastos?.Select(g => new MovimientoCajaDto
            {
                Id = g.Id,
                Fecha = g.Fecha,
                Descripcion = g.Descripcion,
                Monto = g.Monto,
                Tipo = g.Tipo,
                Categoria = g.Categoria,
                ImagenPath = g.ImagenPath,
                ProductoId = g.ProductoId,
                Cantidad = g.Cantidad,
                OrdenCargaId = g.OrdenCargaId,
                CompraId = g.CompraId,
                GastoRecurrenteId = g.GastoRecurrenteId
            }).ToList() ?? new()
        };
    }

    [HttpGet("transferencias-no-vinculadas")]
    public async Task<ActionResult> GetTransferenciasNoVinculadas()
    {
        var transferencias = await transferenciaService.GetTransferenciasNoVinculadasAsync();
        var dto = transferencias.Select(t => new Shared.DTOs.TransferenciaDto
        {
            Id = t.Id,
            Fecha = t.Fecha,
            Monto = t.Monto,
            Descripcion = t.Descripcion,
            Trabajador = t.Trabajador,
            Estado = t.Estado,
            RendicionId = t.RendicionId,
            MovimientoCajaId = t.MovimientoCajaId
        }).ToList();
        return Ok(dto);
    }

    [HttpGet("compras-no-vinculadas")]
    public async Task<ActionResult<List<CompraDto>>> GetComprasNoVinculadas(
        [FromServices] ICompraService compraService,
        [FromQuery] string? proveedor = null,
        [FromQuery] string? numeroDocumento = null)
    {
        var compras = await compraService.GetComprasNoVinculadasAsync(proveedor, numeroDocumento);
        var dto = compras.Select(c => new CompraDto
        {
            Id = c.Id,
            FechaCompra = c.FechaCompra,
            Proveedor = c.Proveedor,
            NumeroDocumento = c.NumeroDocumento,
            MontoTotal = c.MontoTotal,
            Estado = c.Estado,
            TipoFactura = c.TipoFactura,
            PagadaCaja = c.PagadaCaja,
            TransferenciaId = c.TransferenciaId
        }).ToList();
        return Ok(dto);
    }

    [HttpGet("gastos-no-vinculados")]
    public async Task<ActionResult<List<MovimientoCajaDto>>> GetGastosNoVinculados(
        [FromServices] ICajaService cajaService,
        [FromQuery] DateTime? fechaDesde = null,
        [FromQuery] DateTime? fechaHasta = null)
    {
        var gastos = await cajaService.GetGastosNoVinculadosAsync(fechaDesde, fechaHasta);
        var dto = gastos.Select(g => new MovimientoCajaDto
        {
            Id = g.Id,
            Fecha = g.Fecha,
            Descripcion = g.Descripcion,
            Monto = g.Monto,
            Tipo = g.Tipo,
            Categoria = g.Categoria,
            ImagenPath = g.ImagenPath,
            ProductoId = g.ProductoId,
            Cantidad = g.Cantidad,
            OrdenCargaId = g.OrdenCargaId,
            CompraId = g.CompraId,
            GastoRecurrenteId = g.GastoRecurrenteId
        }).ToList();
        return Ok(dto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRendicion(int id)
    {
        var rendicion = await rendicionService.GetByIdAsync(id);
        if (rendicion == null)
            return NotFound($"Rendición {id} no encontrada.");

        // 1. Unlink compras from this rendicion's transferencias
        var transferencias = await context.Transferencias
            .Where(t => t.RendicionId == id)
            .Include(t => t.Compras)
            .ToListAsync();

        foreach (var t in transferencias)
        {
            foreach (var c in t.Compras)
                c.TransferenciaId = null;
        }

        // 2. Delete MovimientosCaja linked to this rendicion or its transferencias
        var movimientos = await context.MovimientosCaja
            .Where(m => m.RendicionId == id)
            .ToListAsync();
        context.MovimientosCaja.RemoveRange(movimientos);

        // 3. Unlink transferencias from AccountingPeriods
        foreach (var t in transferencias)
            t.PeriodoId = null;

        // 4. Delete transferencias
        context.Transferencias.RemoveRange(transferencias);

        // 5. Delete rendicion
        context.Rendiciones.Remove(rendicion);

        await context.SaveChangesAsync();
        return Ok(new { message = $"Rendición {id} y sus {transferencias.Count} transferencias eliminadas." });
    }
}