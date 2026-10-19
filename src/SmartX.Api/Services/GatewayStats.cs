using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface IGatewayStats
{
    PipelineStats Build();
}

/// <summary>Assembles the live counters shown on the Command Stream page from the services that own them.</summary>
public sealed class GatewayStats : IGatewayStats
{
    private readonly ITelemetryPipeline _pipeline;
    private readonly IAlertTracker _alerts;
    private readonly IDeviceStateStore _devices;
    private readonly ICommandService _commands;

    public GatewayStats(ITelemetryPipeline pipeline, IAlertTracker alerts, IDeviceStateStore devices,
        ICommandService commands)
    {
        _pipeline = pipeline;
        _alerts = alerts;
        _devices = devices;
        _commands = commands;
    }

    public PipelineStats Build()
    {
        var counters = _pipeline.GetCounters();

        return new PipelineStats
        {
            StandardQueueDepth = counters.StandardDepth,
            PriorityQueueDepth = counters.PriorityDepth,
            TotalEnqueued = counters.Enqueued,
            TotalProcessed = counters.Processed,
            StandardProcessed = counters.StandardProcessed,
            PriorityProcessed = counters.PriorityProcessed,
            RejectedPackets = counters.Rejected,
            AverageStandardWaitMs = Math.Round(counters.AverageStandardWaitMs, 1),
            AveragePriorityWaitMs = Math.Round(counters.AveragePriorityWaitMs, 1),
            DuplicateAlertsSuppressed = _alerts.DuplicatesSuppressed,
            ActiveAlertCount = _alerts.ActiveCount,
            DisconnectedDeviceCount = _alerts.DisconnectedCount,
            DeviceCount = _devices.Count,
            UndoDepth = _commands.UndoDepth,
            ServerTimeUtc = DateTime.UtcNow
        };
    }
}
