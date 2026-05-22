namespace VendingManager.Web.Components.Shared;

/// <summary>
/// Shared helpers for slot rendering logic used across Reposicion,
/// TemplatesRecarga, and related components.
/// </summary>
public static class SlotHelpers
{
    /// <summary>
    /// Computes the shelf (floor) index for a slot number.
    /// Uses integer division: slot 1-10 → shelf 0, 11-20 → shelf 1, 21-30 → shelf 2, etc.
    /// Equivalent to <c>(SlotNumberSort - 1) / 10</c>.
    /// </summary>
    /// <param name="slotNumber">The slot number as a string (e.g. "1", "15", "42").</param>
    /// <returns>Zero-based shelf index.</returns>
    public static int ComputeShelfIndex(string slotNumber)
    {
        if (int.TryParse(slotNumber, out int n))
            return (n - 1) / 10;
        return 0;
    }

    /// <summary>
    /// Overload that accepts an already-parsed integer slot number for callers
    /// that already have the parsed value (avoids double parsing in hot paths).
    /// </summary>
    public static int ComputeShelfIndex(int slotNumberSort)
    {
        return (slotNumberSort - 1) / 10;
    }

    /// <summary>
    /// Groups a collection of slots into shelves based on slot number.
    /// Each shelf contains slots in range [shelf*10+1, (shelf+1)*10].
    /// </summary>
    public static IEnumerable<IGrouping<int, T>> GroupByShelf<T>(
        IEnumerable<T> slots,
        Func<T, string> slotNumberSelector)
    {
        return slots
            .GroupBy(s => ComputeShelfIndex(slotNumberSelector(s)))
            .OrderBy(g => g.Key);
    }

    /// <summary>
    /// Overload for callers that already have parsed slot numbers.
    /// </summary>
    public static IEnumerable<IGrouping<int, T>> GroupByShelf<T>(
        IEnumerable<T> slots,
        Func<T, int> slotNumberSortSelector)
    {
        return slots
            .GroupBy(s => ComputeShelfIndex(slotNumberSortSelector(s)))
            .OrderBy(g => g.Key);
    }
}