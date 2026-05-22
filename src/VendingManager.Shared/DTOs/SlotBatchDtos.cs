namespace VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

/// <summary>
/// Request for batch slot actions on a template's periodo.
/// </summary>
public class SlotBatchRequest
{
    /// <summary>
    /// List of slot actions to apply. Each action targets a specific slot.
    /// Valid ActionType values: REFILL, EMPTY, SWAP.
    /// </summary>
    public List<SlotActionDto> Actions { get; set; } = new();
}

/// <summary>
/// Response for a batch slot action operation.
/// </summary>
public class SlotBatchResponse
{
    /// <summary>
    /// Number of slots successfully processed.
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Any errors encountered during processing.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Response for a template state transition.
/// </summary>
public class TemplateEstadoResponse
{
    public int TemplateId { get; set; }
    public EstadoTemplate Estado { get; set; }
}