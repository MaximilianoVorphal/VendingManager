namespace VendingManager.Shared.DTOs;

/// <summary>
/// Resumen de la sincronización global de todos los templates contra ventas históricas.
/// </summary>
public class SyncAllVentasResultDto
{
    public int TotalVentasActualizadas { get; set; }
    public int TemplatesProcesados { get; set; }
    public List<SyncTemplateVentasResult> Detalles { get; set; } = new();
}

/// <summary>
/// Resultado de sincronización para un template individual.
/// </summary>
public class SyncTemplateVentasResult
{
    public int TemplateId { get; set; }
    public string TemplateNombre { get; set; } = string.Empty;
    public int VentasActualizadas { get; set; }
}
