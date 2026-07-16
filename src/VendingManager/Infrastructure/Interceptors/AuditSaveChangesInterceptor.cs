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
    /// Static registry mapping entity CLR types to their corresponding history types.
    /// Uses compile-time typeof() so entity renames break the build, not silently at runtime.
    /// Only entities in this registry produce history records.
    /// </summary>
    private static readonly Dictionary<Type, Type> HistoryTypeRegistry = new()
    {
        [typeof(Compra)] = typeof(CompraHistory),
        [typeof(Producto)] = typeof(ProductoHistory),
        [typeof(Maquina)] = typeof(MaquinaHistory),
        [typeof(Venta)] = typeof(VentaHistory),
        [typeof(MovimientoCaja)] = typeof(MovimientoCajaHistory),
        [typeof(ConfiguracionSlot)] = typeof(ConfiguracionSlotHistory),
        [typeof(GastoRecurrente)] = typeof(GastoRecurrenteHistory),
        [typeof(OrdenCarga)] = typeof(OrdenCargaHistory),
        [typeof(User)] = typeof(UserHistory),
        [typeof(Transferencia)] = typeof(TransferenciaHistory),
        [typeof(Rendicion)] = typeof(RendicionHistory),
        [typeof(ProveedorCatalog)] = typeof(ProveedorCatalogHistory),
    };

    /// <summary>
    /// Validates the audit type registry at startup. Throws
    /// <see cref="InvalidOperationException"/> if any audited entity type
    /// has no mapped history type (null value), ensuring fail-fast behavior
    /// instead of silent audit gaps.
    /// </summary>
    internal static void ValidateRegistry(Dictionary<Type, Type> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var kvp in registry)
        {
            if (kvp.Key is null)
                throw new InvalidOperationException(
                    "Audit type registry contains a null entity type key.");

            if (kvp.Value is null)
                throw new InvalidOperationException(
                    $"Audit type registry: entity type '{kvp.Key.Name}' has no mapped history type.");
        }
    }

    /// <summary>
    /// Convenience overload that validates the built-in
    /// <see cref="HistoryTypeRegistry"/> at startup.
    /// </summary>
    public static void ValidateRegistry()
    {
        ValidateRegistry(HistoryTypeRegistry);
    }

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
                if (HistoryTypeRegistry.ContainsKey(entry.Entity.GetType()))
                {
                    context.Add(historyRecord);
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
        if (!HistoryTypeRegistry.ContainsKey(entry.Entity.GetType()))
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
        var historyType = HistoryTypeRegistry[entry.Entity.GetType()];

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
                    // Handle type mismatches from ValueConverters (e.g. enum -> string).
                    // CurrentValues returns the CLR type; the history property may
                    // use the store type. Convert before calling SetValue to avoid
                    // ArgumentException when types don't match.
                    if (!prop.PropertyType.IsInstanceOfType(value))
                    {
                        if (prop.PropertyType == typeof(string))
                        {
                            // Enum -> string: produce uppercase to match store format
                            value = value is Enum
                                ? value.ToString()!.ToUpperInvariant()
                                : value.ToString();
                        }
                        else
                        {
                            value = Convert.ChangeType(value, prop.PropertyType);
                        }
                    }

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