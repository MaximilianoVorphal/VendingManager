namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO for ProductoEAN responses in the API (avoids circular navigation serialization).
/// </summary>
public class ProductoEanDto
{
    public int Id { get; set; }
    public string? EAN { get; set; }
    public string? SKU { get; set; }
    public int? ProductoId { get; set; }
    public string? ProductoNombre { get; set; }
    public int? PackSize { get; set; }
    public string? Proveedor { get; set; }
    public string? DescripcionProveedor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

/// <summary>
/// Request DTO for creating or updating a ProductoEAN mapping.
/// </summary>
public class CreateProductoEanRequestDto
{
    public string? EAN { get; set; }
    public string? SKU { get; set; }
    public int? ProductoId { get; set; }
    public int? PackSize { get; set; }
    public string? Proveedor { get; set; }
    public string? DescripcionProveedor { get; set; }
}
