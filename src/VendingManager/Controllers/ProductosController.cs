using Microsoft.AspNetCore.Mvc;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Entities;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductosController : ControllerBase
    {
        private readonly IInventarioService _inventarioService;
        private readonly IAuditService _auditService;

        public ProductosController(IInventarioService inventarioService, IAuditService auditService)
        {
            _inventarioService = inventarioService;
            _auditService = auditService;
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
                    await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Importar Catálogo (Productos)", $"Catálogo importado: {file.FileName}");
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
                var producto = await _inventarioService.GetProductoAsync(dto.ProductoId);
                if (producto == null) return NotFound("Producto no encontrado");
                int stockAnterior = producto.StockBodega;
                await _inventarioService.AjustarStockAsync(dto.ProductoId, dto.NuevoStock);
                string tipoMov = dto.NuevoStock > stockAnterior ? "Ingreso" : "Salida";
                int diferencia = Math.Abs(dto.NuevoStock - stockAnterior);
                await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", $"{tipoMov} Stock", $"{producto.Nombre}: {stockAnterior} → {dto.NuevoStock} ({tipoMov} de {diferencia} uds)");
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

        [HttpGet("{id}/historial-costos")]
        public async Task<ActionResult<IEnumerable<HistorialCostoViewDto>>> GetHistorialCostos(int id)
        {
            var historial = await _inventarioService.GetHistorialCostosAsync(id);
            return Ok(historial);
        }

        [HttpPost]
        public async Task<ActionResult<Producto>> PostProducto(Producto producto)
        {
            var created = await _inventarioService.CreateProductoAsync(producto);
            await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Crear Producto", $"Producto creado: {created.Nombre} (ID: {created.Id}). Costo: ${created.CostoPromedio:N0}, Stock: {created.StockBodega}");
            return CreatedAtAction("GetProducto", new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutProducto(int id, Producto producto, [FromQuery] DateTime? recalculateFrom = null, [FromQuery] DateTime? recalculateTo = null)
        {
            if (id != producto.Id) return BadRequest();
            try
            {
                var anterior = await _inventarioService.GetProductoAsync(id);
                var cambios = new List<string>();
                if (anterior != null)
                {
                    if (anterior.Nombre != producto.Nombre) cambios.Add($"Nombre: {anterior.Nombre} → {producto.Nombre}");
                    if (anterior.CostoPromedio != producto.CostoPromedio) cambios.Add($"Costo: ${anterior.CostoPromedio:N0} → ${producto.CostoPromedio:N0}");
                    if (anterior.Categoria != producto.Categoria) cambios.Add($"Cat: {anterior.Categoria} → {producto.Categoria}");
                    if (anterior.Proveedor != producto.Proveedor) cambios.Add($"Prov: {anterior.Proveedor} → {producto.Proveedor}");
                }
                await _inventarioService.UpdateProductoAsync(id, producto, recalculateFrom, recalculateTo);
                string detalle = cambios.Count > 0 ? string.Join(", ", cambios) : "Sin cambios detectados";
                if (recalculateFrom.HasValue) detalle += $" [Recálculo desde {recalculateFrom.Value:dd/MM/yyyy}]";
                await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Actualizar Producto", $"{producto.Nombre} (ID: {id}): {detalle}");
                return NoContent();
            }
            catch (Exception)
            {
                if (await _inventarioService.GetProductoAsync(id) == null) return NotFound();
                throw;
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProducto(int id)
        {
            var producto = await _inventarioService.GetProductoAsync(id);
            if (producto == null) return NotFound();
            await _inventarioService.DeleteProductoAsync(id);
            await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Eliminar Producto", $"Producto eliminado: {producto.Nombre} (ID: {id})");
            return NoContent();
        }
    }
}
