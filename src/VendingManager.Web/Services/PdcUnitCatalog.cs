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
/// Static unit entry used by the home sidebar and unit selector.
/// <see cref="OurVendId"/> is the OURVEND machine id used to look up real online
/// status from the scraper API. Leave null for synthetic entries (e.g. "Todas").
/// </summary>
public record PdcUnitItem(int Id, string Label, string? OurVendId);

public static class PdcUnitCatalog
{
    public static IReadOnlyList<PdcUnitItem> HardcodedUnits { get; } = new List<PdcUnitItem>
    {
        new(0, "Todas",       null),
        new(1, "Máquina 001", "2410280012"),
        new(2, "Máquina 002", "2410280047"),
        new(3, "Máquina 003", "2410280089"),
    };
}
