namespace VendingManager.Web.Components.Shared;

/// <summary>
/// Shared helpers for machine name processing used across Home,
/// InformeVentas, and related components.
/// </summary>
public static class MachineHelpers
{
    /// <summary>
    /// Extracts a short code from a machine name.
    /// If the name starts with "MAQUINA" (case-insensitive), returns the last 4 digits
    /// of the first whitespace-separated token after the prefix.
    /// Otherwise, returns the first 6 characters as a fallback.
    /// Returns "---" for null or empty input.
    /// </summary>
    public static string ExtractShortCode(string? machineName)
    {
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
