using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Implements the lifecycle state machine for TemplateRecarga.
/// State machine: Pendiente (0) ↔ Terminado (1).
/// </summary>
public class TemplateRecargaLifecycleService : ITemplateRecargaLifecycleService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TemplateRecargaLifecycleService> _logger;

    public TemplateRecargaLifecycleService(
        ApplicationDbContext context,
        ILogger<TemplateRecargaLifecycleService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TemplateRecargaDto> TerminarAsync(int templateId)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template con ID {templateId} no encontrado");

        if (template.Estado != EstadoTemplate.Pendiente)
        {
            throw new InvalidOperationException(
                $"No se puede terminar: el template está en estado {template.Estado}. " +
                $"Solo templates en estado Pendiente pueden terminarse.");
        }

        template.Estado = EstadoTemplate.Terminado;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "[Terminar] Template {TemplateId} transitioned to Terminado",
            templateId);

        return MapToDto(template);
    }

    public async Task<TemplateRecargaDto> ReabrirAsync(int templateId)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template con ID {templateId} no encontrado");

        template.Estado = EstadoTemplate.Pendiente;

        // Clear snapshot slots on reopen
        foreach (var periodo in template.Periodos)
        {
            _context.SnapshotSlots.RemoveRange(periodo.SnapshotSlots);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "[Reabrir] Template {TemplateId} reset to Pendiente",
            templateId);

        return MapToDto(template);
    }

    public async Task<List<SnapshotSlotDto>> GetLatestTerminadoTemplateSlotsAsync(int maquinaId)
    {
        // Find the latest Terminado template that has a periodo for this machine
        var latestTerminadoTemplate = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                    .ThenInclude(s => s.Producto)
            .Where(t => t.Estado == EstadoTemplate.Terminado)
            .Where(t => t.Periodos.Any(p => p.MaquinaId == maquinaId))
            .OrderByDescending(t => t.FechaCreacion)
            .FirstOrDefaultAsync();

        if (latestTerminadoTemplate == null)
        {
            return new List<SnapshotSlotDto>();
        }

        var periodoForMaquina = latestTerminadoTemplate.Periodos
            .FirstOrDefault(p => p.MaquinaId == maquinaId);

        if (periodoForMaquina == null)
        {
            return new List<SnapshotSlotDto>();
        }

        return periodoForMaquina.SnapshotSlots
            .Select(s => new SnapshotSlotDto
            {
                Id = s.Id,
                NumeroSlot = s.NumeroSlot,
                ProductoId = s.ProductoId,
                ProductoNombre = s.Producto?.Nombre ?? "",
                CantidadInicial = s.CantidadInicial,
                CapacidadSlot = s.CapacidadSlot,
                Estado = s.Estado
            })
            .ToList();
    }

    public async Task<int> SyncSlotsToConfiguracionAsync(int templateId)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template con ID {templateId} no encontrado");

        return await SyncSlotsToConfiguracionInternalAsync(template);
    }

    /// <summary>
    /// Internal sync: upserts ConfiguracionSlots from SnapshotSlots for all periods.
    /// </summary>
    private async Task<int> SyncSlotsToConfiguracionInternalAsync(TemplateRecarga template)
    {
        int count = 0;

        foreach (var periodo in template.Periodos)
        {
            foreach (var slot in periodo.SnapshotSlots)
            {
                var existing = await _context.ConfiguracionSlots
                    .FirstOrDefaultAsync(c =>
                        c.MaquinaId == periodo.MaquinaId &&
                        c.NumeroSlot == slot.NumeroSlot);

                if (existing != null)
                {
                    existing.ProductoId = slot.ProductoId;
                    existing.StockActual = slot.CantidadInicial;
                    existing.CapacidadMaxima = slot.CapacidadSlot > 0 ? slot.CapacidadSlot : existing.CapacidadMaxima;
                }
                else
                {
                    _context.ConfiguracionSlots.Add(new ConfiguracionSlot
                    {
                        MaquinaId = periodo.MaquinaId,
                        NumeroSlot = slot.NumeroSlot,
                        ProductoId = slot.ProductoId,
                        StockActual = slot.CantidadInicial,
                        CapacidadMaxima = slot.CapacidadSlot > 0 ? slot.CapacidadSlot : 10,
                        StockMinimo = 2,
                        PrecioVenta = 0
                    });
                }

                count++;
            }
        }

        if (count > 0)
        {
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "[SyncSlotsToConfiguracion] Template {TemplateId}: {Count} slots synced",
                template.Id, count);
        }
        else
        {
            _logger.LogWarning(
                "[SyncSlotsToConfiguracion] Template {TemplateId} has no slots — skipping inventory sync",
                template.Id);
        }

        return count;
    }

    private static TemplateRecargaDto MapToDto(TemplateRecarga t)
    {
        return new TemplateRecargaDto
        {
            Id = t.Id,
            Nombre = t.Nombre,
            Descripcion = t.Descripcion,
            FechaCreacion = t.FechaCreacion,
            Estado = t.Estado,
            Periodos = t.Periodos.Select(p => new PeriodoRecargaDto
            {
                Id = p.Id,
                MaquinaId = p.MaquinaId,
                MaquinaNombre = p.Maquina?.Nombre ?? "Desconocida",
                FechaRecarga = p.FechaRecarga,
                FechaFin = p.FechaRecarga.AddYears(2), // placeholder — not used in lifecycle context
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