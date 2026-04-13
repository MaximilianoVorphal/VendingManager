namespace VendingManager.Core.Entities;

public class Producto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty; // Ej: "Papas Fritas"
    public string SKU { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; } = string.Empty; // "Product barcode*"
    public string? Categoria { get; set; } = string.Empty;    // "Type*"
    public string? Proveedor { get; set; } = string.Empty;    // "Supplier*"
    public decimal PrecioVenta { get; set; } = 0;            // "Unit price*"
    public int StockBodega { get; set; } = 0;                // Stock Control

    // "Cost price" del Excel se mapeará aquí
    public decimal CostoPromedio { get; set; } = 0;
}
