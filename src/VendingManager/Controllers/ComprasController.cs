using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ComprasController(ICompraService compraService, IFacturaOcrService facturaOcrService) : ControllerBase
{
    public async Task<ActionResult<IEnumerable<CompraDto>>> GetCompras([FromQuery] int? limit = null)
    {
        try 
        {
            var compras = await compraService.GetComprasAsync(limit);
            
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
                FacturaImagenPath = c.FacturaImagenPath,
                TransferenciaId = c.TransferenciaId,
                Detalles = c.Detalles.Select(d => new DetalleCompraDto
                {
                    Id = d.Id,
                    CompraId = d.CompraId,
                    ProductoId = d.ProductoId,
                    ProductoNombre = d.Producto?.Nombre,
                    DescripcionItem = d.DescripcionItem,
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
        var c = await compraService.GetCompraByIdAsync(id);
        if (c == null) return NotFound();

        var dto = new CompraDto
        {
            Id = c.Id,
            FechaCompra = c.FechaCompra,
            Proveedor = c.Proveedor,
            NumeroDocumento = c.NumeroDocumento,
            MontoTotal = c.MontoTotal,
            Estado = c.Estado,
            TipoFactura = c.TipoFactura,
            PagadaCaja = c.PagadaCaja,
            FacturaImagenPath = c.FacturaImagenPath,
            Detalles = c.Detalles.Select(d => new DetalleCompraDto
            {
                Id = d.Id,
                CompraId = d.CompraId,
                ProductoId = d.ProductoId,
                ProductoNombre = d.Producto?.Nombre,
                DescripcionItem = d.DescripcionItem,
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
            TipoFactura = request.TipoFactura,
            PagadaCaja = request.PagadaCaja,
            UsuarioRegistra = User.Identity?.Name,
            Detalles = request.Detalles.Select(d => new DetalleCompra
            {
                ProductoId = d.ProductoId,
                DescripcionItem = d.DescripcionItem,
                Cantidad = d.Cantidad,
                CostoUnitario = d.CostoUnitario,
                Subtotal = d.Cantidad * d.CostoUnitario
            }).ToList()
        };

        // Recalcular monto total de la factura
        nuevaCompra.MontoTotal = nuevaCompra.Detalles.Sum(d => d.Subtotal);

        var guardada = await compraService.RegistrarCompraAsync(nuevaCompra);
        
        return CreatedAtAction(nameof(GetCompra), new { id = guardada.Id }, new { id = guardada.Id });
    }

    [HttpPost("{id}/pagar")]
    public async Task<IActionResult> MarcarPagada(int id)
    {
        await compraService.MarcarComoPagada(id);
        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> ActualizarCompra(int id, [FromBody] RegistrarCompraRequestDto request)
    {
        try
        {
            await compraService.ActualizarCompraAsync(id, request);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> EliminarCompra(int id)
    {
        try
        {
            await compraService.EliminarCompraAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("upload-factura")]
    public async Task<ActionResult<OcrInvoiceResultDto>> UploadFactura(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("No se proporcionó ningún archivo válido.");

            var resultado = await facturaOcrService.ExtractInvoiceDataAsync(file);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error procesando factura con IA: {ex.Message}");
        }
    }

    [HttpPost("{id}/factura")]
    public async Task<IActionResult> UploadFacturaImagen(int id, IFormFile file)
    {
        try
        {
            // Validación extra antes de llamar al servicio
            if (file == null || file.Length == 0)
                return BadRequest("No se proporcionó ningún archivo.");

            var path = await compraService.SaveFacturaImagenAsync(id, file);
            return Ok(new { Path = path });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(500, $"Error de permisos al guardar la imagen: {ex.Message}. Verifique que el directorio de uploads tenga permisos de escritura.");
        }
        catch (IOException ex)
        {
            return StatusCode(500, $"Error de E/S al guardar la imagen: {ex.Message}");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error guardando imagen: {ex.GetType().Name} - {ex.Message}");
        }
    }

    [HttpGet("{id}/factura")]
    public async Task<IActionResult> GetFacturaImagen(int id)
    {
        var compra = await compraService.GetCompraByIdAsync(id);
        if (compra == null) return NotFound();

        if (string.IsNullOrEmpty(compra.FacturaImagenPath))
            return NotFound("Esta compra no tiene imagen de factura.");

        var filePath = compraService.ResolveFacturaPhysicalPath(compra.FacturaImagenPath);
        if (!System.IO.File.Exists(filePath))
            return NotFound("Archivo de imagen no encontrado en disco.");

        var contentType = compra.FacturaImagenPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "image/" + Path.GetExtension(compra.FacturaImagenPath).TrimStart('.').ToLowerInvariant();

        return PhysicalFile(Path.GetFullPath(filePath), contentType);
    }
}
