using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.Constants;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;


[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ContabilidadController(
    IContabilidadService contabilidadService,
    ITransferenciaService transferenciaService,
    IIntegrityCheckService integrityCheckService,
    ApplicationDbContext context) : ControllerBase
{
    private readonly IContabilidadService _service = contabilidadService;
    private readonly ITransferenciaService _transferenciaService = transferenciaService;
    private readonly IIntegrityCheckService _integrityCheckService = integrityCheckService;
    private readonly ApplicationDbContext _context = context;

    [HttpPost("transferencia-con-movimiento")]
    public async Task<ActionResult<TransferenciaDto>> CrearTransferenciaConMovimiento(
        [FromBody] TransferenciaConMovimientoRequest request,
        CancellationToken ct = default)
    {
        if (request.Monto <= 0)
            return BadRequest("El monto debe ser mayor a cero.");

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

    /// <summary>
    /// Crea un cuadre completo (período + transferencia 1:1). Cada cuadre es una
    /// hoja independiente que se concilia contra sus propias compras y gastos.
    /// </summary>
    [HttpPost("cuadre")]
    public async Task<ActionResult<CuadreCreadoDto>> CrearCuadre(
        [FromBody] CrearCuadreRequest request,
        CancellationToken ct = default)
    {
        if (request.Monto <= 0)
            return BadRequest("El monto debe ser mayor a cero.");

        try
        {
            var dto = await _service.CrearCuadreAsync(request, ct);
            return CreatedAtAction(nameof(CrearCuadre), new { id = dto.PeriodoId }, dto);
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

    [HttpDelete("transferencia/{id}")]
    [ProducesResponseType(typeof(EliminarTransferenciaResultDto), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 401)]
    [ProducesResponseType(typeof(string), 404)]
    public async Task<ActionResult<EliminarTransferenciaResultDto>> EliminarTransferencia(
        int id, CancellationToken ct = default)
    {
        try
        {
            var result = await _service.EliminarTransferenciaCuadreAsync(id, ct);
            return Ok(result);
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

    [HttpPost("transferencia/{transferenciaId}/vincular-compra/{compraId}")]
    public async Task<IActionResult> VincularCompraExistente(
        int transferenciaId, int compraId, CancellationToken ct = default)
    {
        try
        {
            await _service.VincularCompraExistenteAsync(compraId, transferenciaId, ct);
            return Ok(new { message = "Compra vinculada" });
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

    // ========== Delete AccountingPeriod ==========

    /// <summary>
    /// Deletes an AccountingPeriod. Unlinks Transferencias and Devoluciones
    /// (sets PeriodoId = null) but does NOT cascade delete or touch MovimientoCaja.
    /// </summary>
    [HttpDelete("periodos/{id}")]
    public async Task<IActionResult> DeletePeriodo(int id, CancellationToken ct = default)
    {
        try
        {
            await _service.DeletePeriodoAsync(id, ct);
            return NoContent();
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

    // ========== Slice 2: Comprobante upload endpoints (TASK-11) ==========

    /// <summary>Upload a comprobante image for a Transferencia. Mirrors ComprasController {id}/factura.</summary>
    [HttpPost("transferencia/{id}/comprobante")]
    public async Task<IActionResult> UploadComprobanteTransferencia(int id, IFormFile file, CancellationToken ct = default)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("No se proporcionó ningún archivo.");

            await _transferenciaService.SaveComprobanteImagenAsync(id, file);
            return Ok(new { Message = "Comprobante guardado correctamente." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Otro usuario modificó esta transferencia. Recargá la página.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error guardando imagen: {ex.GetType().Name} - {ex.Message}");
        }
    }

    /// <summary>Retrieve the comprobante image for a Transferencia. Mirrors ComprasController GET {id}/factura.</summary>
    [HttpGet("transferencia/{id}/comprobante")]
    public async Task<IActionResult> GetComprobanteTransferencia(int id, CancellationToken ct = default)
    {
        var transferencia = await _transferenciaService.GetByIdAsync(id);
        if (transferencia == null)
            return NotFound($"Transferencia {id} no encontrada.");

        if (transferencia.ComprobanteImagen == null)
            return NotFound("Esta transferencia no tiene comprobante adjunto.");

        var contentType = transferencia.ComprobanteImagenContentType ?? "application/octet-stream";
        return File(transferencia.ComprobanteImagen, contentType);
    }

    // ========== Slice 2: Verification endpoints (TASK-11) ==========

    /// <summary>Mark a Transferencia as verified. POST = verify, handles RowVersion concurrency → 409.</summary>
    [HttpPost("transferencia/{id}/verificar")]
    public async Task<IActionResult> VerificarTransferencia(int id, CancellationToken ct = default)
    {
        try
        {
            await _service.MarcarTransferenciaVerificadaAsync(id, true, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Otro usuario modificó esta transferencia. Recargá la página.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Mark a Transferencia as unverified (toggle off).</summary>
    [HttpPost("transferencia/{id}/desverificar")]
    public async Task<IActionResult> DesverificarTransferencia(int id, CancellationToken ct = default)
    {
        try
        {
            await _service.MarcarTransferenciaVerificadaAsync(id, false, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Otro usuario modificó esta transferencia. Recargá la página.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Mark a Compra as verified.</summary>
    [HttpPost("compra/{id}/verificar")]
    public async Task<IActionResult> VerificarCompra(int id, CancellationToken ct = default)
    {
        try
        {
            await _service.MarcarCompraVerificadaAsync(id, true, ct);
            return NoContent();
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

    /// <summary>Mark a Compra as unverified (toggle off).</summary>
    [HttpPost("compra/{id}/desverificar")]
    public async Task<IActionResult> DesverificarCompra(int id, CancellationToken ct = default)
    {
        try
        {
            await _service.MarcarCompraVerificadaAsync(id, false, ct);
            return NoContent();
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

    // ========== Slice 2: Devolución endpoint (TASK-11) ==========

    /// <summary>Register a Devolucion and post its inverse cash-in MovimientoCaja atomically.</summary>
    [HttpPost("devolucion")]
    public async Task<ActionResult<DevolucionDto>> RegistrarDevolucion(
        [FromBody] RegistrarDevolucionRequest request,
        CancellationToken ct = default)
    {
        if (request.PeriodoId == null && request.RendicionId == null)
            return BadRequest("Debe especificar al menos un PeriodoId o RendicionId.");

        if (request.Monto <= 0)
            return BadRequest("El monto debe ser mayor a cero.");

        try
        {
            var dto = await _service.RegistrarDevolucionAsync(request, ct);
            return CreatedAtAction(nameof(RegistrarDevolucion), new { id = dto.Id }, dto);
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

    // ========== Conciliación Global ==========

    /// <summary>
    /// Returns the global multi-period reconciliation matrix for a given worker.
    /// Groups compras by provider slug across all periods for the worker.
    /// </summary>
    [HttpGet("periodos/conciliacion-global")]
    public async Task<ActionResult<ConciliacionGlobalDto>> GetConciliacionGlobal(
        [FromQuery] string? trabajador,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(trabajador))
            return BadRequest("El parámetro 'trabajador' es obligatorio.");

        var dto = await _service.GetConciliacionGlobalAsync(trabajador, ct);
        return Ok(dto);
    }

    // ========== Data Integrity Checks ==========

    /// <summary>
    /// Runs all data integrity checks and returns grouped results with severity.
    /// </summary>
    [HttpGet("integridad")]
    public async Task<ActionResult<List<IntegrityCheckResultDto>>> GetIntegridad(
        CancellationToken ct = default)
    {
        var results = await _integrityCheckService.RunAllChecksAsync(ct);
        return Ok(results);
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
                Verificada = c.Verificada,
                FacturaImagenPath = c.FacturaImagenPath
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
            Verificada = c.Verificada,
            FacturaImagenPath = c.FacturaImagenPath,
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
