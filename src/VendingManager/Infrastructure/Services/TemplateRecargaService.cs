using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Servicio para gestionar templates de recarga y análisis de stockout por período
/// </summary>
public class TemplateRecargaService : ITemplateRecargaService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TemplateRecargaService> _logger;

    public TemplateRecargaService(ApplicationDbContext context, ILogger<TemplateRecargaService> logger)
    {
        _context = context;
        _logger = logger;
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

        return templates
            .Select(t => MapToDto(t, crossTemplateChain))
            .OrderByDescending(t => t.FechaRecargaMin)
            .ToList();
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

        // Eliminar snapshots de períodos anteriores
        foreach (var periodo in template.Periodos)
        {
            _context.SnapshotSlots.RemoveRange(periodo.SnapshotSlots);
        }
        // Eliminar períodos anteriores
        _context.PeriodosRecarga.RemoveRange(template.Periodos);

        // Agregar nuevos períodos con snapshots
        template.Periodos = dto.Periodos.Select(p => new PeriodoRecarga
        {
            TemplateRecargaId = template.Id,
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
                $"Ya existe un período con FechaRecarga {fechaRecarga:dd/MM/yyyy HH:mm} "
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
        return await _context.ConfiguracionSlots
            .Include(c => c.Producto)
            .Where(c => c.MaquinaId == maquinaId)
            .OrderBy(c => c.NumeroSlot)
            .Select(c => new SnapshotSlotDto
            {
                NumeroSlot = c.NumeroSlot,
                ProductoId = c.ProductoId,
                ProductoNombre = c.Producto != null ? c.Producto.Nombre : "",
                CantidadInicial = c.StockActual,
                CapacidadSlot = c.CapacidadMaxima,
                Estado = c.ProductoId == null ? EstadoSlot.Pendiente : EstadoSlot.Lleno
            })
            .ToListAsync();
    }

    /// <summary>
    /// Análisis de stockout usando los períodos específicos del template.
    /// Cada máquina se analiza con su propio rango de fecha/hora.
    /// Si el período tiene snapshots, usa cálculo exacto de agotamiento.
    /// </summary>
    public async Task<List<StockoutAnalysisDto>> AnalyzarPorTemplateAsync(int templateId, double umbralHorasSilencio = 24)
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

        // Build cross-template lookup BEFORE the period loop so GetEndDateForPeriodoAsync
        // can resolve end dates across ALL templates (not just the current one)
        var crossTemplateLookup = await BuildCrossTemplateLookupAsync();

        // Procesar cada período (cada máquina con su rango específico)
        foreach (var periodo in template.Periodos)
        {
            // Convertir snapshots a diccionario ProductoId -> CantidadInicial
            var snapshotPorProducto = periodo.SnapshotSlots
                .Where(s => s.ProductoId.HasValue && s.ProductoId > 0)
                .GroupBy(s => s.ProductoId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.CantidadInicial));

            // Get end date from cross-template lookup (avoids DB query for next recarga)
            var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga, crossTemplateLookup);

            var analisisMaquina = await AnalizarMaquinaEnPeriodo(
                periodo.MaquinaId,
                periodo.Maquina?.Nombre ?? "Desconocida",
                periodo.FechaRecarga,
                fechaFin,
                umbralHorasSilencio,
                periodo.SnapshotSlots.ToList(), // Pasar la lista completa de slots
                snapshotPorProducto);

            result.AddRange(analisisMaquina);
        }

        // Ordenar por dinero perdido
        return result
            .OrderByDescending(x => x.DineroPerdidoEstimado)
            .ThenByDescending(x => x.PosibleQuiebre)
            .ThenByDescending(x => x.HorasSinStock)
            .ToList();
    }

    /// <summary>
    /// Builds a cross-template lookup: (MaquinaId, FechaRecarga) → FechaFin for O(1) lookup.
    /// Uses all periods from ALL templates to find the next recarga across the full chain.
    /// </summary>
    private async Task<Dictionary<(int maquinaId, DateTime fechaRecarga), DateTime>> BuildCrossTemplateLookupAsync()
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

    /// <summary>
    /// Análisis de stockout para una máquina específica en un período determinado.
    /// Si hay snapshot para el producto, usa cálculo exacto. Sino, usa inferencia por silencio.
    /// </summary>
    /// Análisis de stockout para una máquina específica en un período determinado.
    /// Si hay snapshot para el producto, usa cálculo exacto. Sino, usa inferencia por silencio.
    /// </summary>
    private async Task<List<StockoutAnalysisDto>> AnalizarMaquinaEnPeriodo(
        int maquinaId, string maquinaNombre, DateTime inicio, DateTime fin, double umbralHoras,
        List<SnapshotSlot> snapshotSlots,
        Dictionary<int, int>? snapshotPorProducto) // Mantener por compatibilidad o eliminar si no se usa
    {
        // 1. Obtener todas las ventas del periodo
        // IMPORTANTE: Traer ventas aunque no tengan coincidencia directa inicial para analisis global
        var ventas = await _context.Ventas
            .Include(v => v.Producto)
            .Where(v => v.MaquinaId == maquinaId)
            .Where(v => v.FechaLocal >= inicio && v.FechaLocal <= fin)
            .Where(v => v.IdOrdenMaquina != "TB-EXTRA" && v.IdOrdenMaquina != "TB-SIN-VENTA")
            .ToListAsync();

        var result = new List<StockoutAnalysisDto>();
        
        // Última actividad global de la máquina (para inferencia de silencio)
        var ultimaActividadMaquina = ventas.Any() ? ventas.Max(v => v.FechaLocal) : inicio;

        // 2. Iterar sobre la CONFIGURACIÓN DE SLOTS (Snapshot)
        // Esto asegura que mostramos TODOS los slots, incluso los que no vendieron
        foreach (var slot in snapshotSlots.OrderBy(s => int.TryParse(s.NumeroSlot, out int n) ? n : 999))
        {
            // Filtrar ventas para ESTE slot específico
            // Normalizamos comparación de strings por si acaso ("01" vs "1")
            // Asumimos coincidencia exacta de string por ahora, lo cual es estándar en el sistema
            var ventasSlot = ventas
                .Where(v => v.NumeroSlot == slot.NumeroSlot)
                .OrderBy(v => v.FechaLocal)
                .ToList();

            // Datos básicos
            var productoId = slot.ProductoId ?? 0;
            var cantidadInicial = slot.CantidadInicial;
            var cantidadVendida = ventasSlot.Count;
            
            DateTime? primeraVenta = ventasSlot.Any() ? ventasSlot.First().FechaLocal : null;
            DateTime? ultimaVenta = ventasSlot.Any() ? ventasSlot.Last().FechaLocal : null;
            
            // Cálculos financieros
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
                // Si no hay ventas, usar datos del producto del snapshot si disponible
                // (No tenemos precio venta en snapshot, asumimos 0 o buscar ultimo historial? 0 por ahora)
                precioPromedio = 0; 
                // Podríamos buscar precio actual en config slot pero snapshot es historia.
            }

            // ===== LÓGICA DE QUIEBRE (PER SLOT) =====
            bool posibleQuiebre = false;
            double horasSinStock = 0;
            DateTime fechaAgotamiento = fin;

            if (cantidadInicial > 0)
            {
                if (cantidadVendida >= cantidadInicial)
                {
                    // Quiebre Confirmado: Vendió todo lo que tenía
                    posibleQuiebre = true;
                    // Fecha exacta cuando vendió la última unidad disponible
                    // (la venta número 'cantidadInicial')
                    if (cantidadInicial <= ventasSlot.Count)
                    {
                        fechaAgotamiento = ventasSlot[cantidadInicial - 1].FechaLocal;
                    }
                    else
                    {
                        // Fallback raro (vendió más de lo que tenía? rellenaron sin avisar?)
                        fechaAgotamiento = ultimaVenta ?? fin;
                    }

                    horasSinStock = Math.Min((fin - fechaAgotamiento).TotalHours, (fin - inicio).TotalHours);
                }
                else
                {
                    // No se agotó
                    posibleQuiebre = false;
                    horasSinStock = 0;
                }
            }
            else
            {
                // Slot empezó vacío o sin producto
                posibleQuiebre = false; 
            }
            
            if (horasSinStock < 0) horasSinStock = 0;


            // Velocidad y Costo Oportunidad
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
                ProductoNombre = slot.Producto?.Nombre ?? "Desconocido", // Usar nombre del snapshot
                NumeroSlot = slot.NumeroSlot, // Slot Individual!

                PrimeraVenta = primeraVenta,
                UltimaVenta = ultimaVenta,
                UltimaActividadMaquina = ultimaActividadMaquina,
                FinReporte = fin,

                PosibleQuiebre = posibleQuiebre,
                HorasSinStock = horasSinStock,

                StockInicial = cantidadInicial,
                CantidadVendida = cantidadVendida,
                HorasActivas = horasActivas,
                VelocidadPorHora = Math.Round(velocidadPorHora, 4),

                PrecioPromedioVenta = Math.Round(precioPromedio, 0),
                GananciaPromedio = Math.Round(gananciaPromedio, 0),
                DineroPerdidoEstimado = Math.Round(dineroPerdido, 0),
                GananciaPerdidaEstimada = Math.Round(gananciaPerdida, 0),
                FechasVentas = ventasSlot.Select(v => v.FechaLocal).OrderBy(d => d).ToList()
            });
        }

        // Pendientes grouping: aggregate revenue from null-product sales
        var ventasPendientes = ventas.Where(v => v.ProductoId == null).ToList();
        if (ventasPendientes.Any())
        {
            var precioVentaTotal = ventasPendientes.Sum(v => v.PrecioVenta);
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
                DineroPerdidoEstimado = precioVentaTotal,
                GananciaPerdidaEstimada = 0,
                FinReporte = fin,
                UltimaActividadMaquina = ultimaActividadMaquina,
                FechasVentas = new List<DateTime>()
            });
        }

        return result;
    }

public async Task<int> SyncVentasWithTemplateAsync(int templateId, bool actualizarCostos)
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
                // Encontrar las ventas de ese slot en el periodo
                var ventasSlot = ventasDelPeriodo.Where(v => v.NumeroSlot == slot.NumeroSlot).ToList();

                foreach (var venta in ventasSlot)
                {
                    bool cambio = false;

                    // 1. Verificar si hay que actualizar Producto
                    if (venta.ProductoId != slot.ProductoId)
                    {
                        venta.ProductoId = slot.ProductoId;
                        cambio = true;
                    }

                    // 2. Verificar si hay que actualizar Costo
                    if (actualizarCostos && slot.Producto != null)
                    {
                        var productoId = slot.ProductoId!.Value;
                        var costoHistorico = await _context.ProductoCostos
                            .GetCostoAtAsync(productoId, venta.FechaLocal);
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

    public async Task<SyncAllVentasResultDto> SyncAllVentasAsync(bool actualizarCostos)
    {
        var result = new SyncAllVentasResultDto();

        var templateIds = await _context.TemplatesRecarga
            .Select(t => new { t.Id, t.Nombre })
            .OrderBy(t => t.Id)
            .ToListAsync();

        foreach (var tpl in templateIds)
        {
            var count = await SyncVentasWithTemplateAsync(tpl.Id, actualizarCostos);
            result.Detalles.Add(new SyncTemplateVentasResult
            {
                TemplateId = tpl.Id,
                TemplateNombre = tpl.Nombre,
                VentasActualizadas = count
            });
        }

        result.TemplatesProcesados = templateIds.Count;
        result.TotalVentasActualizadas = result.Detalles.Sum(d => d.VentasActualizadas);

        _logger.LogInformation(
            "[SyncAllVentas] Completado: {Templates} templates procesados, {Total} ventas actualizadas. " +
            "Detalle: {@Detalles}",
            result.TemplatesProcesados, result.TotalVentasActualizadas,
            result.Detalles.Select(d => new { d.TemplateId, d.TemplateNombre, d.VentasActualizadas }));

        return result;
    }

    public async Task<SyncSlotProductoResultDto> SyncSlotProductoAsync(int templateId, int periodoId, string numeroSlot, int productoId)
    {
        var periodo = await _context.PeriodosRecarga
            .FirstOrDefaultAsync(p => p.Id == periodoId && p.TemplateRecargaId == templateId)
            ?? throw new InvalidOperationException($"Período {periodoId} no encontrado para el template {templateId}");

        var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga);

        _logger.LogInformation(
            "[SyncSlotProducto] INICIO — Template={TemplateId}, Periodo={PeriodoId}, " +
            "Maquina={MaquinaId}, Slot={NumeroSlot}, ProductoNuevo={ProductoId}, " +
            "Rango=[{Inicio} → {Fin}]",
            templateId, periodoId, periodo.MaquinaId, numeroSlot, productoId,
            periodo.FechaRecarga, fechaFin);

        var ventas = await _context.Ventas
            .Where(v => v.MaquinaId == periodo.MaquinaId)
            .Where(v => v.NumeroSlot == numeroSlot)
            .Where(v => v.FechaLocal >= periodo.FechaRecarga && v.FechaLocal <= fechaFin)
            .AsNoTracking()
            .ToListAsync();

        _logger.LogInformation(
            "[SyncSlotProducto] Ventas encontradas={Count}. IDs={Ids}",
            ventas.Count,
            ventas.Select(v => v.Id).ToList());

        // Cargar el producto para fallback de CostoPromedio si no hay ProductoCosto histórico
        var producto = await _context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productoId);

        int count = 0;
        foreach (var venta in ventas)
        {
            // Attach para que EF Core trackee esta venta
            _context.Ventas.Attach(venta);

            if (venta.ProductoId != productoId)
            {
                var productoIdAnterior = venta.ProductoId;
                var costoAnterior = venta.CostoVenta;

                venta.ProductoId = productoId;

                // Recalculate CostoVenta from historical cost at sale date
                var costoHistorico = await _context.ProductoCostos
                    .GetCostoAtAsync(productoId, venta.FechaLocal);
                decimal costoALaFecha = costoHistorico?.Costo
                    ?? producto?.CostoPromedio
                    ?? costoAnterior; // Si no hay ni histórico ni CostoPromedio, mantener el costo existente
                if (venta.CostoVenta != costoALaFecha)
                    venta.CostoVenta = costoALaFecha;

                _logger.LogInformation(
                    "[SyncSlotProducto] Venta={VentaId}: Producto {Antes}→{Ahora}, " +
                    "Fecha={FechaLocal}, CostoHistoricoDB={CostoHistoricoDB}, " +
                    "CostoPromedioProducto={CostoPromedio}, " +
                    "CostoVenta {CostoAntes}→{CostoAhora}",
                    venta.Id, productoIdAnterior, productoId,
                    venta.FechaLocal, costoHistorico?.Costo,
                    producto?.CostoPromedio,
                    costoAnterior, venta.CostoVenta);
                count++;
            }
        }

        if (count > 0)
        {
            var slot = await _context.SnapshotSlots
                .FirstOrDefaultAsync(s => s.PeriodoRecargaId == periodo.Id && s.NumeroSlot == numeroSlot);
            if (slot != null)
            {
                _logger.LogInformation(
                    "[SyncSlotProducto] SnapshotSlot={SlotId}: ProductoId {Antes}→{Ahora}, Estado {EstadoAntes}→Lleno",
                    slot.Id, slot.ProductoId, productoId, slot.Estado);
                slot.ProductoId = productoId;
                slot.Estado = EstadoSlot.Lleno;
            }
            await _context.SaveChangesAsync();
            
            // Verify persistence
            var ventaVerificacion = await _context.Ventas
                .AsNoTracking()
                .Where(v => v.MaquinaId == periodo.MaquinaId
                         && v.NumeroSlot == numeroSlot
                         && v.FechaLocal >= periodo.FechaRecarga
                         && v.FechaLocal <= fechaFin)
                .ToListAsync();
            _logger.LogInformation(
                "[SyncSlotProducto] VERIFICACION post-save: {Count} ventas en DB para slot {Slot}. " +
                "Muestra: {@Muestra}",
                ventaVerificacion.Count, numeroSlot,
                ventaVerificacion.Select(v => new { v.Id, v.ProductoId, v.CostoVenta, v.FechaLocal }).ToList());
        }
        else
        {
            _logger.LogWarning(
                "[SyncSlotProducto] SIN CAMBIOS — {Count} ventas encontradas pero " +
                "ninguna requirió actualización (todas ya tienen ProductoId={ProductoId})",
                ventas.Count, productoId);
        }

        return new SyncSlotProductoResultDto
        {
            MaquinaId = periodo.MaquinaId,
            NumeroSlot = numeroSlot,
            ProductoId = productoId,
            VentasActualizadas = count
        };
    }

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
            Periodos = t.Periodos.Select(p => new PeriodoRecargaDto
            {
                Id = p.Id,
                MaquinaId = p.MaquinaId,
                MaquinaNombre = p.Maquina?.Nombre ?? "Desconocida",
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
}
