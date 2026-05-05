namespace VendingManager.Core.Entities;

/// <summary>
/// History entity for Producto. Mirrors Producto columns plus audit fields.
/// </summary>
public class ProductoHistory
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- Producto base columns ---
    public string Nombre { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; } = string.Empty;
    public string? Categoria { get; set; } = string.Empty;
    public string? Proveedor { get; set; } = string.Empty;
    public decimal PrecioVenta { get; set; }
    public int StockBodega { get; set; }
    public decimal CostoPromedio { get; set; }
}
