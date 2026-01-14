using Microsoft.AspNetCore.Mvc;

namespace VendingManager.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductosController : ControllerBase
    {
        private readonly IInventarioService _inventarioService;

        public ProductosController(IInventarioService inventarioService)
        {
            _inventarioService = inventarioService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Producto>>> GetProductos()
        {
            return Ok(await _inventarioService.GetProductosAsync());
        }

        [HttpPost("importar-catalogo")]
        public async Task<IActionResult> SubirCatalogo(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Por favor sube un archivo vÃ¡lido.");

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    string resultado = await _inventarioService.ImportarCatalogoAsync(stream, file.FileName);
                    return Ok(new { message = resultado });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }

        [HttpGet("exportar-catalogo")]
        public async Task<IActionResult> ExportarCatalogo()
        {
            try
            {
                var archivo = await _inventarioService.ExportarCatalogoAsync();
                var nombreArchivo = $"Catalogo_Productos_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                return File(archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno al exportar: {ex.Message}");
            }
        }

        [HttpPost("ajustar-stock")]
        public async Task<IActionResult> AjustarStock([FromBody] StockUpdateDto dto)
        {
            try
            {
                await _inventarioService.AjustarStockAsync(dto.ProductoId, dto.NuevoStock);
                return Ok(new { message = "Stock actualizado", nuevoStock = dto.NuevoStock });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("not found")) return NotFound("Producto no encontrado");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Producto>> GetProducto(int id)
        {
            var producto = await _inventarioService.GetProductoAsync(id);
            if (producto == null) return NotFound();
            return producto;
        }

        [HttpPost]
        public async Task<ActionResult<Producto>> PostProducto(Producto producto)
        {
            var created = await _inventarioService.CreateProductoAsync(producto);
            return CreatedAtAction("GetProducto", new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutProducto(int id, Producto producto, [FromQuery] DateTime? recalculateFrom = null, [FromQuery] DateTime? recalculateTo = null)
        {
            if (id != producto.Id) return BadRequest();
            try
            {
                await _inventarioService.UpdateProductoAsync(id, producto, recalculateFrom, recalculateTo);
                return NoContent();
            }
            catch (Exception)
            {
                // In a real scenario we'd check for specific errors
                if (await _inventarioService.GetProductoAsync(id) == null) return NotFound();
                throw;
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProducto(int id)
        {
            if (await _inventarioService.GetProductoAsync(id) == null) return NotFound();
            await _inventarioService.DeleteProductoAsync(id);
            return NoContent();
        }
    }
}
