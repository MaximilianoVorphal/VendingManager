using System.Collections.Generic;

namespace VendingManager.Web.Services;

public enum PdcUnitStatus
{
    All,
    Online,
    StockLow,
    Offline
}

/// <summary>
/// Static unit entry used by the home unit selector. The home sidebar does NOT
/// consume this catalog — it builds its list from the live machine-status API.
/// </summary>
public record PdcUnitItem(int Id, string Label);

public static class PdcUnitCatalog
{
    public static IReadOnlyList<PdcUnitItem> HardcodedUnits { get; } = new List<PdcUnitItem>
    {
        new(0, "Todas"),
        new(1, "Máquina 001"),
        new(2, "Máquina 002"),
        new(3, "Máquina 003"),
    };
}
