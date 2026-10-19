namespace SmartX.Shared.Models;

public enum SuggestionKind
{
    /// <summary>Run a manual override command (one click).</summary>
    Command,

    /// <summary>Jump to / focus a device the operator usually checks next.</summary>
    OpenDevice,

    /// <summary>A free-text search the operator habitually runs.</summary>
    Investigate
}

public enum InsightSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>A proactive "Suggested Action" produced by the pattern-analysis engine.</summary>
public sealed class SuggestedAction
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;

    /// <summary>0..1 — how strongly the learned history supports this suggestion.</summary>
    public double Confidence { get; set; }
    public SuggestionKind Kind { get; set; }
    public string? DeviceId { get; set; }
    public CommandAction? Command { get; set; }
}

/// <summary>An "Automated Insight": a device the engine thinks needs attention before the operator searches for it.</summary>
public sealed class AutomatedInsight
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public InsightSeverity Severity { get; set; }
    public double Score { get; set; }
}

public sealed class SuggestionsSnapshot
{
    public List<SuggestedAction> Actions { get; set; } = new();
    public List<AutomatedInsight> Insights { get; set; } = new();
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Plain-English description of what the engine is reacting to right now.</summary>
    public string ContextSummary { get; set; } = string.Empty;
    public int ObservedEvents { get; set; }
    public int LearnedTransitions { get; set; }

    /// <summary>Cheap change-detection key so the server only pushes when something actually changed.</summary>
    public string Signature() =>
        string.Join('|', Actions.Select(a => a.Id + ":" + (int)(a.Confidence * 100)))
        + "#" + string.Join('|', Insights.Select(i => i.Title));
}
