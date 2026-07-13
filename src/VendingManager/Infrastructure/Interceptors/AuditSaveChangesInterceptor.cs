using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VendingManager.Core.Entities;

namespace VendingManager.Infrastructure.Interceptors;

/// <summary>
    /// Interceptor de EF Core que captura automáticamente los cambios de estado de entidades
    /// y escribe registros de auditoría en la tabla Auditoria.
    /// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AuditSaveChangesInterceptor() { }

    public AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        WriteIndented = false
    };

    /// <summary>
    /// Mapa de nombre de tipo de entidad a nombre de tipo history.
    /// Solo entidades en este mapa escriben registros history.
    /// </summary>
    private static readonly Dictionary<string, string> HistoryTypeMap = new()
    {
        { "Compra", "CompraHistory" },
        { "Producto", "ProductoHistory" },
        { "Maquina", "MaquinaHistory" },
        { "Venta", "VentaHistory" },
        { "MovimientoCaja", "MovimientoCajaHistory" },
        { "ConfiguracionSlot", "ConfiguracionSlotHistory" },
        { "GastoRecurrente", "GastoRecurrenteHistory" },
        { "OrdenCarga", "OrdenCargaHistory" },
        { "User", "UserHistory" },
        { "Transferencia", "TransferenciaHistory" },
        { "Rendicion", "RendicionHistory" },
        { "ProveedorCatalog", "ProveedorCatalogHistory" },
        { "DepreciacionMaquina", "DepreciacionMaquinaHistory" }
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var context = eventData.Context;

        // Capturar todas las entidades rastreadas Added/Modified/Deleted
        var entries = context.ChangeTracker.Entries().ToList();

        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            var auditoria = CreateAuditoriaRecord(entry, context, _httpContextAccessor);
            if (auditoria is not null)
            {
                context.Set<Auditoria>().Add(auditoria);
            }

            // Escribir registro history si el tipo de entidad está en el mapa history
            var historyRecord = CreateHistoryRecord(entry, context, _httpContextAccessor);
            if (historyRecord is not null)
            {
                var historyTypeName = entry.Entity.GetType().Name;
                if (HistoryTypeMap.TryGetValue(historyTypeName, out var historyTypeFullName))
                {
                    var historyType = Type.GetType($"VendingManager.Core.Entities.{historyTypeFullName}");
                    if (historyType is not null)
                    {
                        // Usar context.Add para la entidad dinámica — funciona con parámetro object
                        context.Add(historyRecord);
                    }
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static Auditoria? CreateAuditoriaRecord(
        EntityEntry entry,
        DbContext context,
        IHttpContextAccessor? httpContextAccessor)
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

        // Capturar BeforeJson y AfterJson basados en el estado
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

        // Resolver Usuario: intentar IHttpContextAccessor, fallback a "system"
        var usuario = ResolveUsuario(context, httpContextAccessor);

        // Capturar clave primaria de la entidad
        var entityId = CaptureEntityId(entry);

        // Construir string de Detalle resumido
        var detalle = $"{entityType} #{entityId} {action}";

        return new Auditoria
        {
            Usuario = usuario,
            Accion = action,
            EntityId = entityId,
            EntityType = entityType,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Detalle = detalle,
            Fecha = DateTime.UtcNow
        };
    }

    private static object? CreateHistoryRecord(
        EntityEntry entry,
        DbContext context,
        IHttpContextAccessor? httpContextAccessor)
    {
        var entityType = entry.Entity.GetType().Name;
        if (!HistoryTypeMap.ContainsKey(entityType))
            return null;

        var action = entry.State switch
        {
            EntityState.Added => "Added",
            EntityState.Modified => "Modified",
            EntityState.Deleted => "Deleted",
            _ => null
        };

        if (action is null)
            return null;

        string? beforeJson = null;
        string? afterJson = null;

        if (entry.State is EntityState.Added or EntityState.Modified)
            afterJson = SerializeToJson(entry.CurrentValues);

        if (entry.State is EntityState.Modified or EntityState.Deleted)
            beforeJson = SerializeToJson(entry.OriginalValues);

        var usuario = ResolveUsuario(context, httpContextAccessor);
        var entityId = CaptureEntityId(entry);

        // Construir un registro history dinámico usando el constructor del tipo history
        var historyTypeName = HistoryTypeMap[entityType];
        var historyType = Type.GetType($"VendingManager.Core.Entities.{historyTypeName}");
        if (historyType is null)
            return null;

        var historyRecord = Activator.CreateInstance(historyType);
        if (historyRecord is null)
            return null;

        // Establecer propiedades de auditoría vía reflexión
        var historyTypeProps = historyType.GetProperties();
        var standardProps = new HashSet<string>
        {
            "EntityId", "Action", "BeforeJson", "AfterJson", "Timestamp", "Usuario"
        };

        foreach (var prop in historyTypeProps)
        {
            if (prop.Name == "EntityId" && entityId.HasValue)
                prop.SetValue(historyRecord, entityId.Value);
            else if (prop.Name == "Action")
                prop.SetValue(historyRecord, action);
            else if (prop.Name == "BeforeJson")
                prop.SetValue(historyRecord, beforeJson);
            else if (prop.Name == "AfterJson")
                prop.SetValue(historyRecord, afterJson);
            else if (prop.Name == "Timestamp")
                prop.SetValue(historyRecord, DateTime.UtcNow);
            else if (prop.Name == "Usuario")
                prop.SetValue(historyRecord, usuario);
            else if (prop.Name == "Id")
            {
                // Skip PK — it's auto-generated
            }
            else if (!standardProps.Contains(prop.Name)
                     && entry.CurrentValues.Properties.Any(p => p.Name == prop.Name))
            {
                // Copy domain-specific scalar snapshot properties (e.g. NombreCanonical)
                var value = entry.CurrentValues[prop.Name];
                if (value is not null && value is not DBNull)
                {
                    prop.SetValue(historyRecord, value);
                }
            }
        }

        return historyRecord;
    }

    private static string? SerializeToJson(PropertyValues propertyValues)
    {
        try
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var property in propertyValues.Properties)
            {
                var value = propertyValues[property];
                // Omitir tipos complejos que no son escalares simples
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
        // Encontrar la clave primaria vía FindPrimaryKey
        var primaryKey = entry.Metadata.FindPrimaryKey();
        if (primaryKey is null)
            return null;

        var keyProperty = primaryKey.Properties.FirstOrDefault();
        if (keyProperty is null)
            return null;

        // Obtener el valor actual de la clave primaria
        var idValue = entry.Property(keyProperty.Name).CurrentValue;
        if (idValue is int intId)
            return intId;

        // Intentar parsear como otros tipos enteros
        if (idValue is long longId)
            return (int)longId;

        return null;
    }

    private static string ResolveUsuario(DbContext context, IHttpContextAccessor? httpContextAccessor)
    {
        // Intentar HttpContextAccessor inyectado primero
        if (httpContextAccessor?.HttpContext?.User?.Identity?.Name is { } username
            && !string.IsNullOrEmpty(username))
        {
            return username;
        }

        return "system";
    }
}