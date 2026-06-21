namespace VendingManager.Core.Interfaces;

/// <summary>
/// Resolves the base file-system path used for file uploads.
/// Centralises the three-step fallback so both CompraService (facturas)
/// and future services (e.g. transfer comprobantes) share a single config source.
/// </summary>
public interface IUploadPathProvider
{
    /// <summary>
    /// Returns the absolute base path for uploads.
    /// Resolution order:
    ///   1. VendingConfig.FacturaUploadPath (explicit, e.g. Docker volume)
    ///   2. IWebHostEnvironment.WebRootPath  (wwwroot when it exists on disk)
    ///   3. IWebHostEnvironment.ContentRootPath/wwwroot  (last-resort fallback)
    /// </summary>
    string GetUploadBasePath();
}
