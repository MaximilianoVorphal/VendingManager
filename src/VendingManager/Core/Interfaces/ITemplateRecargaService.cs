namespace VendingManager.Core.Interfaces;

/// <summary>
/// Servicio para gestionar templates de recarga y análisis de stockout por período
/// </summary>
public interface ITemplateRecargaService
{
    /// <summary>Obtener todos los templates</summary>
    Task<List<TemplateRecargaDto>> GetAllAsync();

    /// <summary>Obtener un template por ID con sus períodos</summary>
    Task<TemplateRecargaDto?> GetByIdAsync(int id);

    /// <summary>Crear nuevo template con sus períodos</summary>
    Task<TemplateRecargaDto> CreateAsync(CreateTemplateRecargaDto dto);

    /// <summary>Actualizar template existente (reemplaza períodos)</summary>
    Task<TemplateRecargaDto> UpdateAsync(int id, UpdateTemplateRecargaDto dto);

    /// <summary>Eliminar template y sus períodos</summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// Ejecutar análisis de stockout usando los períodos del template.
    /// Cada máquina se analiza usando su rango de fecha/hora específico.
    /// </summary>
    Task<List<StockoutAnalysisDto>> AnalyzarPorTemplateAsync(int templateId, double umbralHorasSilencio = 24);

    /// <summary>
    /// Obtiene la configuración actual de slots de una máquina para crear un snapshot
    /// </summary>
    Task<List<SnapshotSlotDto>> GetSlotsForMaquinaAsync(int maquinaId);

    /// <summary>
    /// Sincroniza históricamente el ProductoId y (opcionalmente) CostoVenta 
    /// en la tabla de Ventas utilizando la configuración del Template de Recarga.
    /// </summary>
    Task<int> SyncVentasWithTemplateAsync(int templateId, bool actualizarCostos);

    /// <summary>
    /// Sincroniza el producto de un slot específico en las ventas históricas
    /// dentro de un período.
    /// </summary>
    Task<SyncSlotProductoResultDto> SyncSlotProductoAsync(int templateId, int periodoId, string numeroSlot, int productoId);
}
