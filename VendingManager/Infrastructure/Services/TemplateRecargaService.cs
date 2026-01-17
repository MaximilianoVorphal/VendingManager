using Microsoft.EntityFrameworkCore;
using VendingManager.Core.DTOs;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

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
        return await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .ThenInclude(p => p.Maquina)
            .OrderByDescending(t => t.FechaCreacion)
            .Select(t => MapToDto(t))
            .ToListAsync();
    }

    public async Task<TemplateRecargaDto?> GetByIdAsync(int id)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .ThenInclude(p => p.Maquina)
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
                FechaFin = p.FechaFin
            }).ToList()
        };

        _context.TemplatesRecarga.Add(template);
        await _context.SaveChangesAsync();

        // Recargar con navegación
        await _context.Entry(template)
            .Collection(t => t.Periodos)
            .Query()
            .Include(p => p.Maquina)
            .LoadAsync();

        return MapToDto(template);
    }

    public async Task<TemplateRecargaDto> UpdateAsync(int id, UpdateTemplateRecargaDto dto)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            throw new InvalidOperationException($"Template con ID {id} no encontrado");

        // Actualizar propiedades básicas
        template.Nombre = dto.Nombre;
        template.Descripcion = dto.Descripcion;

        // Eliminar períodos anteriores
        _context.PeriodosRecarga.RemoveRange(template.Periodos);

        // Agregar nuevos períodos
        template.Periodos = dto.Periodos.Select(p => new PeriodoRecarga
        {
            TemplateRecargaId = template.Id,
            MaquinaId = p.MaquinaId,
            FechaInicio = p.FechaInicio,
            FechaFin = p.FechaFin
        }).ToList();

        await _context.SaveChangesAsync();

        // Recargar con navegación
        await _context.Entry(template)
            .Collection(t => t.Periodos)
            .Query()
            .Include(p => p.Maquina)
            .LoadAsync();

        return MapToDto(template);
    }

    public async Task DeleteAsync(int id)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template != null)
        {
            _context.PeriodosRecarga.RemoveRange(template.Periodos);
            _context.TemplatesRecarga.Remove(template);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Análisis de stockout usando los períodos específicos del template.
    /// Cada máquina se analiza con su propio rango de fecha/hora.
    /// </summary>
    public async Task<List<StockoutAnalysisDto>> AnalyzarPorTemplateAsync(int templateId, double umbralHorasSilencio = 24)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
            .ThenInclude(p => p.Maquina)
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null || !template.Periodos.Any())
            return new List<StockoutAnalysisDto>();

        var result = new List<StockoutAnalysisDto>();

        // Procesar cada período (cada máquina con su rango específico)
        foreach (var periodo in template.Periodos)
        {
            var analisisMaquina = await AnalizarMaquinaEnPeriodo(
                periodo.MaquinaId,
                periodo.Maquina?.Nombre ?? "Desconocida",
                periodo.FechaInicio,
                periodo.FechaFin,
                umbralHorasSilencio);

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
    /// Análisis de stockout para una máquina específica en un período determinado
    /// </summary>
    private async Task<List<StockoutAnalysisDto>> AnalizarMaquinaEnPeriodo(
        int maquinaId, string maquinaNombre, DateTime inicio, DateTime fin, double umbralHoras)
    {
        // Obtener ventas de esta máquina en el período (excluyendo fantasmas)
        var ventas = await _context.Ventas
            .Include(v => v.Producto)
            .Where(v => v.MaquinaId == maquinaId)
            .Where(v => v.FechaLocal >= inicio && v.FechaLocal <= fin)
            .Where(v => v.IdOrdenMaquina != "TB-EXTRA" && v.IdOrdenMaquina != "TB-SIN-VENTA")
            .ToListAsync();

        if (!ventas.Any())
            return new List<StockoutAnalysisDto>();

        // Última actividad de la máquina en el período
        var ultimaActividadMaquina = ventas.Max(v => v.FechaLocal);

        // Agrupar por producto
        var grupos = ventas
            .Where(v => v.ProductoId.HasValue && v.ProductoId > 0)
            .GroupBy(v => v.ProductoId)
            .ToList();

        var result = new List<StockoutAnalysisDto>();

        foreach (var grupo in grupos)
        {
            var productoId = grupo.Key!.Value;
            var ventasGrupo = grupo.ToList();

            var primeraVenta = ventasGrupo.Min(v => v.FechaLocal);
            var ultimaVenta = ventasGrupo.Max(v => v.FechaLocal);
            var cantidad = ventasGrupo.Count;

            var precioPromedio = ventasGrupo.Average(v => v.PrecioVenta);
            var costoPromedio = ventasGrupo.Average(v =>
                v.CostoVenta > 0 ? v.CostoVenta : (v.Producto?.CostoPromedio ?? 0));
            var gananciaPromedio = precioPromedio - costoPromedio;

            // Detección de silencio
            var horasDiferencia = (ultimaActividadMaquina - ultimaVenta).TotalHours;
            var posibleQuiebre = horasDiferencia > umbralHoras;

            // Horas sin stock hasta el fin del período
            var horasSinStock = (fin - ultimaVenta).TotalHours;
            if (horasSinStock < 0) horasSinStock = 0;

            // Velocidad real
            var horasActivas = (ultimaVenta - primeraVenta).TotalHours;
            if (horasActivas < 1) horasActivas = 1;
            var velocidadPorHora = cantidad / (decimal)horasActivas;

            // Costo de oportunidad
            decimal dineroPerdido = 0;
            decimal gananciaPerdida = 0;

            if (posibleQuiebre && horasSinStock > 0)
            {
                dineroPerdido = velocidadPorHora * (decimal)horasSinStock * precioPromedio;
                gananciaPerdida = velocidadPorHora * (decimal)horasSinStock * gananciaPromedio;
            }

            var producto = ventasGrupo.First().Producto;
            var slots = ventasGrupo.Select(v => v.NumeroSlot).Distinct().ToList();

            result.Add(new StockoutAnalysisDto
            {
                MaquinaId = maquinaId,
                MaquinaNombre = maquinaNombre,
                ProductoId = productoId,
                ProductoNombre = producto?.Nombre ?? "Desconocido",
                NumeroSlot = string.Join(", ", slots),

                PrimeraVenta = primeraVenta,
                UltimaVenta = ultimaVenta,
                UltimaActividadMaquina = ultimaActividadMaquina,
                FinReporte = fin,

                PosibleQuiebre = posibleQuiebre,
                HorasSinStock = horasSinStock,

                CantidadVendida = cantidad,
                HorasActivas = horasActivas,
                VelocidadPorHora = Math.Round(velocidadPorHora, 4),

                PrecioPromedioVenta = Math.Round(precioPromedio, 0),
                GananciaPromedio = Math.Round(gananciaPromedio, 0),
                DineroPerdidoEstimado = Math.Round(dineroPerdido, 0),
                GananciaPerdidaEstimada = Math.Round(gananciaPerdida, 0)
            });
        }

        return result;
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
                FechaFin = p.FechaFin
            }).ToList()
        };
    }
}
