using VendingManager.Shared.Enums;

namespace VendingManager.Shared.Models;

/// <summary>
/// A slot detected by OCR with its confidence level and detection details.
/// </summary>
public record SlotDetectado
{
    /// <summary>OCR-recognized slot identifier (e.g. "A1").</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>Index of the matched slot in the machine's actual slot list.</summary>
    public int SlotIndex { get; init; }

    /// <summary>Product name from the machine's actual slot configuration, if matched.</summary>
    public string? Producto { get; init; }

    /// <summary>Quantity detected by OCR.</summary>
    public int CantidadDetectada { get; init; }

    /// <summary>Maximum capacity of the slot from the machine configuration.</summary>
    public int Capacidad { get; init; }

    /// <summary>Confidence level of the OCR match.</summary>
    public Confianza Confianza { get; init; }
}

/// <summary>
/// Complete OCR reading result with review metadata.
/// </summary>
public record LecturaRecarga
{
    /// <summary>Machine identifier.</summary>
    public string MaquinaId { get; init; } = string.Empty;

    /// <summary>Detected slots from OCR processing.</summary>
    public List<SlotDetectado> Slots { get; init; } = new();

    /// <summary>Total units detected across all slots.</summary>
    public int TotalUnidades => Slots.Sum(s => s.CantidadDetectada);

    /// <summary>Number of slots that need review (confidence not Alta).</summary>
    public int ARevisar => Slots.Count(s => s.Confianza != Confianza.Alta);
}

/// <summary>
/// Represents a machine's actual slot configuration for comparison.
/// </summary>
public record SlotActual
{
    /// <summary>Index in the machine's slot list.</summary>
    public int Index { get; init; }

    /// <summary>Slot identifier (e.g. "A1").</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>Current product name in the slot, if any.</summary>
    public string? Producto { get; init; }

    /// <summary>Maximum capacity of the slot.</summary>
    public int Capacidad { get; init; }
}
