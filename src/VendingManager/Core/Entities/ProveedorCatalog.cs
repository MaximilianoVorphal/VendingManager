using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities;

/// <summary>
/// Canonical supplier identity, owner-curated. Acts as the authoritative name
/// for a supplier, to which raw OCR strings are resolved via aliases.
/// </summary>
public class ProveedorCatalog
{
    [Key]
    public int Id { get; set; }

    /// <summary>Curated canonical display name for the supplier (required, unique).</summary>
    [Required]
    [MaxLength(200)]
    public string NombreCanonical { get; set; } = string.Empty;

    /// <summary>Timestamp of first registration.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last time this catalog entry was linked from a compra. Mirrors ProductoEAN.LastSeenAt.</summary>
    public DateTime? LastSeenAt { get; set; }
}
