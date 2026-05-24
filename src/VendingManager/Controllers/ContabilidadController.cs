using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ContabilidadController(IContabilidadService contabilidadService) : ControllerBase
{
    private readonly IContabilidadService _service = contabilidadService;

    [HttpPost("transferencia-con-movimiento")]
    public async Task<ActionResult<TransferenciaDto>> CrearTransferenciaConMovimiento(
        [FromBody] TransferenciaConMovimientoRequest request,
        CancellationToken ct = default)
    {
        if (request.Monto <= 0)
            return BadRequest("El monto debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(request.Trabajador))
            return BadRequest("El nombre del trabajador es obligatorio.");

        try
        {
            var transferencia = await _service.CrearTransferenciaConMovimientoAsync(request, ct);
            var dto = MapToDto(transferencia);
            return CreatedAtAction(nameof(CrearTransferenciaConMovimiento), new { id = dto.Id }, dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("compra-vinculada")]
    public async Task<ActionResult<CompraDto>> CrearCompraVinculada(
        [FromBody] CompraVinculadaRequest request,
        CancellationToken ct = default)
    {
        if (request.TransferenciaId <= 0)
            return BadRequest("Debe especificar una transferencia válida.");
        if (request.Detalles == null || !request.Detalles.Any())
            return BadRequest("La compra debe tener al menos un detalle.");
        if (request.Detalles.Any(d => d.Cantidad <= 0 || d.CostoUnitario < 0))
            return BadRequest("Los detalles de compra tienen valores inválidos.");

        try
        {
            var compra = await _service.CrearCompraVinculadaAsync(request, ct);
            var dto = MapToCompraDto(compra);
            return CreatedAtAction(nameof(CrearCompraVinculada), new { id = dto.Id }, dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("gasto-vinculado")]
    public async Task<ActionResult<MovimientoCajaDto>> CrearGastoVinculado(
        [FromBody] GastoVinculadoRequest request,
        CancellationToken ct = default)
    {
        if (request.Monto <= 0)
            return BadRequest("El monto debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(request.Trabajador))
            return BadRequest("El nombre del trabajador es obligatorio.");

        try
        {
            var gasto = await _service.CrearGastoVinculadoAsync(request, ct);
            var dto = MapToMovimientoDto(gasto);
            return CreatedAtAction(nameof(CrearGastoVinculado), new { id = dto.Id }, dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("trabajadores-activos")]
    public async Task<ActionResult<List<TrabajadorActivoDto>>> GetTrabajadoresActivos(
        CancellationToken ct = default)
    {
        var trabajadores = await _service.GetTrabajadoresActivosAsync(ct);
        return Ok(trabajadores);
    }

    [HttpPut("transferencia/{id}/monto")]
    public async Task<IActionResult> ActualizarMontoTransferencia(
        int id,
        [FromBody] ActualizarMontoRequest request,
        CancellationToken ct = default)
    {
        if (request.Monto <= 0)
            return BadRequest("El monto debe ser mayor a cero.");

        try
        {
            await _service.ActualizarMontoTransferenciaAsync(id, request.Monto, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("Otro usuario modificó"))
                return Conflict(ex.Message);
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("transferencia/{id}/desvincular")]
    public async Task<IActionResult> DesvincularTransferencia(int id, CancellationToken ct = default)
    {
        try
        {
            await _service.DesvincularTransferenciaAsync(id, ct);
            return Ok(new { message = "Transferencia desvinculada" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ========== Edit Endpoints ==========

    [HttpPut("compra/{id}")]
    public async Task<ActionResult<CompraDto>> UpdateCompra(
        int id,
        [FromBody] UpdateCompraRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var dto = await _service.UpdateCompraAsync(id, request, ct);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("gasto/{id}")]
    public async Task<ActionResult<MovimientoCajaDto>> UpdateGasto(
        int id,
        [FromBody] UpdateGastoRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var dto = await _service.UpdateGastoAsync(id, request, ct);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ========== AccountingPeriod Endpoints ==========

    [HttpGet("periodos")]
    public async Task<ActionResult<List<AccountingPeriodDto>>> GetPeriodos(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        CancellationToken ct = default)
    {
        var periodos = await _service.GetPeriodosAsync(desde, hasta, ct);
        return Ok(periodos);
    }

    [HttpGet("periodos/{id}")]
    public async Task<ActionResult<AccountingPeriodFullDto>> GetPeriodoFull(
        int id,
        CancellationToken ct = default)
    {
        var periodo = await _service.GetPeriodoFullAsync(id, ct);
        if (periodo == null)
            return NotFound($"Período {id} no encontrado.");

        return Ok(periodo);
    }

    [HttpPost("periodos")]
    public async Task<ActionResult<AccountingPeriodDto>> CreatePeriodo(
        [FromBody] CreatePeriodoRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("El nombre del período es obligatorio.");

        try
        {
            var dto = await _service.CreatePeriodoAsync(request, ct);
            return CreatedAtAction(nameof(GetPeriodoFull), new { id = dto.Id }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("periodos/{id}")]
    public async Task<ActionResult<AccountingPeriodDto>> UpdatePeriodo(
        int id,
        [FromBody] UpdatePeriodoRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var dto = await _service.UpdatePeriodoAsync(id, request, ct);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("periodos/{id}/cerrar")]
    public async Task<IActionResult> ClosePeriodo(int id, CancellationToken ct = default)
    {
        try
        {
            await _service.ClosePeriodoAsync(id, ct);
            return Ok(new { message = "Período cerrado exitosamente." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static TransferenciaDto MapToDto(VendingManager.Core.Entities.Transferencia t)
    {
        return new TransferenciaDto
        {
            Id = t.Id,
            Fecha = t.Fecha,
            Monto = t.Monto,
            Descripcion = t.Descripcion,
            Trabajador = t.Trabajador ?? string.Empty,
            Estado = t.Estado,
            RendicionId = t.RendicionId,
            PeriodoId = t.PeriodoId,
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
                PagadaCaja = c.PagadaCaja,
                TransferenciaId = c.TransferenciaId
            }).ToList() ?? new()
        };
    }

    private static CompraDto MapToCompraDto(VendingManager.Core.Entities.Compra c)
    {
        return new CompraDto
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
            Detalles = c.Detalles?.Select(d => new DetalleCompraDto
            {
                Id = d.Id,
                CompraId = d.CompraId,
                ProductoId = d.ProductoId,
                ProductoNombre = d.Producto?.Nombre,
                DescripcionItem = d.DescripcionItem,
                Cantidad = d.Cantidad,
                CostoUnitario = d.CostoUnitario,
                Subtotal = d.Subtotal
            }).ToList() ?? new()
        };
    }

    private static MovimientoCajaDto MapToMovimientoDto(VendingManager.Core.Entities.MovimientoCaja m)
    {
        return new MovimientoCajaDto
        {
            Id = m.Id,
            Fecha = m.Fecha,
            Descripcion = m.Descripcion,
            Monto = m.Monto,
            Tipo = m.Tipo,
            Categoria = m.Categoria,
            ImagenPath = m.ImagenPath,
            ProductoId = m.ProductoId,
            Cantidad = m.Cantidad,
            OrdenCargaId = m.OrdenCargaId,
            CompraId = m.CompraId,
            GastoRecurrenteId = m.GastoRecurrenteId
        };
    }
}

public class ActualizarMontoRequest
{
    public decimal Monto { get; set; }
}
