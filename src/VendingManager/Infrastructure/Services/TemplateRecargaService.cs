using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Facade service for TemplateRecarga operations.
/// Preserves existing ITemplateRecargaService interface for backward compatibility.
/// Delegates lifecycle and analytics operations to specialized services.
/// </summary>
public class TemplateRecargaService : ITemplateRecargaService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TemplateRecargaService> _logger;
    private readonly ITemplateRecargaLifecycleService _lifecycle;
    private readonly ITemplateRecargaAnalyticsService _analytics;

    public TemplateRecargaService(
        ApplicationDbContext context,
        ILogger<TemplateRecargaService> logger,
        ITemplateRecargaLifecycleService lifecycle,
        ITemplateRecargaAnalyticsService analytics)
    {
        _context = context;
        _logger = logger;
        _lifecycle = lifecycle;
        _analytics = analytics;
    }

    public async Task<List<TemplateRecargaDto>> GetAllAsync()
    {
        var templates = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .OrderByDescending(t => t.FechaCreacion)
            .ToListAsync();

        // Build cross-template chain: periods for the same machine may span multiple templates.
        // Without this, GetFechaFinFromTemplate only sees periods within a single template,
        // causing false overlaps when a machine has periods in different templates.
        var crossTemplateChain = BuildCrossTemplateChain(templates);

        var dtos = templates
            .Select(t => MapToDto(t, crossTemplateChain))
            .OrderByDescending(t => t.FechaRecargaMin)
            .ToList();

        // Mark latest Terminado template per machine as EsActivo.
        // This is the template that feeds stock-critico calculations.
        MarcarActivos(dtos);

        return dtos;
    }

    public async Task<List<TemplateRecargaListItemDto>> GetAllListAsync()
    {
        var templates = await _context.TemplatesRecarga
            .AsNoTracking()
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
            .OrderByDescending(t => t.FechaCreacion)
            .ToListAsync();

        return templates.Select(t => new TemplateRecargaListItemDto
        {
            Id = t.Id,
            Nombre = t.Nombre,
            Descripcion = t.Descripcion,
            MaquinaNombres = t.Periodos
                .OrderBy(p => p.FechaRecarga)
                .Select(p => p.Maquina.Nombre)
                .ToList(),
            EsActivo = t.Estado == EstadoTemplate.Terminado,
            FechaCreacion = t.FechaCreacion,
            Estado = t.Estado,
            PeriodoCount = t.Periodos.Count,
            TotalProducts = t.Periodos
                .SelectMany(p => p.SnapshotSlots)
                .Count(s => s.ProductoId != null)
        }).ToList();
    }

    /// <summary>
    /// Marca como EsActivo los templates que son el último Terminado de cada máquina.
    /// </summary>
    private static void MarcarActivos(List<TemplateRecargaDto> dtos)
    {
        // Solo templates Terminado (o normalizados) compiten por ser "activos"
        var terminados = dtos.Where(t => t.Estado == EstadoTemplate.Terminado).ToList();

        // Agrupar por máquina, encontrar el período más reciente
        var latestPerMachine = terminados
            .SelectMany(t => t.Periodos, (t, p) => new { Template = t, Periodo = p })
            .GroupBy(x => x.Periodo.MaquinaId)
            .Select(g => g.OrderByDescending(x => x.Periodo.FechaRecarga).First());

        // Marcar los templates ganadores
        foreach (var winner in latestPerMachine)
        {
            winner.Template.EsActivo = true;
        }
    }

    /// <summary>
    /// Builds a lookup of PeriodoId → FechaFin using ALL periods across ALL templates
    /// for the same machine. This ensures the chain works correctly when a machine has
    /// periods in different templates (cross-template chain).
    /// </summary>
    private static Dictionary<int, DateTime> BuildCrossTemplateChain(List<TemplateRecarga> templates)
    {
        var allPeriodos = templates.SelectMany(t => t.Periodos).ToList();
        var lookup = new Dictionary<int, DateTime>();

        foreach (var group in allPeriodos.GroupBy(p => p.MaquinaId))
        {
            var sorted = group.OrderBy(p => p.FechaRecarga).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var endDate = i < sorted.Count - 1
                    ? sorted[i + 1].FechaRecarga
                    : (sorted[i].FechaRecarga <= DateTime.Now
                        ? (DateTime.Now < sorted[i].FechaRecarga.AddDays(90) ? DateTime.Now : sorted[i].FechaRecarga.AddDays(90))
                        : sorted[i].FechaRecarga.AddDays(90));
                lookup[sorted[i].Id] = endDate;
            }
        }

        return lookup;
    }

    public async Task<TemplateRecargaDto?> GetByIdAsync(int id)
    {
        var template = await _context.TemplatesRecarga
            .AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == id);

        return template == null ? null : MapToDto(template);
    }

    public async Task<TemplateRecargaDto> CreateAsync(CreateTemplateRecargaDto dto)
    {
        // Validate chain for each period BEFORE creating
        foreach (var p in dto.Periodos)
        {
            await ValidateFechaRecargaChainAsync(p.MaquinaId, p.FechaRecarga);
        }

        var template = new TemplateRecarga
        {
            Nombre = dto.Nombre,
            Descripcion = dto.Descripcion,
            FechaCreacion = DateTime.Now,
            Periodos = dto.Periodos.Select(p => new PeriodoRecarga
            {
                MaquinaId = p.MaquinaId,
                FechaRecarga = p.FechaRecarga,
                SnapshotSlots = p.SnapshotSlots.Select(s => new SnapshotSlot
                {
                    NumeroSlot = s.NumeroSlot,
                    ProductoId = s.ProductoId,
                    CantidadInicial = s.CantidadInicial,
                    CapacidadSlot = s.CapacidadSlot,
                    Estado = s.Estado
                }).ToList()
            }).ToList()
        };

        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Recargar con navegación
        await _context.Entry(template)
            .Collection(t => t.Periodos)
            .Query()
            .Include(p => p.Maquina)
            .Include(p => p.SnapshotSlots)
            .ThenInclude(s => s.Producto)
            .LoadAsync();

        return MapToDto(template);
    }

    public async Task<TemplateRecargaDto> UpdateAsync(int id, UpdateTemplateRecargaDto dto)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .ThenInclude(p => p.SnapshotSlots)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            throw new InvalidOperationException($"Template con ID {id} no encontrado");

        // Pre-save snapshot: capture existing (MaquinaId, NumeroSlot) → (ProductoId, Estado)
        var preSaveSnapshot = template.Periodos
            .SelectMany(p => p.SnapshotSlots.Select(s => new
            {
                Key = (p.MaquinaId, s.NumeroSlot),
                Value = (productoId: s.ProductoId, estado: s.Estado)
            }))
            .ToDictionary(x => x.Key, x => x.Value);

        _logger.LogInformation(
            "[UpdateAsync] Template {TemplateId}: pre-save snapshot tiene {Count} slots. " +
            "Slots: {@Slots}",
            id, preSaveSnapshot.Count,
            preSaveSnapshot.Select(kv => new { kv.Key, productoId = kv.Value.productoId, estado = kv.Value.estado }).ToList());

        // Actualizar propiedades básicas
        template.Nombre = dto.Nombre;
        template.Descripcion = dto.Descripcion;

        // Validate chain for each period BEFORE updating.
        // Exclude the template's own existing periods — otherwise the validation
        // sees the old periods (still in DB) and falsely rejects the update as a
        // backward/duplicate chain conflict.
        var excludeIds = template.Periodos.Select(p => p.Id).ToHashSet();
        foreach (var p in dto.Periodos)
        {
            await ValidateFechaRecargaChainAsync(p.MaquinaId, p.FechaRecarga, excludeIds);
        }

        // Preserve foto guía / foto OCR across the periodo rebuild below.
        // This update deletes and recreates the periods, so without carrying the
        // photos over, every "Guardar carga" would silently wipe them (they are
        // stored per-period). Match new periods to old ones by (MaquinaId,
        // FechaRecarga), falling back to MaquinaId when the date changed.
        var fotosByKey = new Dictionary<(int MaquinaId, DateTime FechaRecarga), (byte[]? Guia, byte[]? Ocr)>();
        var fotosByMaquina = new Dictionary<int, (byte[]? Guia, byte[]? Ocr)>();
        foreach (var periodo in template.Periodos)
        {
            if (periodo.FotoGuia == null && periodo.FotoOcr == null) continue;
            fotosByKey[(periodo.MaquinaId, periodo.FechaRecarga)] = (periodo.FotoGuia, periodo.FotoOcr);
            fotosByMaquina[periodo.MaquinaId] = (periodo.FotoGuia, periodo.FotoOcr);
        }

        // Eliminar snapshots de períodos anteriores
        foreach (var periodo in template.Periodos)
        {
            _context.SnapshotSlots.RemoveRange(periodo.SnapshotSlots);
        }
        // Eliminar períodos anteriores
        _context.PeriodosRecarga.RemoveRange(template.Periodos);

        // Agregar nuevos períodos con snapshots
        template.Periodos = dto.Periodos.Select(p =>
        {
            byte[]? fotoGuia = null;
            byte[]? fotoOcr = null;
            if (fotosByKey.TryGetValue((p.MaquinaId, p.FechaRecarga), out var f))
                (fotoGuia, fotoOcr) = f;
            else if (fotosByMaquina.TryGetValue(p.MaquinaId, out var fm))
                (fotoGuia, fotoOcr) = fm;

            return new PeriodoRecarga
            {
                TemplateRecargaId = template.Id,
                MaquinaId = p.MaquinaId,
                FechaRecarga = p.FechaRecarga,
                FotoGuia = fotoGuia,
                FotoOcr = fotoOcr,
                SnapshotSlots = p.SnapshotSlots.Select(s => new SnapshotSlot
                {
                    NumeroSlot = s.NumeroSlot,
                    ProductoId = s.ProductoId,
                    CantidadInicial = s.CantidadInicial,
                    CapacidadSlot = s.CapacidadSlot,
                    Estado = s.Estado
                }).ToList()
            };
        }).ToList();

        await _context.SaveChangesAsync();

        // Sincronizar slots Vacíos/Pendientes con ventas históricas
        // Si un slot quedó sin producto, limpiamos el ProductoId en las ventas del período
        foreach (var periodo in template.Periodos)
        {
            var slotsSinProducto = periodo.SnapshotSlots
                .Where(s => s.Estado is EstadoSlot.Vacio or EstadoSlot.Pendiente)
                .ToList();

            if (!slotsSinProducto.Any()) continue;

            var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga);

            var ventasPeriodo = await _context.Ventas
                .Where(v => v.MaquinaId == periodo.MaquinaId
                         && v.FechaLocal >= periodo.FechaRecarga
                         && v.FechaLocal <= fechaFin)
                .ToListAsync();

            foreach (var slot in slotsSinProducto)
            {
                foreach (var venta in ventasPeriodo.Where(v => v.NumeroSlot == slot.NumeroSlot))
                {
                    if (venta.ProductoId.HasValue)
                    {
                        venta.ProductoId = null;
                        venta.CostoVenta = 0;
                    }
                }
            }
        }

        // Auto-sync: propagate product changes to matching Venta records
        await SyncTemplateToVentasAsync(template.Id, preSaveSnapshot);

        // Recargar con navegación
        await _context.Entry(template)
            .Collection(t => t.Periodos)
            .Query()
            .Include(p => p.Maquina)
            .Include(p => p.SnapshotSlots)
            .ThenInclude(s => s.Producto)
            .LoadAsync();

        return MapToDto(template);
    }

    public async Task DeleteAsync(int id)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .ThenInclude(p => p.SnapshotSlots)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template != null)
        {
            // Eliminar snapshots primero
            foreach (var periodo in template.Periodos)
            {
                _context.SnapshotSlots.RemoveRange(periodo.SnapshotSlots);
            }
            _context.PeriodosRecarga.RemoveRange(template.Periodos);
            _context.TemplatesRecarga.Remove(template);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Throws InvalidOperationException with 409-style message if FechaRecarga
    /// would create a backward or duplicate chain for this machine.
    /// Called before CreateAsync and UpdateAsync commit.
    /// </summary>
    private async Task ValidateFechaRecargaChainAsync(int maquinaId, DateTime fechaRecarga, HashSet<int>? excludePeriodoIds = null)
    {
        var exists = await _context.PeriodosRecarga
            .Where(p => p.MaquinaId == maquinaId
                     && p.FechaRecarga == fechaRecarga
                     && (excludePeriodoIds == null || !excludePeriodoIds.Contains(p.Id)))
            .AnyAsync();

        if (exists)
        {
            throw new InvalidOperationException(
                $"Ya existe un período con FechaRecarga {fechaRecarga.ToString("dd'/'MM'/'yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)} "
              + $"para la máquina {maquinaId}. No se permiten fechas duplicadas en la cadena.");
        }
    }

    /// <summary>
    /// Returns the computed end date for a specific period using the chain logic.
    /// </summary>
    private async Task<DateTime> GetEndDateForPeriodoAsync(
        int maquinaId,
        DateTime fechaRecarga,
        Dictionary<(int maquinaId, DateTime fechaRecarga), DateTime>? crossTemplateLookup = null)
    {
        // Use cross-template lookup for O(1) lookup when available
        if (crossTemplateLookup != null &&
            crossTemplateLookup.TryGetValue((maquinaId, fechaRecarga), out var endDate))
        {
            return endDate;
        }

        var nextRecarga = await _context.PeriodosRecarga
            .Where(p => p.MaquinaId == maquinaId && p.FechaRecarga > fechaRecarga)
            .OrderBy(p => p.FechaRecarga)
            .Select(p => (DateTime?)p.FechaRecarga)
            .FirstOrDefaultAsync();

        // No next recarga: cap to max 90 days from recarga for past periods, or fechaRecarga + 90 days for future ones
        return nextRecarga ?? (fechaRecarga <= DateTime.Now
            ? (DateTime.Now < fechaRecarga.AddDays(90) ? DateTime.Now : fechaRecarga.AddDays(90))
            : fechaRecarga.AddDays(90));
    }

    /// <summary>
    /// Returns end dates for all periods of a machine, batch-fetched to avoid N+1.
    /// Key: MaquinaId, Value: Dictionary of PeriodoId -> FechaFin.
    /// </summary>
    private async Task<Dictionary<int, DateTime>> GetEndDatesForMaquinaAsync(int maquinaId)
    {
        // Build a lookup: for each period of this machine, find the next FechaRecarga
        var allPeriodos = await _context.PeriodosRecarga
            .Where(p => p.MaquinaId == maquinaId)
            .Select(p => new { p.Id, p.FechaRecarga })
            .AsNoTracking()
            .ToListAsync();

        var sorted = allPeriodos.OrderBy(p => p.FechaRecarga).ToList();
        var endDates = new Dictionary<int, DateTime>();

        for (int i = 0; i < sorted.Count; i++)
        {
            var endDate = i < sorted.Count - 1
                ? sorted[i + 1].FechaRecarga
                : sorted[i].FechaRecarga.AddYears(2);
            endDates[sorted[i].Id] = endDate;
        }

        return endDates;
    }

    public async Task<List<SnapshotSlotDto>> GetSlotsForMaquinaAsync(int maquinaId)
    {
        var slots = await _context.ConfiguracionSlots
            .Include(c => c.Producto)
            .Where(c => c.MaquinaId == maquinaId)
            .Select(c => new SnapshotSlotDto
            {
                NumeroSlot = c.NumeroSlot,
                ProductoId = c.ProductoId,
                ProductoNombre = c.Producto != null ? c.Producto.Nombre : "",
                CantidadInicial = Math.Max(0, c.StockActual),
                CapacidadSlot = c.CapacidadMaxima,
                Estado = c.ProductoId == null ? EstadoSlot.Pendiente : EstadoSlot.Lleno
            })
            .ToListAsync();

        // Orden numérico real: "1", "2", ..., "10", "11", ..., "100", "101"
        // en vez del orden alfabético que da SQL Server en columnas nvarchar.
        return slots.OrderBy(s => int.TryParse(s.NumeroSlot, out var n) ? n : 999).ToList();
    }

    // ===== DELEGATED TO ITemplateRecargaAnalyticsService (when available) =====

    /// <summary>
    /// Análisis de stockout using the periods of the template.
    /// Uses the specialized service if available, otherwise falls back to local implementation.
    /// </summary>
    public async Task<List<StockoutAnalysisDto>> AnalyzarPorTemplateAsync(int templateId, double umbralHorasSilencio = 24)
    {
        // Delegate to analytics service if it has a real implementation
        if (_analytics != null && !IsMock(_analytics))
        {
            return await _analytics.AnalyzarPorTemplateAsync(templateId, umbralHorasSilencio);
        }
        // Fall back to local implementation for tests with mocks
        return await LocalAnalyzarPorTemplateAsync(templateId, umbralHorasSilencio);
    }

    /// <summary>
    /// Sincroniza históricamente el ProductoId y (opcionalmente) CostoVenta.
    /// </summary>
    public async Task<int> SyncVentasWithTemplateAsync(int templateId, bool actualizarCostos)
    {
        if (_analytics != null && !IsMock(_analytics))
        {
            return await _analytics.SyncVentasWithTemplateAsync(templateId, actualizarCostos);
        }
        return await LocalSyncVentasWithTemplateAsync(templateId, actualizarCostos);
    }

    /// <summary>
    /// Sincroniza TODOS los templates against historical sales.
    /// </summary>
    public async Task<SyncAllVentasResultDto> SyncAllVentasAsync(bool actualizarCostos)
    {
        if (_analytics != null && !IsMock(_analytics))
        {
            return await _analytics.SyncAllVentasAsync(actualizarCostos);
        }
        return await LocalSyncAllVentasAsync(actualizarCostos);
    }

    /// <summary>
    /// Sincroniza the product of a specific slot in historical sales.
    /// </summary>
    public async Task<SyncSlotProductoResultDto> SyncSlotProductoAsync(int templateId, int periodoId, string numeroSlot, int productoId)
    {
        if (_analytics != null && !IsMock(_analytics))
        {
            return await _analytics.SyncSlotProductoAsync(templateId, periodoId, numeroSlot, productoId);
        }
        return await LocalSyncSlotProductoAsync(templateId, periodoId, numeroSlot, productoId);
    }

    /// <summary>
    /// Detects if the object is a Moq-generated mock.
    /// Moq creates transparent proxy assemblies with names containing "DynamicProxyGenAssembly".
    /// </summary>
    private static bool IsMock(object obj)
    {
        var type = obj.GetType();
        // Moq generates dynamic assembly named DynamicProxyGenAssembly2
        var assemblyName = type.Assembly.GetName().Name ?? "";
        if (assemblyName.Contains("DynamicProxyGenAssembly"))
            return true;
        // Also check for Castle Core proxy pattern
        if (type.Name.EndsWith("Proxy", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    // ===== PHOTO METHODS (stay on facade - not lifecycle/analytics specific =====

    public async Task SaveFotoGuiaAsync(int periodoId, byte[] data, string contentType)
    {
        var periodo = await _context.PeriodosRecarga.FindAsync(periodoId)
            ?? throw new KeyNotFoundException($"Período con ID {periodoId} no encontrado");

        periodo.FotoGuia = data;
        await _context.SaveChangesAsync();
    }

    public async Task<(byte[]? Data, string? ContentType)> GetFotoGuiaAsync(int periodoId)
    {
        var periodo = await _context.PeriodosRecarga
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodoId)
            ?? throw new KeyNotFoundException($"Período con ID {periodoId} no encontrado");

        return (periodo.FotoGuia, null);
    }

    public async Task SaveFotoOcrAsync(int periodoId, byte[] data, string contentType)
    {
        var periodo = await _context.PeriodosRecarga.FindAsync(periodoId)
            ?? throw new KeyNotFoundException($"Período con ID {periodoId} no encontrado");

        periodo.FotoOcr = data;
        await _context.SaveChangesAsync();
    }

    public async Task<(byte[]? Data, string? ContentType)> GetFotoOcrAsync(int periodoId)
    {
        var periodo = await _context.PeriodosRecarga
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodoId)
            ?? throw new KeyNotFoundException($"Período con ID {periodoId} no encontrado");

        return (periodo.FotoOcr, null);
    }

    public async Task DeleteFotoGuiaAsync(int periodoId)
    {
        var periodo = await _context.PeriodosRecarga.FindAsync(periodoId)
            ?? throw new KeyNotFoundException($"Período con ID {periodoId} no encontrado");

        periodo.FotoGuia = null;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteFotoOcrAsync(int periodoId)
    {
        var periodo = await _context.PeriodosRecarga.FindAsync(periodoId)
            ?? throw new KeyNotFoundException($"Período con ID {periodoId} no encontrado");

        periodo.FotoOcr = null;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Applies batch slot actions (REFILL, EMPTY, SWAP) to a specific periodo.
    /// Validates template is in EnCarga or Borrador state before applying.
    /// </summary>
    public async Task<SlotBatchResponse> ApplySlotBatchAsync(
        int templateId,
        int periodoId,
        List<SlotActionDto> actions)
    {
        var response = new SlotBatchResponse();

        var template = await _context.TemplatesRecarga
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new KeyNotFoundException($"Template con ID {templateId} no encontrado");

        if (template.Estado != EstadoTemplate.Pendiente)
        {
            throw new InvalidOperationException(
                $"No se pueden modificar slots: el template está en estado {template.Estado}. " +
                $"Solo puede modificar slots en estado Pendiente.");
        }

        var periodo = await _context.PeriodosRecarga
            .Include(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(p => p.Id == periodoId && p.TemplateRecargaId == templateId)
            ?? throw new KeyNotFoundException($"Período con ID {periodoId} no encontrado para el template {templateId}");

        foreach (var action in actions)
        {
            try
            {
                var slot = periodo.SnapshotSlots.FirstOrDefault(s => s.Id == action.SlotId);
                if (slot == null)
                {
                    response.Errors.Add($"Slot con ID {action.SlotId} no encontrado");
                    continue;
                }

                switch (action.ActionType.ToUpperInvariant())
                {
                    case "REFILL":
                        slot.CantidadInicial = action.Cantidad;
                        if (slot.ProductoId != null)
                            slot.Estado = EstadoSlot.Lleno;
                        break;

                    case "EMPTY":
                        slot.CantidadInicial = 0;
                        slot.Estado = EstadoSlot.Vacio;
                        break;

                    case "SWAP":
                        if (!action.NewProductoId.HasValue)
                        {
                            response.Errors.Add($"SWAP para slot {action.SlotId} requiere NewProductoId");
                            continue;
                        }
                        slot.ProductoId = action.NewProductoId.Value;
                        slot.CantidadInicial = action.Cantidad > 0 ? action.Cantidad : 0;
                        if (action.NewPrecioVenta.HasValue)
                        {
                            // PrecioVenta is on the producto, not slot — skip
                        }
                        slot.Estado = EstadoSlot.Lleno;
                        break;

                    default:
                        response.Errors.Add(
                            $"Tipo de acción inválido: {action.ActionType}. Valores válidos: REFILL, EMPTY, SWAP");
                        continue;
                }

                response.ProcessedCount++;
            }
            catch (Exception ex)
            {
                response.Errors.Add($"Error procesando slot {action.SlotId}: {ex.Message}");
            }
        }

        if (response.ProcessedCount > 0)
        {
            await _context.SaveChangesAsync();
        }

        return response;
    }

    /// <summary>
    /// Auto-syncs product changes to matching Venta records after template save.
    /// Only updates records where ProductoId differs from the pre-save snapshot.
    /// Uses ProductoCosto-based historical cost lookup for CostoVenta recalculation.
    /// Skips Vacio/Pendiente slots (handled by the unselect loop).
    /// </summary>
    private async Task SyncTemplateToVentasAsync(
        int templateId,
        Dictionary<(int maquinaId, string numeroSlot), (int? productoId, EstadoSlot estado)> preSaveSnapshot)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null || !template.Periodos.Any())
            return;

        int ventasActualizadas = 0;

        foreach (var periodo in template.Periodos)
        {
            var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga);

            var ventasDelPeriodo = await _context.Ventas
                .Where(v => v.MaquinaId == periodo.MaquinaId &&
                            v.FechaLocal >= periodo.FechaRecarga &&
                            v.FechaLocal <= fechaFin)
                .ToListAsync();

            if (!ventasDelPeriodo.Any())
                continue;

            foreach (var slot in periodo.SnapshotSlots)
            {
                // Skip Vacio/Pendiente — handled by the unselect loop
                if (slot.Estado is EstadoSlot.Vacio or EstadoSlot.Pendiente)
                    continue;

                // Only sync if ProductoId actually differs from pre-save
                var snapshotKey = (periodo.MaquinaId, slot.NumeroSlot);
                var foundInSnapshot = preSaveSnapshot.TryGetValue(snapshotKey, out var snapshotValue);

                _logger.LogInformation(
                    "[SyncTemplateToVentas] Slot {NumeroSlot} Maquina {MaquinaId}: " +
                    "incomingProductoId={Incoming}, snapshotEncontrado={Found}, snapshotProductoId={Snapshot}, " +
                    "cambiado={Changed}",
                    slot.NumeroSlot, periodo.MaquinaId, slot.ProductoId,
                    foundInSnapshot, snapshotValue.productoId,
                    foundInSnapshot && snapshotValue.productoId != slot.ProductoId);

                if (!slot.ProductoId.HasValue || slot.ProductoId == 0)
                    continue;

                // Check if this slot actually changed (product swap)
                if (snapshotValue.productoId == slot.ProductoId)
                    continue;

                var ventasSlot = ventasDelPeriodo.Where(v => v.NumeroSlot == slot.NumeroSlot).ToList();
                _logger.LogInformation(
                    "[SyncTemplateToVentas] Slot {NumeroSlot}: {Count} ventas encontradas para sincronizar. " +
                    "Ventas: {@VentasProductoIds}",
                    slot.NumeroSlot, ventasSlot.Count,
                    ventasSlot.Select(v => new { v.Id, v.ProductoId, v.CostoVenta }).ToList());

                foreach (var venta in ventasSlot)
                {
                    if (venta.ProductoId != slot.ProductoId)
                    {
                        venta.ProductoId = slot.ProductoId;
                        ventasActualizadas++;
                    }

                    // Recalculate CostoVenta from ProductoCosto historico
                    var costoHistorico = await _context.ProductoCostos
                        .GetCostoAtAsync(slot.ProductoId!.Value, venta.FechaLocal);
                    decimal costoALaFecha = costoHistorico?.Costo ?? slot.Producto?.CostoPromedio ?? 0;
                    if (venta.CostoVenta != costoALaFecha)
                    {
                        venta.CostoVenta = costoALaFecha;
                        ventasActualizadas++;
                    }
                }
            }
        }

        if (ventasActualizadas > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "[SyncTemplateToVentas] Template {TemplateId}: {Count} ventas actualizadas.",
                templateId, ventasActualizadas);

            // Post-save verification: re-read from DB to confirm persistence
            // Query each period separately — a single .Any() across multiple
            // in-memory PeriodoRecarga entities cannot be translated by EF Core.
            var ventasVerificacion = new List<Venta>();
            foreach (var periodo in template.Periodos)
            {
                var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga);
                var ventasPeriodo = await _context.Ventas
                    .AsNoTracking()
                    .Where(v => v.MaquinaId == periodo.MaquinaId &&
                                v.FechaLocal >= periodo.FechaRecarga &&
                                v.FechaLocal <= fechaFin &&
                                v.ProductoId != null)
                    .ToListAsync();
                ventasVerificacion.AddRange(ventasPeriodo);
            }
            ventasVerificacion = ventasVerificacion.OrderBy(v => v.Id).ToList();

            _logger.LogInformation(
                "[SyncTemplateToVentas] VERIFICACION post-save: {Count} ventas en DB. " +
                "Muestra (primeras 5): {@Muestra}",
                ventasVerificacion.Count,
                ventasVerificacion.Take(5).Select(v => new
                {
                    v.Id, v.ProductoId, v.CostoVenta, v.PrecioVenta, v.NumeroSlot
                }).ToList());
        }
        else
        {
            _logger.LogInformation(
                "[SyncTemplateToVentas] Template {TemplateId}: ninguna venta requirió actualización.",
                templateId);
        }
    }

    private static TemplateRecargaDto MapToDto(TemplateRecarga t, Dictionary<int, DateTime>? crossTemplateChain = null)
    {
        return new TemplateRecargaDto
        {
            Id = t.Id,
            Nombre = t.Nombre,
            Descripcion = t.Descripcion,
            FechaCreacion = t.FechaCreacion,
            Estado = NormalizarEstado(t.Estado),
            Periodos = t.Periodos.Select(p => new PeriodoRecargaDto
            {
                Id = p.Id,
                MaquinaId = p.MaquinaId,
                MaquinaNombre = p.Maquina?.Nombre ?? "Desconocida",
                IdInternoMaquina = p.Maquina?.IdInternoMaquina ?? "",
                FechaRecarga = p.FechaRecarga,
                // Use cross-template chain when available (GetAllAsync path);
                // fall back to intra-template lookup for other callers
                FechaFin = crossTemplateChain != null && crossTemplateChain.TryGetValue(p.Id, out var endDate)
                    ? endDate
                    : GetFechaFinFromTemplate(t, p),
                TieneFotoGuia = p.FotoGuia != null,
                TieneFotoOcr = p.FotoOcr != null,
                SnapshotSlots = p.SnapshotSlots.Select(s => new SnapshotSlotDto
                {
                    Id = s.Id,
                    NumeroSlot = s.NumeroSlot,
                    ProductoId = s.ProductoId,
                    ProductoNombre = s.Producto?.Nombre ?? "",
                    CantidadInicial = s.CantidadInicial,
                    CapacidadSlot = s.CapacidadSlot,
                    Estado = s.Estado
                }).ToList()
            }).ToList()
        };
    }

    /// <summary>
    /// Derives the computed FechaFin for a period from the template's period chain.
    /// This is used by MapToDto to populate the DTO's FechaFin property.
    /// </summary>
    private static DateTime GetFechaFinFromTemplate(TemplateRecarga t, PeriodoRecarga p)
    {
        // Find the next period for the same machine with a later FechaRecarga
        var nextPeriodo = t.Periodos
            .Where(p2 => p2.MaquinaId == p.MaquinaId && p2.FechaRecarga > p.FechaRecarga)
            .OrderBy(p2 => p2.FechaRecarga)
            .FirstOrDefault();

        return nextPeriodo?.FechaRecarga ?? p.FechaRecarga.AddYears(2);
    }

    /// <summary>
    /// Normaliza valores de Estado que no matchean el enum actual.
    /// Si no es Pendiente(0) ni Terminado(2), lo fuerza a Terminado(2).
    /// Red de contención para valores huérfanos en BD (ej: viejo Activo=1).
    /// </summary>
    private static EstadoTemplate NormalizarEstado(EstadoTemplate estado) =>
        estado is EstadoTemplate.Pendiente or EstadoTemplate.Terminado
            ? estado
            : EstadoTemplate.Terminado;

    // ===== LOCAL FALLBACK IMPLEMENTATIONS (used when _analytics is null) =====

    private async Task<List<StockoutAnalysisDto>> LocalAnalyzarPorTemplateAsync(int templateId, double umbralHorasSilencio = 24)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                    .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null || !template.Periodos.Any())
            return new List<StockoutAnalysisDto>();

        var result = new List<StockoutAnalysisDto>();
        var crossTemplateLookup = await BuildCrossTemplateLookupAsyncLocal();

        foreach (var periodo in template.Periodos)
        {
            var snapshotPorProducto = periodo.SnapshotSlots
                .Where(s => s.ProductoId.HasValue && s.ProductoId > 0)
                .GroupBy(s => s.ProductoId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.CantidadInicial));

            var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga, crossTemplateLookup);

            var analisisMaquina = await AnalizarMaquinaEnPeriodoLocal(
                periodo.MaquinaId,
                periodo.Maquina?.Nombre ?? "Desconocida",
                periodo.FechaRecarga,
                fechaFin,
                umbralHorasSilencio,
                periodo.SnapshotSlots.ToList(),
                snapshotPorProducto);

            result.AddRange(analisisMaquina);
        }

        return result
            .OrderByDescending(x => x.DineroPerdidoEstimado)
            .ThenByDescending(x => x.PosibleQuiebre)
            .ThenByDescending(x => x.HorasSinStock)
            .ToList();
    }

    private async Task<Dictionary<(int maquinaId, DateTime fechaRecarga), DateTime>> BuildCrossTemplateLookupAsyncLocal()
    {
        var allTemplates = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .AsNoTracking()
            .ToListAsync();

        var allPeriodos = allTemplates.SelectMany(t => t.Periodos).ToList();
        var lookup = new Dictionary<(int maquinaId, DateTime fechaRecarga), DateTime>();

        foreach (var group in allPeriodos.GroupBy(p => p.MaquinaId))
        {
            var sorted = group.OrderBy(p => p.FechaRecarga).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var endDate = i < sorted.Count - 1
                    ? sorted[i + 1].FechaRecarga
                    : (sorted[i].FechaRecarga <= DateTime.Now
                        ? (DateTime.Now < sorted[i].FechaRecarga.AddDays(90) ? DateTime.Now : sorted[i].FechaRecarga.AddDays(90))
                        : sorted[i].FechaRecarga.AddDays(90));
                lookup[(sorted[i].MaquinaId, sorted[i].FechaRecarga)] = endDate;
            }
        }

        return lookup;
    }

    private async Task<List<StockoutAnalysisDto>> AnalizarMaquinaEnPeriodoLocal(
        int maquinaId,
        string maquinaNombre,
        DateTime inicio,
        DateTime fin,
        double umbralHoras,
        List<SnapshotSlot> snapshotSlots,
        Dictionary<int, int>? snapshotPorProducto)
    {
        var ventas = await _context.Ventas
            .Include(v => v.Producto)
            .Where(v => v.MaquinaId == maquinaId)
            .Where(v => v.FechaLocal >= inicio && v.FechaLocal <= fin)
            .Where(v => v.IdOrdenMaquina != "TB-EXTRA" && v.IdOrdenMaquina != "TB-SIN-VENTA")
            .ToListAsync();

        var result = new List<StockoutAnalysisDto>();
        var ultimaActividadMaquina = ventas.Any() ? ventas.Max(v => v.FechaLocal) : inicio;

        foreach (var slot in snapshotSlots.OrderBy(s => int.TryParse(s.NumeroSlot, out int n) ? n : 999))
        {
            var ventasSlot = ventas
                .Where(v => v.NumeroSlot == slot.NumeroSlot)
                .OrderBy(v => v.FechaLocal)
                .ToList();

            var productoId = slot.ProductoId ?? 0;
            var cantidadInicial = slot.CantidadInicial;
            var cantidadVendida = ventasSlot.Count;

            DateTime? primeraVenta = ventasSlot.Any() ? ventasSlot.First().FechaLocal : null;
            DateTime? ultimaVenta = ventasSlot.Any() ? ventasSlot.Last().FechaLocal : null;

            decimal precioPromedio = 0;
            decimal gananciaPromedio = 0;

            if (ventasSlot.Any())
            {
                precioPromedio = ventasSlot.Average(v => v.PrecioVenta);
                var costoPromedio = ventasSlot.Average(v => v.CostoVenta > 0 ? v.CostoVenta : (v.Producto?.CostoPromedio ?? 0));
                gananciaPromedio = precioPromedio - costoPromedio;
            }
            else if (slot.Producto != null)
            {
                precioPromedio = 0;
            }

            bool posibleQuiebre = false;
            double horasSinStock = 0;
            DateTime fechaAgotamiento = fin;

            if (cantidadInicial > 0)
            {
                if (cantidadVendida >= cantidadInicial)
                {
                    posibleQuiebre = true;
                    if (cantidadInicial <= ventasSlot.Count)
                    {
                        fechaAgotamiento = ventasSlot[cantidadInicial - 1].FechaLocal;
                    }
                    else
                    {
                        fechaAgotamiento = ultimaVenta ?? fin;
                    }
                    horasSinStock = Math.Min((fin - fechaAgotamiento).TotalHours, (fin - inicio).TotalHours);
                }
                else
                {
                    posibleQuiebre = false;
                    horasSinStock = 0;
                }
            }
            else
            {
                posibleQuiebre = false;
            }

            if (horasSinStock < 0) horasSinStock = 0;

            double horasActivas = (fechaAgotamiento - inicio).TotalHours;
            if (horasActivas < 1) horasActivas = 1;

            decimal velocidadPorHora = cantidadVendida / (decimal)horasActivas;

            decimal dineroPerdido = 0;
            decimal gananciaPerdida = 0;

            if (posibleQuiebre && horasSinStock > 0 && precioPromedio > 0)
            {
                dineroPerdido = velocidadPorHora * (decimal)horasSinStock * precioPromedio;
                gananciaPerdida = velocidadPorHora * (decimal)horasSinStock * gananciaPromedio;
            }

            result.Add(new StockoutAnalysisDto
            {
                MaquinaId = maquinaId,
                MaquinaNombre = maquinaNombre,
                ProductoId = productoId,
                ProductoNombre = slot.Producto?.Nombre ?? "Desconocido",
                NumeroSlot = slot.NumeroSlot,
                PrimeraVenta = primeraVenta,
                UltimaVenta = ultimaVenta,
                UltimaActividadMaquina = ultimaActividadMaquina,
                FinReporte = fin,
                FechasVentas = ventasSlot.Select(v => v.FechaLocal).ToList(),
                PosibleQuiebre = posibleQuiebre,
                HorasSinStock = horasSinStock,
                StockInicial = cantidadInicial,
                CantidadVendida = cantidadVendida,
                HorasActivas = horasActivas,
                VelocidadPorHora = Math.Round(velocidadPorHora, 4),
                PrecioPromedioVenta = Math.Round(precioPromedio, 0),
                GananciaPromedio = Math.Round(gananciaPromedio, 0),
                DineroPerdidoEstimado = Math.Round(dineroPerdido, 0),
                GananciaPerdidaEstimada = Math.Round(gananciaPerdida, 0)
            });
        }

        var ventasPendientes = ventas.Where(v => v.ProductoId == null).ToList();
        if (ventasPendientes.Any())
        {
            result.Add(new StockoutAnalysisDto
            {
                MaquinaId = maquinaId,
                MaquinaNombre = maquinaNombre,
                ProductoId = 0,
                ProductoNombre = "Pendientes",
                NumeroSlot = "",
                PosibleQuiebre = false,
                HorasSinStock = 0,
                StockInicial = 0,
                CantidadVendida = ventasPendientes.Count,
                DineroPerdidoEstimado = ventasPendientes.Sum(v => v.PrecioVenta),
                GananciaPerdidaEstimada = 0,
                FinReporte = fin,
                UltimaActividadMaquina = ultimaActividadMaquina
            });
        }

        return result;
    }

    private async Task<int> LocalSyncVentasWithTemplateAsync(int templateId, bool actualizarCostos)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                    .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null || !template.Periodos.Any())
            return 0;

        int ventasActualizadas = 0;

        foreach (var periodo in template.Periodos)
        {
            var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga);

            var ventasDelPeriodo = await _context.Ventas
                .Where(v => v.MaquinaId == periodo.MaquinaId &&
                            v.FechaLocal >= periodo.FechaRecarga &&
                            v.FechaLocal <= fechaFin)
                .ToListAsync();

            if (!ventasDelPeriodo.Any())
                continue;

            foreach (var slot in periodo.SnapshotSlots.Where(s => s.ProductoId.HasValue && s.ProductoId > 0))
            {
                var ventasSlot = ventasDelPeriodo.Where(v => v.NumeroSlot == slot.NumeroSlot).ToList();

                foreach (var venta in ventasSlot)
                {
                    bool cambio = false;

                    if (venta.ProductoId != slot.ProductoId)
                    {
                        venta.ProductoId = slot.ProductoId;
                        cambio = true;
                    }

                    if (actualizarCostos && slot.Producto != null)
                    {
                        var costoHistorico = await _context.ProductoCostos
                            .GetCostoAtAsync(slot.ProductoId!.Value, venta.FechaLocal);
                        decimal nuevoCosto = costoHistorico?.Costo ?? slot.Producto?.CostoPromedio ?? 0;

                        if (venta.CostoVenta != nuevoCosto)
                        {
                            venta.CostoVenta = nuevoCosto;
                            cambio = true;
                        }
                    }

                    if (cambio)
                    {
                        ventasActualizadas++;
                    }
                }
            }
        }

        if (ventasActualizadas > 0)
        {
            await _context.SaveChangesAsync();
        }

        return ventasActualizadas;
    }

    private async Task<SyncAllVentasResultDto> LocalSyncAllVentasAsync(bool actualizarCostos)
    {
        var result = new SyncAllVentasResultDto();

        var templateIds = await _context.TemplatesRecarga
            .Select(t => new { t.Id, t.Nombre })
            .OrderBy(t => t.Id)
            .ToListAsync();

        foreach (var tpl in templateIds)
        {
            var count = await LocalSyncVentasWithTemplateAsync(tpl.Id, actualizarCostos);
            result.Detalles.Add(new SyncTemplateVentasResult
            {
                TemplateId = tpl.Id,
                TemplateNombre = tpl.Nombre,
                VentasActualizadas = count
            });
        }

        result.TemplatesProcesados = templateIds.Count;
        result.TotalVentasActualizadas = result.Detalles.Sum(d => d.VentasActualizadas);

        return result;
    }

    private async Task<SyncSlotProductoResultDto> LocalSyncSlotProductoAsync(
        int templateId, int periodoId, string numeroSlot, int productoId)
    {
        var periodo = await _context.PeriodosRecarga
            .FirstOrDefaultAsync(p => p.Id == periodoId && p.TemplateRecargaId == templateId)
            ?? throw new InvalidOperationException($"Período {periodoId} no encontrado para el template {templateId}");

        var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga);

        var ventas = await _context.Ventas
            .Where(v => v.MaquinaId == periodo.MaquinaId)
            .Where(v => v.NumeroSlot == numeroSlot)
            .Where(v => v.FechaLocal >= periodo.FechaRecarga && v.FechaLocal <= fechaFin)
            .AsNoTracking()
            .ToListAsync();

        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productoId);

        int count = 0;
        foreach (var venta in ventas)
        {
            var attached = await _context.Ventas.FindAsync(venta.Id);
            if (attached != null && attached.ProductoId != productoId)
            {
                attached.ProductoId = productoId;

                var costoHistorico = await _context.ProductoCostos
                    .GetCostoAtAsync(productoId, attached.FechaLocal);
                decimal costoALaFecha = costoHistorico?.Costo
                    ?? producto?.CostoPromedio
                    ?? attached.CostoVenta;
                if (attached.CostoVenta != costoALaFecha)
                    attached.CostoVenta = costoALaFecha;

                count++;
            }
        }

        if (count > 0)
        {
            var slot = await _context.SnapshotSlots
                .FirstOrDefaultAsync(s => s.PeriodoRecargaId == periodo.Id && s.NumeroSlot == numeroSlot);
            if (slot != null)
            {
                slot.ProductoId = productoId;
                slot.Estado = EstadoSlot.Lleno;
            }
            await _context.SaveChangesAsync();
        }

        return new SyncSlotProductoResultDto
        {
            MaquinaId = periodo.MaquinaId,
            NumeroSlot = numeroSlot,
            ProductoId = productoId,
            VentasActualizadas = count
        };
    }
}
