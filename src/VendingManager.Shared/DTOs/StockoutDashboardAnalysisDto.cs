namespace VendingManager.Shared.DTOs;

/// <summary>
/// Backend-owned dashboard payload containing authoritative slot results and machine-scoped product rows.
/// </summary>
public class StockoutDashboardAnalysisDto
{
    public List<StockoutAnalysisDto> Slots { get; set; } = new();
    public List<StockoutProductoMaquinaDto> ProductosMaquina { get; set; } = new();
}
