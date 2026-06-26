using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

/// <summary>
/// Repository for ProveedorAlias entities.
/// CRITICAL: All methods are PURE EF change-tracker mutations — NO internal SaveChanges calls.
/// Atomicity/SaveChanges belongs to the service layer (Design S3).
/// Mirrors the ProductoEAN repository pattern.
/// </summary>
public interface IProveedorAliasRepository
{
    /// <summary>Gets an alias by its normalized key for O(1) Step-0a exact lookup.</summary>
    Task<ProveedorAlias?> GetByNormalizedNameAsync(string normalizedName, CancellationToken ct = default);

    /// <summary>Returns all known aliases (used for bulk operations).</summary>
    Task<List<ProveedorAlias>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Stages a new alias in the change tracker (NO SaveChanges).</summary>
    Task AddAsync(ProveedorAlias alias, CancellationToken ct = default);

    /// <summary>Marks an existing alias as updated in the change tracker (NO SaveChanges).</summary>
    Task UpdateAsync(ProveedorAlias alias, CancellationToken ct = default);

    /// <summary>Stages removal of an alias (NO SaveChanges).</summary>
    Task DeleteAsync(ProveedorAlias alias, CancellationToken ct = default);
}
