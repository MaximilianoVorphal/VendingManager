using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// CRITICAL: namespace MUST be VendingManager.Core.Entities (not ...History).
// AuditSaveChangesInterceptor resolves history types via
//   Type.GetType("VendingManager.Core.Entities.{HistoryTypeName}")
// A namespace mismatch silently defeats audit recording for this entity. (Design S4)
namespace VendingManager.Core.Entities;

/// <summary>
/// Audit history entity for ProveedorCatalog.
/// Mirrors CompraHistory: audit columns + scalar snapshot of the canonical name.
/// </summary>
public class ProveedorCatalogHistory
{
    [Key]
    public int Id { get; set; }

    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string? BeforeJson { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- ProveedorCatalog scalar snapshot ---

    [Required]
    [MaxLength(200)]
    public string NombreCanonical { get; set; } = string.Empty;
}
