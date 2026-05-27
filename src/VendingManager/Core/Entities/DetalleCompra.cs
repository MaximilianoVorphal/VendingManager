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

    public bool EsPendiente { get; set; } = false;

    /// <summary>Código EAN extraído del OCR, para aprendizaje automático al guardar.</summary>
    public string? Ean { get; set; }

    /// <summary>SKU del proveedor extraído del OCR, para aprendizaje automático al guardar.</summary>
    public string? Sku { get; set; }

    /// <summary>Cantidad de unidades por pack (no mapeado a DB). Solo se usa en el flujo de creación para aprendizaje EAN.</summary>
    [NotMapped]
    public int? PackSize { get; set; }
}
