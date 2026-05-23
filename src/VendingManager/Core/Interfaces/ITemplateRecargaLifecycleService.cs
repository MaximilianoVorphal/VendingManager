namespace VendingManager.Core.Interfaces;

using VendingManager.Shared.DTOs;

/// <summary>
/// Servicio para gestionar el ciclo de vida de un TemplateRecarga.
/// State machine: Pendiente (0) → Terminado (2).
/// </summary>
public interface ITemplateRecargaLifecycleService
{
    /// <summary>
    /// Termina el template: Pendiente (0) → Terminado (2).
    /// El template queda como completado, fuente para stock-critico.
    /// </summary>
    Task<TemplateRecargaDto> TerminarAsync(int templateId);

    /// <summary>
    /// Reabre el template: Terminado (2) → Pendiente (0).
    /// Preserva todos los SnapshotSlots.
    /// </summary>
    Task<TemplateRecargaDto> ReabrirAsync(int templateId);

    /// <summary>
    /// Obtiene los SnapshotSlots del template Terminado más reciente para una máquina.
    /// Usado por PurchasingService para stock-critico.
    /// </summary>
    Task<List<SnapshotSlotDto>> GetLatestTerminadoTemplateSlotsAsync(int maquinaId);

    /// <summary>
    /// Sincroniza los SnapshotSlots del template a ConfiguracionSlots.
    /// </summary>
    Task<int> SyncSlotsToConfiguracionAsync(int templateId);
}