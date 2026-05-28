namespace VendingManager.Core.Entities;

/// <summary>
/// Mapeo EAN/SKU de proveedor a Producto del catálogo.
/// Permite lookup exacto por código de barras (EAN-8 a EAN-13).
/// Soporta packs: PackSize > 1 indica cantidad de unidades por pack.
/// </summary>
public class ProductoEAN
{
    public int Id { get; set; }

    /// <summary>Código de barras EAN-8, EAN-12 o EAN-13 (normalizado).</summary>
    public string? EAN { get; set; }

    /// <summary>SKU del proveedor (opcional).</summary>
    public string? SKU { get; set; }

    /// <summary>Producto del catálogo al que se mapea este EAN (nullable hasta que se asigne).</summary>
    public int? ProductoId { get; set; }

    /// <summary>Navegación a Producto.</summary>
    public Producto? Producto { get; set; }

    /// <summary>Nombre del proveedor que usa este EAN.</summary>
    public string? Proveedor { get; set; }

    /// <summary>Cantidad de unidades si es un pack (null = unitario, 6 = pack de 6).</summary>
    public int? PackSize { get; set; }

    /// <summary>Descripción del producto según el proveedor (para validación visual).</summary>
    public string? DescripcionProveedor { get; set; }

    /// <summary>Fecha de creación del registro.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Última vez que se vio/confirmó este EAN en una compra.</summary>
    public DateTime? LastSeenAt { get; set; }
}
