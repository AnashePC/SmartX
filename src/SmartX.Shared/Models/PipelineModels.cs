namespace SmartX.Shared.Models;

/// <summary>Returned by POST /api/telemetry: the packet was accepted into a lane, not yet processed.</summary>
public sealed class IngestAck
{
    public bool Accepted { get; set; }
    public AlertPriority Lane { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public int StandardQueueDepth { get; set; }
    public int PriorityQueueDepth { get; set; }
}

/// <summary>Live gateway counters pushed to the Command Stream page every second.</summary>
public sealed class PipelineStats
{
    public int StandardQueueDepth { get; set; }
    public int PriorityQueueDepth { get; set; }
    public long TotalEnqueued { get; set; }
    public long TotalProcessed { get; set; }
    public long StandardProcessed { get; set; }
    public long PriorityProcessed { get; set; }
    public long RejectedPackets { get; set; }
    public long DuplicateAlertsSuppressed { get; set; }
    public int ActiveAlertCount { get; set; }
    public int DisconnectedDeviceCount { get; set; }
    public int DeviceCount { get; set; }
    public int UndoDepth { get; set; }
    public double AverageStandardWaitMs { get; set; }
    public double AveragePriorityWaitMs { get; set; }
    public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One point on a device's timeline graph (served from a SortedDictionary keyed by timestamp).</summary>
public sealed class TimelinePoint
{
    public DateTime TimestampUtc { get; set; }
    public string SensorId { get; set; } = string.Empty;
    public double Value { get; set; }
    public bool IsAnomaly { get; set; }
    public string Display { get; set; } = string.Empty;
}
