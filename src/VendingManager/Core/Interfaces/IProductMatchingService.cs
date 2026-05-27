using VendingManager.Core.Entities;

namespace VendingManager.Core.Interfaces;

/// <summary>
/// Servicio de matching de productos OCR contra el catálogo.
/// </summary>
public interface IProductMatchingService
{
    /// <summary>
    /// Matchea un nombre de producto OCR contra el catálogo de productos.
    /// Aplica Step 0 (EAN/SKU lookup) si se proporcionan, antes de caer en fuzzy matching.
    /// </summary>
    /// <param name="productName">Nombre del producto extraído por OCR.</param>
    /// <returns>Resultado del matching con el producto encontrado o null.</returns>
    Task<ProductMatchResult> MatchAsync(string productName);

    /// <summary>
    /// Matchea con contexto EAN/SKU. Antes de fuzzy matching, consulta el catálogo
    /// ProductoEAN por código de barras o SKU de proveedor.
    /// </summary>
    /// <param name="productName">Nombre del producto extraído por OCR.</param>
    /// <param name="ean">Código EAN opcional (8–13 dígitos).</param>
    /// <param name="sku">SKU del proveedor opcional.</param>
    /// <param name="proveedor">Nombre del proveedor para filtrar SKU.</param>
    /// <returns>Resultado del matching.</returns>
    Task<ProductMatchResult> MatchAsync(string productName, string? ean, string? sku, string? proveedor);

    /// <summary>
    /// Persiste (upsert) la relación EAN → ProductoId para aprendizaje automático.
    /// </summary>
    /// <param name="ean">Código EAN (8–13 dígitos).</param>
    /// <param name="productoId">ID del producto del catálogo.</param>
    /// <param name="packSize">Cantidad de unidades si es pack (null = unitario).</param>
    Task SaveMappingAsync(string ean, int productoId, int? packSize = null);
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

    /// <summary>Registro ProductoEAN asociado al match (cuando se encontró por EAN/SKU).</summary>
    public ProductoEAN? ProductoEAN { get; init; }
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
    Barcode,

    /// <summary>Match exacto por EAN o SKU desde ProductoEAN.</summary>
    Ean
}
