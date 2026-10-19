using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Hubs;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// One place that knows every SignalR event the gateway publishes, so services stay free of
/// hub plumbing and the client has a single list of events to subscribe to.
/// </summary>
public interface IGatewayNotifier
{
    Task TelemetryAsync(TelemetryFeedItem item, CancellationToken ct = default);
    Task DeviceChangedAsync(DeviceState device, CancellationToken ct = default);
    Task AlertRaisedAsync(ActiveAlert alert, CancellationToken ct = default);
    Task AlertClearedAsync(ActiveAlert alert, CancellationToken ct = default);
    Task CommandsChangedAsync(CommandHistorySnapshot snapshot, CancellationToken ct = default);
    Task SuggestionsAsync(SuggestionsSnapshot snapshot, CancellationToken ct = default);
    Task StatsAsync(PipelineStats stats, CancellationToken ct = default);
}

public sealed class GatewayNotifier : IGatewayNotifier
{
    private const string ViewersGroup = "dashboard-viewers";
    private readonly IHubContext<TelemetryHub> _hub;

    public GatewayNotifier(IHubContext<TelemetryHub> hub)
    {
        _hub = hub;
    }

    public Task TelemetryAsync(TelemetryFeedItem item, CancellationToken ct = default) =>
        _hub.Clients.Group(ViewersGroup).SendAsync("TelemetryReceived", item, ct);

    public Task DeviceChangedAsync(DeviceState device, CancellationToken ct = default) =>
        _hub.Clients.Group(ViewersGroup).SendAsync("DeviceChanged", device, ct);

    public Task AlertRaisedAsync(ActiveAlert alert, CancellationToken ct = default) =>
        _hub.Clients.Group(ViewersGroup).SendAsync("AlertRaised", alert, ct);

    public Task AlertClearedAsync(ActiveAlert alert, CancellationToken ct = default) =>
        _hub.Clients.Group(ViewersGroup).SendAsync("AlertCleared", alert, ct);

    public Task CommandsChangedAsync(CommandHistorySnapshot snapshot, CancellationToken ct = default) =>
        _hub.Clients.Group(ViewersGroup).SendAsync("CommandsChanged", snapshot, ct);

    public Task SuggestionsAsync(SuggestionsSnapshot snapshot, CancellationToken ct = default) =>
        _hub.Clients.Group(ViewersGroup).SendAsync("SuggestionsUpdated", snapshot, ct);

    public Task StatsAsync(PipelineStats stats, CancellationToken ct = default) =>
        _hub.Clients.Group(ViewersGroup).SendAsync("PipelineStats", stats, ct);
}
