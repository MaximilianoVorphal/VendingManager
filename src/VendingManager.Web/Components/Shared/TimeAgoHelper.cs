namespace VendingManager.Web.Components.Shared;

public static class TimeAgoHelper
{
    public static string Format(DateTime when, DateTime now)
    {
        var span = now - when;

        if (span.TotalSeconds < 60)
            return $"hace {(int)span.TotalSeconds} seg";

        if (span.TotalMinutes < 60)
        {
            var mins = (int)span.TotalMinutes;
            return mins == 1 ? "hace 1 min" : $"hace {mins} min";
        }

        if (span.TotalHours < 24)
        {
            var hours = (int)span.TotalHours;
            return hours == 1 ? "hace 1 hora" : $"hace {hours} horas";
        }

        var days = (int)span.TotalDays;
        return days == 1 ? "hace 1 día" : $"hace {days} días";
    }
}
