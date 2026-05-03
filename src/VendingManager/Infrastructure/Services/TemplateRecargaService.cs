using Microsoft.EntityFrameworkCore;
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

    public TemplateRecargaService(ApplicationDbContext context)
    {
        _context = context;
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

        return templates.Select(t => MapToDto(t)).ToList();
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
        var template = new TemplateRecarga
        {
            Nombre = dto.Nombre,
            Descripcion = dto.Descripcion,
            FechaCreacion = DateTime.Now,
            Periodos = dto.Periodos.Select(p => new PeriodoRecarga
            {
                MaquinaId = p.MaquinaId,
                FechaInicio = p.FechaInicio,
                FechaFin = p.FechaFin,
                SnapshotSlots = p.SnapshotSlots.Select(s => new SnapshotSlot
                {
                    NumeroSlot = s.NumeroSlot,
                    ProductoId = s.ProductoId,
                    CantidadInicial = s.CantidadInicial,
                    CapacidadSlot = s.CapacidadSlot
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

        // Actualizar propiedades básicas
        template.Nombre = dto.Nombre;
        template.Descripcion = dto.Descripcion;

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
            FechaInicio = p.FechaInicio,
            FechaFin = p.FechaFin,
            SnapshotSlots = p.SnapshotSlots.Select(s => new SnapshotSlot
            {
                NumeroSlot = s.NumeroSlot,
                ProductoId = s.ProductoId,
                CantidadInicial = s.CantidadInicial,
                CapacidadSlot = s.CapacidadSlot
            }).ToList()
        }).ToList();

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
    /// Obtiene la configuración actual de slots para una máquina (para crear snapshot)
    /// </summary>
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
                CapacidadSlot = c.CapacidadMaxima
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
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null || !template.Periodos.Any())
            return new List<StockoutAnalysisDto>();

        var result = new List<StockoutAnalysisDto>();

        // Procesar cada período (cada máquina con su rango específico)
        foreach (var periodo in template.Periodos)
        {
            // Convertir snapshots a diccionario ProductoId -> CantidadInicial
            var snapshotPorProducto = periodo.SnapshotSlots
                .Where(s => s.ProductoId.HasValue && s.ProductoId > 0)
                .GroupBy(s => s.ProductoId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.CantidadInicial));

            var analisisMaquina = await AnalizarMaquinaEnPeriodo(
                periodo.MaquinaId,
                periodo.Maquina?.Nombre ?? "Desconocida",
                periodo.FechaInicio,
                periodo.FechaFin,
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

                    horasSinStock = (fin - fechaAgotamiento).TotalHours;
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

        // Si actualizamos costos, cargamos el historial de compras de los productos involucrados
        var historicoCompras = new List<HistoricoCostoDto>();
        if (actualizarCostos)
        {
            var productIds = template.Periodos
                .SelectMany(p => p.SnapshotSlots)
                .Where(s => s.ProductoId.HasValue && s.ProductoId > 0)
                .Select(s => s.ProductoId!.Value)
                .Distinct()
                .ToList();

            if (productIds.Any())
            {
                var rawHistorico = await _context.DetallesCompra
                    .Join(_context.Compras, d => d.CompraId, c => c.Id, (detalle, compra) => new HistoricoCostoDto
                    {
                        ProductoId = detalle.ProductoId,
                        CostoUnitario = detalle.CostoUnitario,
                        FechaCompra = compra.FechaCompra
                    })
                    .Where(x => x.ProductoId.HasValue && productIds.Contains(x.ProductoId.Value))
                    .OrderBy(x => x.FechaCompra)
                    .ToListAsync();
                    
                historicoCompras.AddRange(rawHistorico);
            }
        }

        foreach (var periodo in template.Periodos)
        {
            var ventasDelPeriodo = await _context.Ventas
                .Where(v => v.MaquinaId == periodo.MaquinaId &&
                            v.FechaLocal >= periodo.FechaInicio &&
                            v.FechaLocal <= periodo.FechaFin)
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
                        decimal nuevoCosto = slot.Producto.CostoPromedio; // fallback al costo actual

                        var comprasProducto = historicoCompras.Where(h => h.ProductoId == slot.ProductoId).ToList();
                        if (comprasProducto.Any())
                        {
                            // Buscar la compra más reciente previa o exacta a la fecha de venta
                            var compraAnterior = comprasProducto.LastOrDefault(h => h.FechaCompra <= venta.FechaLocal);
                            if (compraAnterior != null)
                            {
                                nuevoCosto = compraAnterior.CostoUnitario;
                            }
                            else
                            {
                                // Si no hay previa, usar la más antigua registrada
                                nuevoCosto = comprasProducto.First().CostoUnitario;
                            }
                        }

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

    public async Task<SyncSlotProductoResultDto> SyncSlotProductoAsync(int templateId, int periodoId, string numeroSlot, int productoId)
    {
        var periodo = await _context.PeriodosRecarga
            .FirstOrDefaultAsync(p => p.Id == periodoId && p.TemplateRecargaId == templateId)
            ?? throw new InvalidOperationException($"Período {periodoId} no encontrado para el template {templateId}");

        var ventas = await _context.Ventas
            .Where(v => v.MaquinaId == periodo.MaquinaId)
            .Where(v => v.NumeroSlot == numeroSlot)
            .Where(v => v.FechaLocal >= periodo.FechaInicio && v.FechaLocal <= periodo.FechaFin)
            .ToListAsync();

        int count = 0;
        foreach (var venta in ventas)
        {
            if (venta.ProductoId != productoId)
            {
                venta.ProductoId = productoId;
                count++;
            }
        }

        if (count > 0) await _context.SaveChangesAsync();

        return new SyncSlotProductoResultDto
        {
            MaquinaId = periodo.MaquinaId,
            NumeroSlot = numeroSlot,
            ProductoId = productoId,
            VentasActualizadas = count
        };
    }

    private class HistoricoCostoDto 
    {
        public int? ProductoId { get; set; }
        public decimal CostoUnitario { get; set; }
        public DateTime FechaCompra { get; set; }
    }

    private static TemplateRecargaDto MapToDto(TemplateRecarga t)
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
                FechaInicio = p.FechaInicio,
                FechaFin = p.FechaFin,
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
}
