using Microsoft.EntityFrameworkCore;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

public class LastSyncTracker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private DateTime? _lastSyncAt;
    private readonly object _lock = new();
    private bool _loaded;

    public LastSyncTracker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public DateTime? GetLastSync()
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                _lastSyncAt = LoadFromDb();
                _loaded = true;
            }
            return _lastSyncAt;
        }
    }

    public void SetLastSync(DateTime when)
    {
        lock (_lock)
        {
            _lastSyncAt = when;
        }
        SaveToDb(when);
    }

    private DateTime? LoadFromDb()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meta = db.SyncMetadata.FirstOrDefault(m => m.Key == "LastSyncAt");
            if (meta?.Value != null && DateTime.TryParse(meta.Value, out var parsed))
                return parsed;
        }
        catch
        {
            // Si la DB no está disponible, dejamos null — se cargará en el próximo intento
        }
        return null;
    }

    private void SaveToDb(DateTime when)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meta = db.SyncMetadata.FirstOrDefault(m => m.Key == "LastSyncAt");
            if (meta == null)
            {
                meta = new Core.Entities.SyncMetadata
                {
                    Key = "LastSyncAt",
                    Value = when.ToString("o"),
                    UpdatedAt = DateTime.UtcNow
                };
                db.SyncMetadata.Add(meta);
            }
            else
            {
                meta.Value = when.ToString("o");
                meta.UpdatedAt = DateTime.UtcNow;
            }
            db.SaveChanges();
        }
        catch
        {
            // Si la DB no está disponible, el valor en memoria sigue válido
            // Se reintentará en el próximo SetLastSync
        }
    }
}
