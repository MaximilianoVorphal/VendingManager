using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VendingManager.Core.Entities;

namespace VendingManager.Infrastructure.Interceptors;

/// <summary>
/// EF Core interceptor that automatically captures entity state changes
/// and writes audit records to the Auditoria table.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        WriteIndented = false
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var context = eventData.Context;

        // Capture all tracked Added/Modified/Deleted entities
        var entries = context.ChangeTracker.Entries().ToList();

        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            var auditoria = CreateAuditoriaRecord(entry, context);
            if (auditoria is not null)
            {
                context.Set<Auditoria>().Add(auditoria);
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static Auditoria? CreateAuditoriaRecord(EntityEntry entry, DbContext context)
    {
        var entityType = entry.Entity.GetType().Name;
        var action = entry.State switch
        {
            EntityState.Added => "Added",
            EntityState.Modified => "Modified",
            EntityState.Deleted => "Deleted",
            _ => null
        };

        if (action is null)
            return null;

        // Capture BeforeJson and AfterJson based on state
        string? beforeJson = null;
        string? afterJson = null;

        if (entry.State is EntityState.Added or EntityState.Modified)
        {
            afterJson = SerializeToJson(entry.CurrentValues);
        }

        if (entry.State is EntityState.Modified or EntityState.Deleted)
        {
            beforeJson = SerializeToJson(entry.OriginalValues);
        }

        // Resolve Usuario: try IHttpContextAccessor, fallback to "system"
        var usuario = ResolveUsuario(context);

        // Capture entity primary key
        var entityId = CaptureEntityId(entry);

        // Build a summary Detalle string
        var detalle = $"{entityType} #{entityId} {action}";

        return new Auditoria
        {
            Usuario = usuario,
            Accion = action,
            Detalle = detalle,
            Fecha = DateTime.UtcNow
        };
    }

    private static string? SerializeToJson(PropertyValues propertyValues)
    {
        try
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var property in propertyValues.Properties)
            {
                var value = propertyValues[property];
                // Skip complex types that are not simple scalars
                if (value is not string and not ValueType and not null)
                    continue;
                dictionary[property.Name] = value;
            }
            return JsonSerializer.Serialize(dictionary, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static int? CaptureEntityId(EntityEntry entry)
    {
        // Find the primary key via FindPrimaryKey
        var primaryKey = entry.Metadata.FindPrimaryKey();
        if (primaryKey is null)
            return null;

        var keyProperty = primaryKey.Properties.FirstOrDefault();
        if (keyProperty is null)
            return null;

        // Get the current value of the primary key
        var idValue = entry.Property(keyProperty.Name).CurrentValue;
        if (idValue is int intId)
            return intId;

        // Try to parse as other integer types
        if (idValue is long longId)
            return (int)longId;

        return null;
    }

    private static string ResolveUsuario(DbContext context)
    {
        if (context is IServiceProvider sp)
        {
            var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
            if (httpContextAccessor?.HttpContext?.User?.Identity?.Name is { } username
                && !string.IsNullOrEmpty(username))
            {
                return username;
            }
        }

        return "system";
    }
}