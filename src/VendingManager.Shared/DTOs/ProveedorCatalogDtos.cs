namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO for a ProveedorCatalog entry, used in API responses.
/// </summary>
public class ProveedorCatalogDto
{
    public int Id { get; set; }
    public string NombreCanonical { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for creating a new ProveedorCatalog entry.
/// </summary>
public class CrearProveedorRequestDto
{
    public string NombreCanonical { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for reassigning a compra to a different supplier,
/// either by selecting an existing catalog entry or creating a new one.
/// </summary>
public class ReasignarProveedorRequestDto
{
    /// <summary>Id of an existing ProveedorCatalog entry to link to.</summary>
    public int? ProveedorCatalogId { get; set; }

    /// <summary>New canonical name to create (when no matching entry exists).</summary>
    public string? NuevoNombreCanonical { get; set; }
}
