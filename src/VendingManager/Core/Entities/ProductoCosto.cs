namespace VendingManager.Core.Entities;

public class ProductoCosto
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }

    public decimal Costo { get; set; }
    public DateTime FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; } // null = current/open
}