using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using VendingManager.Core.Configuration;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Default implementation of IUploadPathProvider.
/// Mirrors the three-step fallback that previously lived inline in CompraService.GetUploadBasePath().
/// </summary>
public class DefaultUploadPathProvider : IUploadPathProvider
{
    private readonly IWebHostEnvironment _env;
    private readonly IOptions<VendingConfig> _config;

    public DefaultUploadPathProvider(IWebHostEnvironment env, IOptions<VendingConfig> config)
    {
        _env = env;
        _config = config;
    }

    public string GetUploadBasePath()
    {
        // 1. Config explícita (para Docker/producción)
        var configuredPath = _config.Value.FacturaUploadPath;
        if (!string.IsNullOrEmpty(configuredPath))
            return configuredPath;

        // 2. Fallback: WebRootPath (cuando wwwroot existe)
        var webRoot = _env.WebRootPath;
        if (!string.IsNullOrEmpty(webRoot))
            return webRoot;

        // 3. Último fallback: ContentRootPath/wwwroot
        webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(webRoot);
        return webRoot;
    }
}
