using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

/// <summary>
/// Servicio de matching de productos OCR contra el catálogo.
/// </summary>
public interface IProductMatchingService
{
    /// <summary>
    /// Matchea un nombre de producto OCR contra el catálogo de productos.
    /// </summary>
    /// <param name="productName">Nombre del producto extraído por OCR.</param>
    /// <returns>Resultado del matching con el producto encontrado o null.</returns>
    Task<ProductMatchResult> MatchAsync(string productName);
}

/// <summary>
/// Resultado del matching de un producto OCR.
/// </summary>
public class ProductMatchResult
{
    /// <summary>Producto matcheado del catálogo, o null si no hay match.</summary>
    public Producto? Producto { get; init; }

    /// <summary>Confianza del matching (0.0 a 1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Indica si se debe sugerir la creación de un nuevo producto.</summary>
    public bool SugerirCreacion { get; init; }

    /// <summary>Método de matching utilizado.</summary>
    public MatchMethod MatchMethod { get; init; }
}

/// <summary>
/// Método utilizado para hacer el match de producto.
/// </summary>
public enum MatchMethod
{
    /// <summary>No se encontró match.</summary>
    None,

    /// <summary>Match por tokenización + Levenshtein fuzzy.</summary>
    Tokenized,

    /// <summary>Match exacto por código de barras.</summary>
    Barcode
}
