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
/// Handles: Borrador → EnCarga → Activo → Cerrado transitions.
/// Also handles sync to ConfiguracionSlots when template becomes Activo.
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

    public async Task<TemplateRecargaDto> StartLoadingAsync(int templateId)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template con ID {templateId} no encontrado");

        if (template.Estado != EstadoTemplate.Borrador)
        {
            throw new InvalidOperationException(
                $"No se puede iniciar carga: el template está en estado {template.Estado}. " +
                $"Solo templates en estado Borrador pueden iniciar carga.");
        }

        // TL-6: Require at least one SnapshotSlot across all periods
        var totalSlots = template.Periodos.Sum(p => p.SnapshotSlots.Count);
        if (totalSlots == 0)
        {
            throw new InvalidOperationException(
                "No se puede iniciar carga: el template no tiene slots configurados. " +
                "Agregue al menos un slot antes de iniciar.");
        }

        template.Estado = EstadoTemplate.EnCarga;
        template.FechaCargaInicio = DateTime.Now;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "[StartLoading] Template {TemplateId} transitioned to EnCarga",
            templateId);

        return MapToDto(template);
    }

    public async Task<TemplateRecargaDto> FinalizeAsync(int templateId)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template con ID {templateId} no encontrado");

        if (template.Estado != EstadoTemplate.EnCarga)
        {
            throw new InvalidOperationException(
                $"No se puede finalizar carga: el template está en estado {template.Estado}. " +
                $"Solo templates en estado EnCarga pueden finalizarse.");
        }

        template.Estado = EstadoTemplate.Activo;
        template.FechaCargaFin = DateTime.Now;

        // Trigger inventory sync to ConfiguracionSlots (DI-6: skips if zero slots)
        await SyncSlotsToConfiguracionInternalAsync(template);

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "[Finalize] Template {TemplateId} transitioned to Activo",
            templateId);

        return MapToDto(template);
    }

    public async Task<TemplateRecargaDto> CloseAsync(int templateId)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template con ID {templateId} no encontrado");

        if (template.Estado != EstadoTemplate.Activo)
        {
            throw new InvalidOperationException(
                $"No se puede cerrar: el template está en estado {template.Estado}. " +
                $"Solo templates en estado Activo pueden cerrarse.");
        }

        template.Estado = EstadoTemplate.Cerrado;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "[Close] Template {TemplateId} transitioned to Cerrado",
            templateId);

        return MapToDto(template);
    }

    public async Task<TemplateRecargaDto> ResetToDraftAsync(int templateId)
    {
        var template = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.Maquina)
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                .ThenInclude(s => s.Producto)
            .FirstOrDefaultAsync(t => t.Id == templateId)
            ?? throw new InvalidOperationException($"Template con ID {templateId} no encontrado");

        template.Estado = EstadoTemplate.Borrador;
        template.FechaCargaInicio = null;
        template.FechaCargaFin = null;

        // Clear snapshot slots on reset
        foreach (var periodo in template.Periodos)
        {
            _context.SnapshotSlots.RemoveRange(periodo.SnapshotSlots);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "[ResetToDraft] Template {TemplateId} reset to Borrador",
            templateId);

        return MapToDto(template);
    }

    public async Task<List<SnapshotSlotDto>> GetActiveTemplateSlotsAsync(int maquinaId)
    {
        // Find the latest Activo template that has a periodo for this machine
        var latestActivoTemplate = await _context.TemplatesRecarga
            .Include(t => t.Periodos)
                .ThenInclude(p => p.SnapshotSlots)
                    .ThenInclude(s => s.Producto)
            .Where(t => t.Estado == EstadoTemplate.Activo)
            .Where(t => t.Periodos.Any(p => p.MaquinaId == maquinaId))
            .OrderByDescending(t => t.FechaCreacion)
            .FirstOrDefaultAsync();

        if (latestActivoTemplate == null)
        {
            return new List<SnapshotSlotDto>();
        }

        var periodoForMaquina = latestActivoTemplate.Periodos
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
    /// Called during FinalizeAsync and by SyncSlotsToConfiguracionAsync.
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
            // DI-6: Active template with no slots — skip sync and log warning
            _logger.LogWarning(
                "[SyncSlotsToConfiguracion] Template {TemplateId} has no slots — skipping inventory sync (DI-6 edge case)",
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
            FechaCargaInicio = t.FechaCargaInicio,
            FechaCargaFin = t.FechaCargaFin,
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