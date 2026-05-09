namespace VendingManager.Core.Configuration;

public class AnalyticsThresholds
{
    public decimal RotacionAlta { get; set; } = 1.0m;
    public decimal RotacionMedia { get; set; } = 0.2m;
    public decimal MargenAlto { get; set; } = 0.50m;

    public static AnalyticsThresholds Default => new()
    {
        RotacionAlta = 1.0m,
        RotacionMedia = 0.2m,
        MargenAlto = 0.50m
    };
}