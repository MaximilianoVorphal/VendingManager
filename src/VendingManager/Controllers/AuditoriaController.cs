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

            // Each history table queried directly (no dynamic EF Core reflection — avoids SQL translation errors)
            var tasks = new List<Task<List<HistoryListItemDto>>>
            {
                GetHistoryRecords<CompraHistory>("Compra"),
                GetHistoryRecords<ProductoHistory>("Producto"),
                GetHistoryRecords<MaquinaHistory>("Maquina"),
                GetHistoryRecords<VentaHistory>("Venta"),
                GetHistoryRecords<MovimientoCajaHistory>("MovimientoCaja"),
                GetHistoryRecords<ConfiguracionSlotHistory>("ConfiguracionSlot"),
                GetHistoryRecords<GastoRecurrenteHistory>("GastoRecurrente"),
                GetHistoryRecords<OrdenCargaHistory>("OrdenCarga"),
                GetHistoryRecords<UserHistory>("User")
            };

            var results = await Task.WhenAll(tasks);
            foreach (var list in results)
                historyItems.AddRange(list);

            // Sort by timestamp descending across all types (in memory)
            var sorted = historyItems.OrderByDescending(h => h.Timestamp).Take(200).ToList();
            return Ok(sorted);
        }

        private async Task<List<HistoryListItemDto>> GetHistoryRecords<THistory>(string entityName)
            where THistory : class
        {
            var records = await _context.Set<THistory>()
                .AsQueryable()
                .Take(100)
                .ToListAsync();

            var historyType = typeof(THistory);
            var idProp = historyType.GetProperty("Id")!;
            var entityIdProp = historyType.GetProperty("EntityId")!;
            var actionProp = historyType.GetProperty("Action")!;
            var usuarioProp = historyType.GetProperty("Usuario")!;
            var timestampProp = historyType.GetProperty("Timestamp")!;
            var beforeJsonProp = historyType.GetProperty("BeforeJson");
            var afterJsonProp = historyType.GetProperty("AfterJson");

            return records.Select(r => new HistoryListItemDto(
                Id: (int)idProp.GetValue(r)!,
                EntityId: entityIdProp != null ? (int)entityIdProp.GetValue(r)! : 0,
                EntityType: entityName,
                Action: (string)actionProp.GetValue(r)!,
                Usuario: (string)usuarioProp.GetValue(r)!,
                Timestamp: (DateTime)timestampProp.GetValue(r)!,
                BeforeJson: beforeJsonProp?.GetValue(r) as string,
                AfterJson: afterJsonProp?.GetValue(r) as string
            )).ToList();
        }

        /// <summary>
        /// Rolls back an entity to its state at a specific history record.
        /// </summary>
        [HttpPost("rollback/{entityType}/{entityId}/{historyId}")]
        public async Task<IActionResult> Rollback(string entityType, int entityId, int historyId)
        {
            // Find history record and deserialize BeforeJson
            var (beforeJson, historyRecord) = await FindHistoryRecord(entityType, historyId);
            if (historyRecord == null)
                return NotFound(new ProblemDetails
                {
                    Title = "Resource not found",
                    Detail = $"History record with ID {historyId} not found.",
                    Status = StatusCodes.Status404NotFound
                });

            if (string.IsNullOrEmpty(beforeJson))
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad request",
                    Detail = "Cannot rollback: BeforeJson is null. This record may be an Added state.",
                    Status = StatusCodes.Status400BadRequest
                });

            var jsonOptions = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };

            try
            {
                // Dispatch to type-specific rollback handler
                return entityType.ToLowerInvariant() switch
                {
                    "producto" => await RollbackEntity<Producto, ProductoHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "compra" => await RollbackEntity<Compra, CompraHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "maquina" => await RollbackEntity<Maquina, MaquinaHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "venta" => await RollbackEntity<Venta, VentaHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "movimientocaja" => await RollbackEntity<MovimientoCaja, MovimientoCajaHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "configuracionslot" => await RollbackEntity<ConfiguracionSlot, ConfiguracionSlotHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "gastorecurrente" => await RollbackEntity<GastoRecurrente, GastoRecurrenteHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "ordencarga" => await RollbackEntity<OrdenCarga, OrdenCargaHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    "user" => await RollbackEntity<User, UserHistory>(entityId, beforeJson, historyRecord, jsonOptions),
                    _ => NotFound(new ProblemDetails
                    {
                        Title = "Resource not found",
                        Detail = $"Entity type '{entityType}' is not supported for rollback.",
                        Status = StatusCodes.Status404NotFound
                    })
                };
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

        private async Task<(string? beforeJson, object? historyRecord)> FindHistoryRecord(string entityType, int historyId)
        {
            return entityType.ToLowerInvariant() switch
            {
                "producto" => (await FindHistory<ProductoHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "compra" => (await FindHistory<CompraHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "maquina" => (await FindHistory<MaquinaHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "venta" => (await FindHistory<VentaHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "movimientocaja" => (await FindHistory<MovimientoCajaHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "configuracionslot" => (await FindHistory<ConfiguracionSlotHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "gastorecurrente" => (await FindHistory<GastoRecurrenteHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "ordencarga" => (await FindHistory<OrdenCargaHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                "user" => (await FindHistory<UserHistory>(historyId)) is { } h ? (h.BeforeJson, h) : (null, null),
                _ => (null, null)
            };
        }

        private async Task<THistory?> FindHistory<THistory>(int historyId) where THistory : class
        {
            return await _context.Set<THistory>().FindAsync(historyId) as THistory;
        }

        private async Task<IActionResult> RollbackEntity<TEntity, THistory>(int entityId, string beforeJson, object historyRecord, JsonSerializerOptions jsonOptions)
            where TEntity : class
            where THistory : class
        {
            var snapshot = JsonSerializer.Deserialize<TEntity>(beforeJson, jsonOptions);
            if (snapshot == null)
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad request",
                    Detail = "Failed to deserialize the history record.",
                    Status = StatusCodes.Status400BadRequest
                });

            var current = await _context.Set<TEntity>().FindAsync(entityId);
            if (current == null)
                return NotFound(new ProblemDetails
                {
                    Title = "Resource not found",
                    Detail = $"Entity with ID {entityId} not found.",
                    Status = StatusCodes.Status404NotFound
                });

            // Copy scalar properties from snapshot to current entity
            foreach (var prop in typeof(TEntity).GetProperties())
            {
                if (prop.Name == "Id") continue;
                var value = prop.GetValue(snapshot);
                if (value is not string and not ValueType and not null) continue;
                prop.SetValue(current, value);
            }

            await _context.SaveChangesAsync();

            return Ok(new RollbackResponseDto(
                EntityId: entityId,
                EntityType: typeof(TEntity).Name,
                HistoryId: (int)typeof(THistory).GetProperty("Id")!.GetValue(historyRecord)!,
                RolledBackAt: DateTime.UtcNow
            ));
        }
    }
}
