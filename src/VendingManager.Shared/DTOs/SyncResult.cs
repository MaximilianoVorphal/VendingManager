namespace VendingManager.Shared.DTOs;

/// <summary>Outcome of a sync cycle, as classified by SyncOrchestratorService.</summary>
public enum SyncOutcome
{
    /// <summary>Data was successfully imported.</summary>
    Ok,
    /// <summary>Scraper returned valid response with zero new rows — not a failure.</summary>
    Empty,
    /// <summary>WAF/anti-automation signal detected.</summary>
    Blocked,
    /// <summary>Unclassified/infra failure (exception, malformed response, etc.).</summary>
    Error,
    /// <summary>The HTTP call did not complete within the time budget.</summary>
    Timeout
}

/// <summary>Structured result from a sync cycle, used by AutomatedReportService to drive PollOutcome.</summary>
public class SyncResult
{
    public SyncOutcome Outcome { get; init; }
    public string? Stats { get; init; }
    public string? Details { get; init; }
}
