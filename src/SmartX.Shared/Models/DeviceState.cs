namespace SmartX.Shared.Models;

public enum DeviceStatus
{
    /// <summary>Registered but no telemetry has arrived yet.</summary>
    AwaitingData,
    Online,
    Alert,
    Disconnected
}

/// <summary>
/// Live state of one node in the mesh. The API keeps these in a
/// Dictionary&lt;string, DeviceState&gt; keyed by MAC address / node id so that every incoming
/// telemetry packet resolves its device with an O(1) lookup.
/// </summary>
public sealed class DeviceState
{
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsRegistered { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.AwaitingData;
    public TelemetryValueKind Kind { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double? LastNumericValue { get; set; }

    /// <summary>For actuators: true = ON. Commands and telemetry both update this.</summary>
    public bool? ActuatorOn { get; set; }
    public string LastDisplayValue { get; set; } = "—";
    public DateTime? LastSeenUtc { get; set; }
    public long PacketCount { get; set; }

    /// <summary>Anomalous readings seen in the last 60 seconds.</summary>
    public int RecentAnomalyCount { get; set; }

    public List<string> ActiveAlerts { get; set; } = new();

    public DeviceState Clone() => new()
    {
        DeviceId = DeviceId,
        Name = Name,
        Category = Category,
        Location = Location,
        IsRegistered = IsRegistered,
        Status = Status,
        Kind = Kind,
        Unit = Unit,
        LastNumericValue = LastNumericValue,
        ActuatorOn = ActuatorOn,
        LastDisplayValue = LastDisplayValue,
        LastSeenUtc = LastSeenUtc,
        PacketCount = PacketCount,
        RecentAnomalyCount = RecentAnomalyCount,
        ActiveAlerts = new List<string>(ActiveAlerts)
    };
}
