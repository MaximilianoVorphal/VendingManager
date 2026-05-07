using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;

namespace VendingManager.Infrastructure.Data;

public static class ProductoCostoExtensions
{
    /// <summary>
    /// Returns the ProductoCosto row active for the given date.
    /// Returns null if no row covers that date; caller should fall back to CostoPromedio.
    /// </summary>
    public static async Task<ProductoCosto?> GetCostoAtAsync(
        this DbSet<ProductoCosto> set, int productoId, DateTime fecha)
    {
        return await set
            .Where(pc => pc.ProductoId == productoId
                      && pc.FechaDesde <= fecha
                      && (pc.FechaHasta == null || pc.FechaHasta > fecha))
            .OrderByDescending(pc => pc.FechaDesde)
            .FirstOrDefaultAsync();
    }
}