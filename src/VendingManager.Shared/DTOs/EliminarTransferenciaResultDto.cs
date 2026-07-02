namespace VendingManager.Shared.DTOs;

/// <summary>
/// Result DTO returned by the DELETE /api/contabilidad/transferencia/{id} endpoint.
/// PascalCase per project default (no AddJsonOptions.PropertyNamingPolicy override).
/// </summary>
public class EliminarTransferenciaResultDto
{
    /// <summary>Number of Compras that were unlinked (TransferenciaId set to null).</summary>
    public int ComprasUnlinked { get; set; }

    /// <summary>The PeriodoId that was deleted, or null for legacy transfers.</summary>
    public int? PeriodoId { get; set; }
}
