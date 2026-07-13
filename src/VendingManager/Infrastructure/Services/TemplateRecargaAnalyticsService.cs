using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.Enums;
using VendingManager.Shared.Helpers;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Implements analytics and sync operations for TemplateRecarga.
/// Extracts stockout analysis and venta sync logic from TemplateRecargaService.
/// </summary>
public class TemplateRecargaAnalyticsService : ITemplateRecargaAnalyticsService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TemplateRecargaAnalyticsService> _logger;

    public TemplateRecargaAnalyticsService(
        ApplicationDbContext context,
        ILogger<TemplateRecargaAnalyticsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<StockoutAnalysisDto>> AnalyzarPorTemplateAsync(
        int templateId,
        double umbralHorasSilencio = 24)
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

        // Build cross-template lookup to resolve FechaFin — filtered to only relevant machines
        var maquinaIds = template.Periodos.Select(p => p.MaquinaId).ToHashSet();
        var crossTemplateLookup = await BuildCrossTemplateLookupAsync(maquinaIds);

        // Pre-load ALL ventas across ALL machines in this template to compute
        // per-machine product velocity (aggregated by slot, not per-slot)
        // with operating-hours filter.
        // Key: (MaquinaId, ProductoId) — velocity varies by machine location,
        // not just by product. Same product in an office vs. a hospital sells differently.
        var todasLasVentas = await _context.Ventas
            .Include(v => v.Producto)
            .Where(v => maquinaIds.Contains(v.MaquinaId))
            .Where(v => v.IdOrdenMaquina != "TB-EXTRA" && v.IdOrdenMaquina != "TB-SIN-VENTA")
            .ToListAsync();

        var velocidadPorProducto = todasLasVentas
            .Where(v => v.ProductoId.HasValue && v.ProductoId > 0)
            .GroupBy(v => (v.MaquinaId, ProductoId: v.ProductoId!.Value))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var ventasEnHorario = g.Where(v => v.FechaLocal.Hour >= 8 && v.FechaLocal.Hour < 22).ToList();
                    if (ventasEnHorario.Count == 0) return 0m;

                    var primera = ventasEnHorario.Min(v => v.FechaLocal);
                    var ultima = ventasEnHorario.Max(v => v.FechaLocal);
                    var horasActivas = HorasEnRangoOperativo(primera, ultima);
                    if (horasActivas < 1) horasActivas = 1;

                    return ventasEnHorario.Count / (decimal)horasActivas;
                });

        foreach (var periodo in template.Periodos)
        {
            var snapshotPorProducto = periodo.SnapshotSlots
                .Where(s => s.ProductoId.HasValue && s.ProductoId > 0)
                .GroupBy(s => s.ProductoId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.CantidadInicial));

            var fechaFin = await GetEndDateForPeriodoAsync(
                periodo.MaquinaId, periodo.FechaRecarga, crossTemplateLookup);

            var ventasMaquina = todasLasVentas
                .Where(v => v.MaquinaId == periodo.MaquinaId
                         && v.FechaLocal >= periodo.FechaRecarga
                         && v.FechaLocal <= fechaFin)
                .ToList();

            var analisisMaquina = AnalizarMaquinaEnPeriodo(
                periodo.MaquinaId,
                periodo.Maquina?.Nombre ?? "Desconocida",
                periodo.FechaRecarga,
                fechaFin,
                umbralHorasSilencio,
                periodo.SnapshotSlots.ToList(),
                snapshotPorProducto,
                ventasMaquina,
                velocidadPorProducto);

            result.AddRange(analisisMaquina);
        }

        return result
            .OrderByDescending(x => x.DineroPerdidoEstimado)
            .ThenByDescending(x => x.PosibleQuiebre)
            .ThenByDescending(x => x.HorasSinStock)
            .ToList();
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
                var ventasSlot = ventasDelPeriodo
                    .Where(v => v.NumeroSlot == slot.NumeroSlot)
                    .ToList();

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

            // Sincronizar slots Vacíos/Pendientes: limpiar ProductoId en ventas del período.
            // Si un slot quedó sin producto (Pendiente o Vacío), las ventas que tenía
            // asignadas deben perder el ProductoId y CostoVenta.
            var slotsSinProducto = periodo.SnapshotSlots
                .Where(s => s.Estado is EstadoSlot.Vacio or EstadoSlot.Pendiente)
                .ToList();

            foreach (var slot in slotsSinProducto)
            {
                foreach (var venta in ventasDelPeriodo.Where(v => v.NumeroSlot == slot.NumeroSlot))
                {
                    if (venta.ProductoId.HasValue)
                    {
                        venta.ProductoId = null;
                        venta.CostoVenta = 0;
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
            "[SyncAllVentas] Completed: {Templates} templates, {Total} ventas updated",
            result.TemplatesProcesados, result.TotalVentasActualizadas);

        return result;
    }

    public async Task<SyncSlotProductoResultDto> SyncSlotProductoAsync(
        int templateId, int periodoId, string numeroSlot, int productoId)
    {
        var periodo = await _context.PeriodosRecarga
            .FirstOrDefaultAsync(p => p.Id == periodoId && p.TemplateRecargaId == templateId)
            ?? throw new InvalidOperationException($"Período {periodoId} no encontrado para el template {templateId}");

        var fechaFin = await GetEndDateForPeriodoAsync(periodo.MaquinaId, periodo.FechaRecarga);

        // Use AsNoTracking for read-only query to avoid double-tracking conflicts
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
            if (venta.ProductoId != productoId)
            {
                // Attach with no-change check before modifying
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

    /// <summary>
    /// Stub: full implementation belongs in PR 2 (lazy timeline endpoint).
    /// Returns null to indicate the feature is not yet wired.
    /// </summary>
    public async Task<SlotTimelineDto?> GetSlotTimelineAsync(int templateId, int maquinaId, string numeroSlot)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null || !template.Periodos.Any(p => p.MaquinaId == maquinaId))
            return null;

        var periodo = template.Periodos.First(p => p.MaquinaId == maquinaId);
        var fechaFin = await GetEndDateForPeriodoAsync(maquinaId, periodo.FechaRecarga);

        var fechasVentas = await _context.Ventas
            .Where(v => v.MaquinaId == maquinaId)
            .Where(v => v.NumeroSlot == numeroSlot)
            .Where(v => v.FechaLocal >= periodo.FechaRecarga && v.FechaLocal <= fechaFin)
            .OrderBy(v => v.FechaLocal)
            .Select(v => v.FechaLocal)
            .ToListAsync();

        var productoId = await _context.SnapshotSlots
            .Where(s => s.PeriodoRecargaId == periodo.Id && s.NumeroSlot == numeroSlot)
            .Select(s => (int?)s.ProductoId)
            .FirstOrDefaultAsync();

        return new SlotTimelineDto
        {
            MaquinaId = maquinaId,
            NumeroSlot = numeroSlot,
            ProductoId = productoId,
            FechasVentas = fechasVentas
        };
    }

    /// <summary>
    /// Builds a cross-template lookup: (MaquinaId, FechaRecarga) → FechaFin for O(1) lookup.
    /// When <paramref name="maquinaIds"/> is provided, only loads periods for those machines.
    /// </summary>
    private async Task<Dictionary<(int maquinaId, DateTime fechaRecarga), DateTime>> BuildCrossTemplateLookupAsync(
        HashSet<int>? maquinaIds = null)
    {
        var allTemplates = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .AsNoTracking()
            .ToListAsync();

        var allPeriodos = allTemplates.SelectMany(t => t.Periodos).ToList();

        if (maquinaIds != null)
        {
            allPeriodos = allPeriodos.Where(p => maquinaIds.Contains(p.MaquinaId)).ToList();
        }
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
    /// Counts the number of operating hours (08:00–22:00) between two timestamps.
    /// Used to avoid diluting velocity and loss calculations with overnight dead hours.
    /// </summary>
    private static double HorasEnRangoOperativo(DateTime desde, DateTime hasta)
        => HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

    private async Task<DateTime> GetEndDateForPeriodoAsync(
        int maquinaId,
        DateTime fechaRecarga,
        Dictionary<(int maquinaId, DateTime fechaRecarga), DateTime>? crossTemplateLookup = null)
    {
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

        return nextRecarga ?? (fechaRecarga <= DateTime.Now
            ? (DateTime.Now < fechaRecarga.AddDays(90) ? DateTime.Now : fechaRecarga.AddDays(90))
            : fechaRecarga.AddDays(90));
    }

    private List<StockoutAnalysisDto> AnalizarMaquinaEnPeriodo(
        int maquinaId,
        string maquinaNombre,
        DateTime inicio,
        DateTime fin,
        double umbralHoras,
        List<SnapshotSlot> snapshotSlots,
        Dictionary<int, int>? snapshotPorProducto,
        List<Venta> ventasMaquina,
        Dictionary<(int maquinaId, int productoId), decimal>? velocidadPorProducto = null)
    {
        var ventas = ventasMaquina
            .Where(v => v.FechaLocal >= inicio && v.FechaLocal <= fin)
            .ToList();

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

            // Resolve effective velocity: product-level aggregate when available,
            // fall back to per-slot velocity otherwise.
            decimal velocidadEfectiva = velocidadPorHora;
            if (velocidadPorProducto != null
                && productoId > 0
                && velocidadPorProducto.TryGetValue((maquinaId, productoId), out var vp)
                && vp > 0)
            {
                velocidadEfectiva = vp;
            }

            decimal dineroPerdido = 0;
            decimal gananciaPerdida = 0;

            // Use product-level velocity when available — aggregates across all machines
            // and filters to operating hours (08:00–22:00) for a more robust estimate.
            // horasSinStock is also adjusted to operating hours for consistency.
            if (posibleQuiebre && horasSinStock > 0 && precioPromedio > 0)
            {
                if (velocidadEfectiva != velocidadPorHora && velocidadPorProducto != null
                    && productoId > 0
                    && velocidadPorProducto.TryGetValue((maquinaId, productoId), out var vpLoss)
                    && vpLoss > 0)
                {
                    double horasSinStockOperativas = HorasEnRangoOperativo(fechaAgotamiento, fin);
                    if (horasSinStockOperativas < 0) horasSinStockOperativas = 0;

                    dineroPerdido = vpLoss * (decimal)horasSinStockOperativas * precioPromedio;
                    gananciaPerdida = vpLoss * (decimal)horasSinStockOperativas * gananciaPromedio;
                }
                else
                {
                    // Fallback: per-slot velocity with raw clock hours
                    dineroPerdido = velocidadPorHora * (decimal)horasSinStock * precioPromedio;
                    gananciaPerdida = velocidadPorHora * (decimal)horasSinStock * gananciaPromedio;
                }
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
                VelocidadPorHora = Math.Round(velocidadEfectiva, 4),
                PrecioPromedioVenta = Math.Round(precioPromedio, 0),
                GananciaPromedio = Math.Round(gananciaPromedio, 0),
                DineroPerdidoEstimado = Math.Round(dineroPerdido, 0),
                GananciaPerdidaEstimada = Math.Round(gananciaPerdida, 0),
            });
        }

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
            });
        }

        return result;
    }
}