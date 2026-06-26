using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities;

/// <summary>
/// Raw OCR string known to map to a canonical supplier.
/// Mirrors the ProductoEAN pattern: each alias is uniquely indexed by its normalized form
/// for O(1) Step-0a exact lookup.
/// </summary>
public class ProveedorAlias
{
    [Key]
    public int Id { get; set; }

    /// <summary>Original raw OCR string as received from the invoice.</summary>
    [Required]
    [MaxLength(200)]
    public string RawName { get; set; } = string.Empty;

    /// <summary>
    /// Normalized key used for exact lookup: lowercase + trim + collapse whitespace + strip diacritics.
    /// Computed by <see cref="VendingManager.Core.Utils.NameNormalizer.Normalize"/>.
    /// This is the uniquely indexed column.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string RawNameNormalized { get; set; } = string.Empty;

    /// <summary>FK to the canonical supplier this alias belongs to.</summary>
    public int ProveedorCatalogId { get; set; }

    /// <summary>Navigation to the canonical supplier.</summary>
    public ProveedorCatalog? ProveedorCatalog { get; set; }

    /// <summary>Timestamp of first registration.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last time this raw string was seen on an invoice.</summary>
    public DateTime? LastSeenAt { get; set; }
}
