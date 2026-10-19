namespace SmartX.Shared.Models;

/// <summary>Which lane of the ingestion pipeline a packet travels through.</summary>
public enum AlertPriority
{
    /// <summary>Standard FIFO queue.</summary>
    Normal = 0,

    /// <summary>Priority queue — bypasses everything waiting in the standard queue.</summary>
    Critical = 1
}

public enum AlertKind
{
    PowerSpike,
    MoistureCriticalLow,
    MoistureCriticalHigh,
    Disconnected
}

/// <summary>An alert that is currently active. The API de-duplicates these with a HashSet&lt;string&gt;.</summary>
public sealed class ActiveAlert
{
    /// <summary>Unique key: "{deviceId}|{kind}". This is what the HashSet stores.</summary>
    public string Key { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public AlertKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime RaisedUtc { get; set; } = DateTime.UtcNow;
    public AlertPriority Priority { get; set; } = AlertPriority.Critical;

    public static string BuildKey(string deviceId, AlertKind kind) => $"{deviceId}|{kind}";
}
