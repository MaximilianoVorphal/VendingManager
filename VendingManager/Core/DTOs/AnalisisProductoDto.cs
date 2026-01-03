namespace VendingManager.Core.DTOs;

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
    
    // Averages (Calculated)
    public decimal PrecioPromedio => CantidadVendida > 0 ? TotalVentas / CantidadVendida : 0;
    public decimal MargenPromedio => TotalVentas > 0 ? (TotalGanancia / TotalVentas) * 100 : 0;
}
