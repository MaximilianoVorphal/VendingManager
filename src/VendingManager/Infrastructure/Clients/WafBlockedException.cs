namespace VendingManager.Infrastructure.Clients;

/// <summary>Thrown when the scraper returns a WAF/block signal (503, challenge page, etc.).</summary>
public sealed class WafBlockedException : Exception
{
    /// <summary>Optional detail from the scraper's response body.</summary>
    public string? BlockReason { get; }

    public WafBlockedException(string? reason = null)
        : base(reason ?? "WAF/block signal detected from scraper")
    {
        BlockReason = reason;
    }
}
