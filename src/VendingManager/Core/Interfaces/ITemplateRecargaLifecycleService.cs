namespace VendingManager.Core.Interfaces;

using VendingManager.Shared.DTOs;

/// <summary>
/// Servicio para gestionar el ciclo de vida de un TemplateRecarga.
/// State machine: Pendiente (0) → Activo (1) → Terminado (2).
/// </summary>
public interface ITemplateRecargaLifecycleService
{
    /// <summary>
    /// Activa el template: Pendiente (0) → Activo (1).
    /// El template queda como la fuente activa para stock-critico.
    /// </summary>
    Task<TemplateRecargaDto> ActivarAsync(int templateId);

    /// <summary>
    /// Termina el template: Activo (1) → Terminado (2).
    /// El template queda como completado, fuente para stock-critico.
    /// </summary>
    Task<TemplateRecargaDto> TerminarAsync(int templateId);

    /// <summary>
    /// Reabre el template: Terminado (2) → Pendiente (0).
    /// Limpia todos los SnapshotSlots.
    /// </summary>
    Task<TemplateRecargaDto> ReabrirAsync(int templateId);

    /// <summary>
    /// Obtiene los SnapshotSlots del template Activo más reciente para una máquina.
    /// Usado por PurchasingService para stock-critico.
    /// </summary>
    Task<List<SnapshotSlotDto>> GetLatestActivoTemplateSlotsAsync(int maquinaId);

    /// <summary>
    /// Sincroniza los SnapshotSlots del template a ConfiguracionSlots.
    /// </summary>
    Task<int> SyncSlotsToConfiguracionAsync(int templateId);
}