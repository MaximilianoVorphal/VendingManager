namespace VendingManager.Core.Interfaces;

using VendingManager.Shared.DTOs;

/// <summary>
/// Servicio para análisis de stockout y sincronización de ventas
/// usando los datos de un TemplateRecarga.
/// </summary>
public interface ITemplateRecargaAnalyticsService
{
    /// <summary>
    /// Ejecuta análisis de stockout usando los períodos del template.
    /// Cada máquina se analiza usando su rango de fecha/hora específico.
    /// </summary>
    Task<List<StockoutAnalysisDto>> AnalyzarPorTemplateAsync(int templateId, double umbralHorasSilencio = 24);

    /// <summary>
    /// Sincroniza históricamente el ProductoId y (opcionalmente) CostoVenta
    /// en la tabla de Ventas utilizando la configuración del Template de Recarga.
    /// </summary>
    Task<int> SyncVentasWithTemplateAsync(int templateId, bool actualizarCostos);

    /// <summary>
    /// Sincroniza TODOS los templates contra las ventas históricas.
    /// Devuelve un resumen con totales y detalle por template.
    /// </summary>
    Task<SyncAllVentasResultDto> SyncAllVentasAsync(bool actualizarCostos);

    /// <summary>
    /// Sincroniza el producto de un slot específico en las ventas históricas
    /// dentro de un período.
    /// </summary>
    Task<SyncSlotProductoResultDto> SyncSlotProductoAsync(int templateId, int periodoId, string numeroSlot, int productoId);
}