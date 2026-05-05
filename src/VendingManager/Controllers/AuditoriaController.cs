using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.Constants;
using VendingManager.Shared.DTOs;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = Roles.Admin)] // Solo administradores pueden ver esto
    public class AuditoriaController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AuditoriaController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Auditoria>>> GetAuditoria([FromQuery] string? usuario = null, [FromQuery] string? accion = null)
        {
            var query = _context.Auditoria.AsQueryable();

            if (!string.IsNullOrEmpty(usuario))
            {
                query = query.Where(a => a.Usuario.Contains(usuario));
            }

            if (!string.IsNullOrEmpty(accion))
            {
                query = query.Where(a => a.Accion.Contains(accion));
            }

            // Ordenamos por fecha descendente (lo más nuevo primero)
            var logs = await query
                .OrderByDescending(a => a.Fecha)
                .Select(a => new VendingManager.Shared.DTOs.AuditoriaDto
                {
                    Id = a.Id,
                    Usuario = a.Usuario,
                    Accion = a.Accion,
                    Detalle = a.Detalle,
                    Fecha = a.Fecha
                })
                .ToListAsync();

            return Ok(logs);
        }

        /// <summary>
        /// Returns all history records from all history tables, ordered by timestamp descending.
        /// </summary>
        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<HistoryListItemDto>>> GetHistory()
        {
            var historyItems = new List<HistoryListItemDto>();

            // Query each history table
            var historyTypes = new[]
            {
                (Type: typeof(ProductoHistory), Name: "Producto"),
                (Type: typeof(CompraHistory), Name: "Compra"),
                (Type: typeof(MaquinaHistory), Name: "Maquina"),
                (Type: typeof(VentaHistory), Name: "Venta"),
                (Type: typeof(MovimientoCajaHistory), Name: "MovimientoCaja"),
                (Type: typeof(ConfiguracionSlotHistory), Name: "ConfiguracionSlot"),
                (Type: typeof(GastoRecurrenteHistory), Name: "GastoRecurrente"),
                (Type: typeof(OrdenCargaHistory), Name: "OrdenCarga"),
                (Type: typeof(UserHistory), Name: "User")
            };

            foreach (var (historyType, entityName) in historyTypes)
            {
                var dbSetMethod = _context.GetType().GetMethod("Set")!.MakeGenericMethod(historyType);
                var dbSet = dbSetMethod.Invoke(_context, null);

                var queryableMethod = dbSet!.GetType().GetMethod("AsQueryable")!;
                var query = (IQueryable<object>)queryableMethod.Invoke(dbSet, null)!;

                var items = await query
                    .OrderByDescending(e => (DateTime)historyType.GetProperty("Timestamp")!.GetValue(e)!)
                    .Take(100) // Limit per entity type
                    .ToListAsync();

                foreach (var item in items)
                {
                    historyItems.Add(new HistoryListItemDto(
                        Id: (int)historyType.GetProperty("Id")!.GetValue(item)!,
                        EntityId: (int)historyType.GetProperty("EntityId")!.GetValue(item)!,
                        EntityType: entityName,
                        Action: (string)historyType.GetProperty("Action")!.GetValue(item)!,
                        Usuario: (string)historyType.GetProperty("Usuario")!.GetValue(item)!,
                        Timestamp: (DateTime)historyType.GetProperty("Timestamp")!.GetValue(item)!,
                        BeforeJson: (string?)historyType.GetProperty("BeforeJson")!.GetValue(item),
                        AfterJson: (string?)historyType.GetProperty("AfterJson")!.GetValue(item)
                    ));
                }
            }

            // Sort by timestamp descending across all types
            var sorted = historyItems.OrderByDescending(h => h.Timestamp).Take(200).ToList();
            return Ok(sorted);
        }

        /// <summary>
        /// Rolls back an entity to its state at a specific history record.
        /// </summary>
        [HttpPost("rollback/{entityType}/{entityId}/{historyId}")]
        public async Task<IActionResult> Rollback(string entityType, int entityId, int historyId)
        {
            // Map entity type name to the entity type
            var entityTypeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                { "Producto", typeof(Producto) },
                { "Compra", typeof(Compra) },
                { "Maquina", typeof(Maquina) },
                { "Venta", typeof(Venta) },
                { "MovimientoCaja", typeof(MovimientoCaja) },
                { "ConfiguracionSlot", typeof(ConfiguracionSlot) },
                { "GastoRecurrente", typeof(GastoRecurrente) },
                { "OrdenCarga", typeof(OrdenCarga) },
                { "User", typeof(User) }
            };

            if (!entityTypeMap.TryGetValue(entityType, out var targetType))
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Resource not found",
                    Detail = $"Entity type '{entityType}' is not supported for rollback.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Map entity type to its history type
            var historyTypeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                { "Producto", typeof(ProductoHistory) },
                { "Compra", typeof(CompraHistory) },
                { "Maquina", typeof(MaquinaHistory) },
                { "Venta", typeof(VentaHistory) },
                { "MovimientoCaja", typeof(MovimientoCajaHistory) },
                { "ConfiguracionSlot", typeof(ConfiguracionSlotHistory) },
                { "GastoRecurrente", typeof(GastoRecurrenteHistory) },
                { "OrdenCarga", typeof(OrdenCargaHistory) },
                { "User", typeof(UserHistory) }
            };

            var historyType = historyTypeMap[entityType];

            // Find the history record
            var historyDbSetMethod = _context.GetType().GetMethod("Set")!.MakeGenericMethod(historyType);
            var historyDbSet = historyDbSetMethod.Invoke(_context, null);
            var historyQueryableMethod = historyDbSet!.GetType().GetMethod("AsQueryable")!;
            var historyQuery = (IQueryable<object>)historyQueryableMethod.Invoke(historyDbSet, null)!;

            var historyRecord = await historyQuery
                .FirstOrDefaultAsync(e => (int)historyType.GetProperty("Id")!.GetValue(e)! == historyId);

            if (historyRecord == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Resource not found",
                    Detail = $"History record with ID {historyId} not found.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Get BeforeJson
            var beforeJson = (string?)historyType.GetProperty("BeforeJson")!.GetValue(historyRecord);
            if (string.IsNullOrEmpty(beforeJson))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad request",
                    Detail = "Cannot rollback: BeforeJson is null. This record may be an Added state.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Deserialize the BeforeJson into the target entity type
            var jsonOptions = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
            var entity = JsonSerializer.Deserialize(beforeJson, targetType, jsonOptions);
            if (entity == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad request",
                    Detail = "Failed to deserialize the history record.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Find the current entity in the database
            var dbSetMethod = _context.GetType().GetMethod("Set")!.MakeGenericMethod(targetType);
            var dbSet = dbSetMethod.Invoke(_context, null);
            var findMethod = dbSet!.GetType().GetMethod("Find", new[] { typeof(object[]) })!;
            var currentEntity = findMethod.Invoke(dbSet, new[] { new object[] { entityId } });

            if (currentEntity == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Resource not found",
                    Detail = $"Entity '{entityType}' with ID {entityId} not found.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Update scalar properties via reflection
            var targetProps = targetType.GetProperties();
            var entityProps = entity.GetType().GetProperties();

            foreach (var prop in targetProps)
            {
                if (prop.Name == "Id") continue; // Don't update the primary key

                var entityProp = entityProps.FirstOrDefault(p => p.Name == prop.Name);
                if (entityProp != null)
                {
                    var value = entityProp.GetValue(entity);
                    // Skip complex types
                    if (value is not string and not ValueType and not null)
                        continue;
                    prop.SetValue(currentEntity, value);
                }
            }

            try
            {
                await _context.SaveChangesAsync();

                return Ok(new RollbackResponseDto(
                    EntityId: entityId,
                    EntityType: entityType,
                    HistoryId: historyId,
                    RolledBackAt: DateTime.UtcNow
                ));
            }
            catch (DbUpdateException ex)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Database constraint violation",
                    Detail = $"Rollback failed due to FK constraint conflict: {ex.InnerException?.Message ?? ex.Message}",
                    Status = StatusCodes.Status409Conflict
                });
            }
        }
    }
}
