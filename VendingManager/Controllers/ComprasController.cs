using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ComprasController : ControllerBase
{
    private readonly ICompraService _compraService;

    public ComprasController(ICompraService compraService)
    {
        _compraService = compraService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CompraDto>>> GetCompras([FromQuery] int? limit = null)
    {
        try 
        {
            var compras = await _compraService.GetComprasAsync(limit);
            
            var dto = compras.Select(c => new CompraDto
            {
                Id = c.Id,
                FechaCompra = c.FechaCompra,
                Proveedor = c.Proveedor,
                NumeroDocumento = c.NumeroDocumento,
                MontoTotal = c.MontoTotal,
                Estado = c.Estado,
                PagadaCaja = c.PagadaCaja,
                Detalles = c.Detalles.Select(d => new DetalleCompraDto
                {
                    Id = d.Id,
                    CompraId = d.CompraId,
                    ProductoId = d.ProductoId,
                    ProductoNombre = d.Producto?.Nombre,
                    Cantidad = d.Cantidad,
                    CostoUnitario = d.CostoUnitario,
                    Subtotal = d.Subtotal
                }).ToList()
            }).ToList();

            return Ok(dto);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error interno: {ex.Message} | Detalle: {ex.InnerException?.Message}");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CompraDto>> GetCompra(int id)
    {
        var c = await _compraService.GetCompraByIdAsync(id);
        if (c == null) return NotFound();

        var dto = new CompraDto
        {
            Id = c.Id,
            FechaCompra = c.FechaCompra,
            Proveedor = c.Proveedor,
            NumeroDocumento = c.NumeroDocumento,
            MontoTotal = c.MontoTotal,
            Estado = c.Estado,
            PagadaCaja = c.PagadaCaja,
            Detalles = c.Detalles.Select(d => new DetalleCompraDto
            {
                Id = d.Id,
                CompraId = d.CompraId,
                ProductoId = d.ProductoId,
                ProductoNombre = d.Producto?.Nombre,
                Cantidad = d.Cantidad,
                CostoUnitario = d.CostoUnitario,
                Subtotal = d.Subtotal
            }).ToList()
        };

        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<CompraDto>> RegistrarCompra(RegistrarCompraRequestDto request)
    {
        if (request.Detalles == null || !request.Detalles.Any())
        {
            return BadRequest("La compra debe tener al menos un producto.");
        }

        var nuevaCompra = new Compra
        {
            FechaCompra = request.FechaCompra == DateTime.MinValue ? DateTime.Now : request.FechaCompra,
            Proveedor = request.Proveedor,
            NumeroDocumento = request.NumeroDocumento,
            Estado = request.Estado,
            PagadaCaja = request.PagadaCaja,
            UsuarioRegistra = User.Identity?.Name,
            Detalles = request.Detalles.Select(d => new DetalleCompra
            {
                ProductoId = d.ProductoId,
                Cantidad = d.Cantidad,
                CostoUnitario = d.CostoUnitario,
                Subtotal = d.Cantidad * d.CostoUnitario
            }).ToList()
        };

        // Recalcular monto total de la factura
        nuevaCompra.MontoTotal = nuevaCompra.Detalles.Sum(d => d.Subtotal);

        var guardada = await _compraService.RegistrarCompraAsync(nuevaCompra);
        
        return CreatedAtAction(nameof(GetCompra), new { id = guardada.Id }, new { id = guardada.Id });
    }

    [HttpPost("{id}/pagar")]
    public async Task<IActionResult> MarcarPagada(int id)
    {
        await _compraService.MarcarComoPagada(id);
        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> ActualizarCompra(int id, [FromBody] ActualizarCompraRequestDto request)
    {
        try
        {
            await _compraService.ActualizarCompraAsync(id, request);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
