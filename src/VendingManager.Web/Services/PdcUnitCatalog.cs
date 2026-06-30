using System.Collections.Generic;

namespace VendingManager.Web.Services;

public enum PdcUnitStatus
{
    All,
    Online,
    StockLow
}

public record PdcUnitItem(int Id, string Label, string? Secondary, PdcUnitStatus Status);

public static class PdcUnitCatalog
{
    public static IReadOnlyList<PdcUnitItem> HardcodedUnits { get; } = new List<PdcUnitItem>
    {
        new(0, "Todas",             null,                    PdcUnitStatus.All),
        new(1, "Máquina 001",       "2410280012 · Online",   PdcUnitStatus.Online),
        new(2, "Máquina 002",       "2410280047 · Online",   PdcUnitStatus.Online),
        new(3, "Máquina 003",       "2410280089 · Stock bajo", PdcUnitStatus.StockLow),
    };
}
