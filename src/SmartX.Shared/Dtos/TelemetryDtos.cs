using SmartX.Shared.Models;

namespace SmartX.Shared.Dtos;

/// <summary>What the client posts to /api/telemetry to ingest one sample.</summary>
public sealed class TelemetryIngestRequest
{
    public string SensorId { get; set; } = string.Empty;
    public TelemetryValueKind Kind { get; set; }

    /// <summary>Used when Kind is Float or Integer.</summary>
    public double NumericValue { get; set; }

    /// <summary>Used when Kind is Boolean.</summary>
    public bool BooleanValue { get; set; }

    public string Unit { get; set; } = string.Empty;
}

/// <summary>What the server pushes back down (REST + SignalR) for the live feed / dashboard.</summary>
public sealed class TelemetryFeedItem
{
    public string SensorId { get; set; } = string.Empty;
    public string DisplayValue { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public bool IsAnomaly { get; set; }

    // ---- Part 2 additions (all optional, so Part 1 consumers keep working) ----

    /// <summary>Friendly device name, e.g. "Pump A".</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Which pipeline lane processed this packet.</summary>
    public AlertPriority Lane { get; set; }

    /// <summary>How long the packet waited in its queue before processing.</summary>
    public int QueueWaitMs { get; set; }
}

/// <summary>What the client posts to /api/sensors to register a new device.</summary>
public sealed class SensorRegistrationRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string DeploymentLocation { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
}
