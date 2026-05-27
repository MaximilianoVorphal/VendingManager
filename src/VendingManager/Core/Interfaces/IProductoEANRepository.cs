using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

/// <summary>
/// Repositorio para la entidad ProductoEAN (mapeo EAN/SKU → Producto).
/// </summary>
public interface IProductoEANRepository
{
    /// <summary>Busca un mapeo por código EAN exacto.</summary>
    Task<ProductoEAN?> GetByEanAsync(string ean, CancellationToken ct = default);

    /// <summary>Busca un mapeo por SKU de proveedor (opcionalmente filtrado por proveedor).</summary>
    Task<ProductoEAN?> GetBySkuAndProveedorAsync(string sku, string? proveedor = null, CancellationToken ct = default);

    /// <summary>Obtiene todos los mapeos EAN registrados.</summary>
    Task<IReadOnlyList<ProductoEAN>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Agrega un nuevo mapeo EAN.</summary>
    Task AddAsync(ProductoEAN entity, CancellationToken ct = default);

    /// <summary>Actualiza un mapeo EAN existente.</summary>
    Task UpdateAsync(ProductoEAN entity, CancellationToken ct = default);

    /// <summary>Elimina un mapeo EAN por Id.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
