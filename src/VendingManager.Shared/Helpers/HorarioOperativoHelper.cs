namespace VendingManager.Shared.Helpers;

/// <summary>
/// Shared utility calculating hours within the operational window (8:00–22:00)
/// between two timestamps. Extracted from TemplateRecargaAnalyticsService to
/// provide a single source of truth for velocity and loss calculations.
/// </summary>
public static class HorarioOperativoHelper
{
    public const int InicioOperativo = 8;
    public const int FinOperativo = 22;

    /// <summary>
    /// Operative hours per day: 8:00–22:00 = 14 hours.
    /// Used by stockout analysis for velocity/day conversion.
    /// </summary>
    public const int HorasOperativasPorDia = FinOperativo - InicioOperativo; // 14

    /// <summary>
    /// Returns total hours in the [8:00, 22:00) window between <paramref name="desde"/>
    /// and <paramref name="hasta"/>. Returns 0 when <paramref name="hasta"/> is not after
    /// <paramref name="desde"/>.
    /// </summary>
    public static double HorasEnRangoOperativo(DateTime desde, DateTime hasta)
    {
        if (hasta <= desde) return 0;

        double total = 0;
        var cursor = desde;

        while (cursor < hasta)
        {
            int hour = cursor.Hour;

            if (hour >= InicioOperativo && hour < FinOperativo)
            {
                var endOfHour = cursor.Date.AddHours(hour + 1);
                if (hour == FinOperativo - 1) endOfHour = cursor.Date.AddHours(FinOperativo);
                var segmentEnd = endOfHour < hasta ? endOfHour : hasta;
                total += (segmentEnd - cursor).TotalHours;
                cursor = segmentEnd;
            }
            else if (hour < InicioOperativo)
            {
                cursor = cursor.Date.AddHours(InicioOperativo);
            }
            else
            {
                cursor = cursor.Date.AddDays(1).AddHours(InicioOperativo);
            }
        }

        return total;
    }
}
