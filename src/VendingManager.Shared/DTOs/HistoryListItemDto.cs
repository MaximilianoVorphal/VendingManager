namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO for the Historial page list binding.
/// </summary>
public record HistoryListItemDto(
    int Id,
    int EntityId,
    string EntityType,
    string Action,
    string Usuario,
    DateTime Timestamp,
    string? BeforeJson,
    string? AfterJson
);

/// <summary>
/// Response DTO for rollback operation.
/// </summary>
public record RollbackResponseDto(
    int EntityId,
    string EntityType,
    int HistoryId,
    DateTime RolledBackAt
);