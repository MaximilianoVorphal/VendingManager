namespace VendingManager.Core.Interfaces;

using VendingManager.Shared.DTOs;

/// <summary>
/// Servicio para gestionar el ciclo de vida de un TemplateRecarga.
/// Handle state transitions: Borrador → EnCarga → Activo → Cerrado.
/// Also handles sync to ConfiguracionSlots when template becomes Activo.
/// </summary>
public interface ITemplateRecargaLifecycleService
{
    /// <summary>
    /// Inicia la carga: Borrador → EnCarga.
    /// Establece FechaCargaInicio. Requiere al menos un SnapshotSlot.
    /// </summary>
    Task<TemplateRecargaDto> StartLoadingAsync(int templateId);

    /// <summary>
    /// Finaliza la carga: EnCarga → Activo.
    /// Establece FechaCargaFin. Sincroniza SnapshotSlots → ConfiguracionSlots.
    /// </summary>
    Task<TemplateRecargaDto> FinalizeAsync(int templateId);

    /// <summary>
    /// Cierra el template: Activo → Cerrado.
    /// Template queda como histórico, no permite más transiciones.
    /// </summary>
    Task<TemplateRecargaDto> CloseAsync(int templateId);

    /// <summary>
    /// Resetea a borrador: cualquier estado → Borrador.
    /// Limpia todos los SnapshotSlots y nullea fechas de carga.
    /// </summary>
    Task<TemplateRecargaDto> ResetToDraftAsync(int templateId);

    /// <summary>
    /// Obtiene los SnapshotSlots del template Activo más reciente para una máquina.
    /// Usado por PurchasingService para stock-critico.
    /// </summary>
    Task<List<SnapshotSlotDto>> GetActiveTemplateSlotsAsync(int maquinaId);

    /// <summary>
    /// Sincroniza los SnapshotSlots del template a ConfiguracionSlots.
    /// Se llama automáticamente en FinalizeAsync.
    /// </summary>
    Task<int> SyncSlotsToConfiguracionAsync(int templateId);
}