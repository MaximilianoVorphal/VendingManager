using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TransferenciaController(ITransferenciaService transferenciaService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TransferenciaDto>>> GetTransferencias()
    {
        var transferencias = await transferenciaService.GetAllAsync();
        var dto = transferencias.Select(MapToDto).ToList();
        return Ok(dto);
    }

    [HttpGet("pendientes")]
    public async Task<ActionResult<IEnumerable<TransferenciaDto>>> GetTransferenciasPendientes()
    {
        var transferencias = await transferenciaService.GetTransferenciasPendientesAsync();
        var dto = transferencias.Select(MapToDto).ToList();
        return Ok(dto);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TransferenciaDto>> GetTransferencia(int id)
    {
        var transferencia = await transferenciaService.GetByIdAsync(id);
        if (transferencia == null) return NotFound();
        return Ok(MapToDto(transferencia));
    }

    [HttpPost]
    public async Task<ActionResult<TransferenciaDto>> CreateTransferencia([FromBody] CreateTransferenciaRequest request)
    {
        var transferencia = new Transferencia
        {
            Fecha = request.Fecha,
            Monto = request.Monto,
            Descripcion = request.Descripcion,
            Trabajador = request.Trabajador
        };

        var created = await transferenciaService.CreateAsync(transferencia);
        return CreatedAtAction(nameof(GetTransferencia), new { id = created.Id }, MapToDto(created));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTransferencia(int id, [FromBody] CreateTransferenciaRequest request)
    {
        var transferencia = new Transferencia
        {
            Fecha = request.Fecha,
            Monto = request.Monto,
            Descripcion = request.Descripcion,
            Trabajador = request.Trabajador
        };

        await transferenciaService.UpdateAsync(id, transferencia);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTransferencia(int id)
    {
        var existing = await transferenciaService.GetByIdAsync(id);
        if (existing == null) return NotFound();

        if (existing.Estado == Shared.Enums.TransferenciaEstado.Conciliado)
            return BadRequest("No se puede eliminar una transferencia ya conciliada.");

        await transferenciaService.DeleteAsync(id);
        return NoContent();
    }

    private static Shared.DTOs.TransferenciaDto MapToDto(Transferencia t)
    {
        return new Shared.DTOs.TransferenciaDto
        {
            Id = t.Id,
            Fecha = t.Fecha,
            Monto = t.Monto,
            Descripcion = t.Descripcion,
            Trabajador = t.Trabajador,
            Estado = t.Estado,
            RendicionId = t.RendicionId,
            MovimientoCajaId = t.MovimientoCajaId,
            Verificada = t.Verificada,
            HasComprobante = t.ComprobanteImagenFileName != null,
            ComprobanteImagenFileName = t.ComprobanteImagenFileName,
            Compras = t.Compras?.Select(c => new CompraDto
            {
                Id = c.Id,
                FechaCompra = c.FechaCompra,
                Proveedor = c.Proveedor,
                NumeroDocumento = c.NumeroDocumento,
                MontoTotal = c.MontoTotal,
                Estado = c.Estado,
                TipoFactura = c.TipoFactura,
                PagadaCaja = c.PagadaCaja,
                TransferenciaId = c.TransferenciaId,
                Verificada = c.Verificada
            }).ToList() ?? new()
        };
    }
}

public class CreateTransferenciaRequest
{
    public DateTime Fecha { get; set; } = DateTime.Now;
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public string Trabajador { get; set; } = string.Empty;
}