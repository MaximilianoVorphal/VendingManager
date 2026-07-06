namespace VendingManager.Web.Components.Shared;

/// <summary>
/// Shared helpers for machine name processing used across Home,
/// InformeVentas, and related components.
/// </summary>
public static class MachineHelpers
{
/// <summary>
/// Extracts a short code from a machine identifier.
/// When <paramref name="idInternoMaquina"/> is provided (e.g. "2410280022"),
/// returns its last 4 characters ("0022") directly — this is the preferred path.
/// Otherwise falls back to parsing <paramref name="machineName"/>:
///   - If name starts with "MAQUINA", returns the last 4 digits of the first token after the prefix.
///   - Otherwise, returns the first 6 characters.
/// Returns "---" when both inputs are null/empty.
/// </summary>
public static string ExtractShortCode(string? machineName, string? idInternoMaquina = null)
{
    // Preferred path: use the internal machine identifier
    if (!string.IsNullOrEmpty(idInternoMaquina) && idInternoMaquina.Length >= 4)
        return idInternoMaquina.Substring(idInternoMaquina.Length - 4);

    // Fallback: parse from the display name
    if (string.IsNullOrEmpty(machineName))
        return "---";

    var upper = machineName.ToUpperInvariant();
    if (upper.StartsWith("MAQUINA"))
    {
        var remainder = machineName.Substring(7).Trim();
        var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0].Length >= 4)
        {
            return parts[0].Substring(parts[0].Length - 4);
        }
    }

    return machineName.Length > 6 ? machineName.Substring(0, 6) : machineName;
}
}
