using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

public class DetalleCompra
{
    [Key]
    public int Id { get; set; }

    public int CompraId { get; set; }
    // public Compra? Compra { get; set; }

    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }
    
    public string? DescripcionItem { get; set; }

    public int Cantidad { get; set; } = 1;

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostoUnitario { get; set; } = 0;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; } = 0;
}
