namespace VendingManager.Shared.DTOs;

public class AnalisisProductoDto
{
    public int ProductoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty; // SKU or Barcode
    public string Categoria { get; set; } = string.Empty;
    
    // Metrics
    public int CantidadVendida { get; set; }
    public decimal TotalVentas { get; set; }     // Revenue
    public decimal TotalGanancia { get; set; }   // Revenue - Cost

    // New Analytics
    public decimal RotacionDiaria { get; set; } // Velocity
    public string Clasificacion { get; set; } = "Normal"; // Estrella, Joya, Cacho, Normal
    public decimal AporteUtilidad { get; set; } // % of total profit (optional, or just absolute $)

    // Averages (Calculated)
    public decimal PrecioPromedio => CantidadVendida > 0 ? TotalVentas / CantidadVendida : 0;
    public decimal MargenPromedio => TotalVentas > 0 ? (TotalGanancia / TotalVentas) * 100 : 0;

    // ABC Classification (REQ-2)
    public string? ClasificacionABC { get; set; }
    public decimal? PorcentajeAcumulado { get; set; }

    // Trends MoM/WoW (REQ-3)
    public string? Tendencia { get; set; }       // "▲", "▼", "→", "—"
    public decimal? CambioPorcentual { get; set; }
}
