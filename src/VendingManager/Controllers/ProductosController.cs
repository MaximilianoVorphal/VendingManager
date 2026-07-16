using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VendingManager.Shared.Constants;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Entities;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProductosController(
        IInventarioService inventarioService,
        IAuditService auditService,
        IProductoEANRepository eanRepo) : ControllerBase
    {
        public async Task<ActionResult<IEnumerable<Producto>>> GetProductos()
        {
            return Ok(await inventarioService.GetProductosAsync());
        }

        [HttpPost("importar-catalogo")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> SubirCatalogo(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Por favor sube un archivo vÃ¡lido.");

            using (var stream = file.OpenReadStream())
            {
                string resultado = await inventarioService.ImportarCatalogoAsync(stream, file.FileName);
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Importar Catálogo (Productos)", $"Catálogo importado: {file.FileName}");
                return Ok(new { message = resultado });
            }
        }

        [HttpGet("exportar-catalogo")]
        public async Task<IActionResult> ExportarCatalogo()
        {
            var archivo = await inventarioService.ExportarCatalogoAsync();
            var nombreArchivo = $"Catalogo_Productos_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
        }

        [HttpPost("ajustar-stock")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> AjustarStock([FromBody] StockUpdateDto dto)
        {
            try
            {
                var producto = await inventarioService.GetProductoAsync(dto.ProductoId);
                if (producto == null) return NotFound("Producto no encontrado");
                int stockAnterior = producto.StockBodega;
                await inventarioService.AjustarStockAsync(dto.ProductoId, dto.NuevoStock);
                string tipoMov = dto.NuevoStock > stockAnterior ? "Ingreso" : "Salida";
                int diferencia = Math.Abs(dto.NuevoStock - stockAnterior);
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", $"{tipoMov} Stock", $"{producto.Nombre}: {stockAnterior} → {dto.NuevoStock} ({tipoMov} de {diferencia} uds)");
                return Ok(new { message = "Stock actualizado", nuevoStock = dto.NuevoStock });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("not found")) return NotFound("Producto no encontrado");
                throw;
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Producto>> GetProducto(int id)
        {
            var producto = await inventarioService.GetProductoAsync(id);
            if (producto == null) return NotFound();
            return producto;
        }

        [HttpGet("{id}/historial-costos")]
        public async Task<ActionResult<IEnumerable<HistorialCostoViewDto>>> GetHistorialCostos(int id)
        {
            var historial = await inventarioService.GetHistorialCostosAsync(id);
            return Ok(historial);
        }

        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<Producto>> PostProducto(Producto producto)
        {
            var created = await inventarioService.CreateProductoAsync(producto);
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Crear Producto", $"Producto creado: {created.Nombre} (ID: {created.Id}). Costo: ${created.CostoPromedio:N0}, Stock: {created.StockBodega}");
            return CreatedAtAction("GetProducto", new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> PutProducto(int id, Producto producto, [FromQuery] DateTime? recalculateFrom = null, [FromQuery] DateTime? recalculateTo = null)
        {
            if (id != producto.Id) return BadRequest();
            try
            {
                var anterior = await inventarioService.GetProductoAsync(id);
                var cambios = new List<string>();
                if (anterior != null)
                {
                    if (anterior.Nombre != producto.Nombre) cambios.Add($"Nombre: {anterior.Nombre} → {producto.Nombre}");
                    if (anterior.CostoPromedio != producto.CostoPromedio) cambios.Add($"Costo: ${anterior.CostoPromedio:N0} → ${producto.CostoPromedio:N0}");
                    if (anterior.Categoria != producto.Categoria) cambios.Add($"Cat: {anterior.Categoria} → {producto.Categoria}");
                    if (anterior.Proveedor != producto.Proveedor) cambios.Add($"Prov: {anterior.Proveedor} → {producto.Proveedor}");
                }
                await inventarioService.UpdateProductoAsync(id, producto, recalculateFrom, recalculateTo);
                string detalle = cambios.Count > 0 ? string.Join(", ", cambios) : "Sin cambios detectados";
                if (recalculateFrom.HasValue) detalle += $" [Recálculo desde {recalculateFrom.Value:dd/MM/yyyy}]";
                await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Actualizar Producto", $"{producto.Nombre} (ID: {id}): {detalle}");
                return NoContent();
            }
            catch (Exception ex)
            {
                if (await inventarioService.GetProductoAsync(id) == null) return NotFound();
                Console.WriteLine($"🔥 ERROR en PUT /api/productos/{id}: {ex}");
                return StatusCode(500, $"Error interno al actualizar producto: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> DeleteProducto(int id)
        {
            var producto = await inventarioService.GetProductoAsync(id);
            if (producto == null) return NotFound();
            await inventarioService.DeleteProductoAsync(id);
            await auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Desconocido", "Eliminar Producto", $"Producto eliminado: {producto.Nombre} (ID: {id})");
            return NoContent();
        }

        // ─── EAN Catalog Endpoints ─────────────────────────────────────────────

        /// <summary>List all EAN mappings (with product name).</summary>
        [HttpGet("ean")]
        public async Task<ActionResult<IEnumerable<ProductoEanDto>>> GetEanMappings()
        {
            var mappings = await eanRepo.GetAllAsync();
            var result = mappings.Select(MapToDto).ToList();
            return Ok(result);
        }

        /// <summary>Get a single EAN mapping by its Id.</summary>
        [HttpGet("ean/{id:int}")]
        public async Task<ActionResult<ProductoEanDto>> GetEanMapping(int id)
        {
            var mappings = await eanRepo.GetAllAsync();
            var entity = mappings.FirstOrDefault(e => e.Id == id);
            if (entity == null) return NotFound();
            return Ok(MapToDto(entity));
        }

        /// <summary>Create a new EAN mapping.</summary>
        [HttpPost("ean")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<ActionResult<ProductoEanDto>> CreateEanMapping([FromBody] CreateProductoEanRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.EAN))
                return BadRequest("EAN code is required.");

            // Check for duplicate
            var existing = await eanRepo.GetByEanAsync(dto.EAN.Trim());
            if (existing != null)
                return Conflict(new { message = $"EAN '{dto.EAN}' already exists (Id={existing.Id})." });

            var entity = new ProductoEAN
            {
                EAN = dto.EAN.Trim(),
                SKU = dto.SKU?.Trim(),
                ProductoId = dto.ProductoId,
                PackSize = dto.PackSize,
                Proveedor = dto.Proveedor?.Trim(),
                DescripcionProveedor = dto.DescripcionProveedor?.Trim(),
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };

            await eanRepo.AddAsync(entity);

            // Reload with Producto navigation for the response
            var reloaded = (await eanRepo.GetAllAsync()).FirstOrDefault(e => e.Id == entity.Id);
            return CreatedAtAction(nameof(GetEanMapping), new { id = entity.Id }, MapToDto(reloaded ?? entity));
        }

        /// <summary>Update an existing EAN mapping.</summary>
        [HttpPut("ean/{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> UpdateEanMapping(int id, [FromBody] CreateProductoEanRequestDto dto)
        {
            var entity = (await eanRepo.GetAllAsync()).FirstOrDefault(e => e.Id == id);
            if (entity == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(dto.EAN))
            {
                // Check for duplicate if EAN is being changed
                var dup = await eanRepo.GetByEanAsync(dto.EAN.Trim());
                if (dup != null && dup.Id != id)
                    return Conflict(new { message = $"EAN '{dto.EAN}' is already assigned to another mapping (Id={dup.Id})." });
                entity.EAN = dto.EAN.Trim();
            }

            if (dto.SKU != null) entity.SKU = dto.SKU.Trim();
            if (dto.ProductoId.HasValue) entity.ProductoId = dto.ProductoId;
            if (dto.PackSize.HasValue) entity.PackSize = dto.PackSize;
            if (dto.Proveedor != null) entity.Proveedor = dto.Proveedor.Trim();
            if (dto.DescripcionProveedor != null) entity.DescripcionProveedor = dto.DescripcionProveedor.Trim();
            entity.LastSeenAt = DateTime.UtcNow;

            await eanRepo.UpdateAsync(entity);
            return NoContent();
        }

        /// <summary>Delete an EAN mapping.</summary>
        [HttpDelete("ean/{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> DeleteEanMapping(int id)
        {
            var entity = (await eanRepo.GetAllAsync()).FirstOrDefault(e => e.Id == id);
            if (entity == null) return NotFound();

            await eanRepo.DeleteAsync(id);
            await auditService.RegistrarAccionAsync(
                User.Identity?.Name ?? "Desconocido",
                "Eliminar EAN",
                $"EAN eliminado: {entity.EAN} (ProductoId: {entity.ProductoId})");
            return NoContent();
        }

        /// <summary>Get all EANs for a specific product.</summary>
        [HttpGet("{id:int}/ean")]
        public async Task<ActionResult<IEnumerable<ProductoEanDto>>> GetEansByProducto(int id)
        {
            var mappings = await eanRepo.GetAllAsync();
            var result = mappings.Where(e => e.ProductoId == id).Select(MapToDto).ToList();
            return Ok(result);
        }

        private static ProductoEanDto MapToDto(ProductoEAN entity) => new()
        {
            Id = entity.Id,
            EAN = entity.EAN,
            SKU = entity.SKU,
            ProductoId = entity.ProductoId,
            ProductoNombre = entity.Producto?.Nombre,
            PackSize = entity.PackSize,
            Proveedor = entity.Proveedor,
            DescripcionProveedor = entity.DescripcionProveedor,
            CreatedAt = entity.CreatedAt,
            LastSeenAt = entity.LastSeenAt
        };
    }
}
