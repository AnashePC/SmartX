using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Once a second: (1) look for nodes that have gone silent and raise a Disconnected alert,
/// (2) push pipeline statistics, and (3) every few seconds re-run the suggestion engine and push the
/// result if it changed — so suggestions and insights appear on their own, before anyone searches.
/// </summary>
public sealed class GatewayHeartbeatService : BackgroundService
{
    private readonly IDeviceStateStore _devices;
    private readonly IAlertTracker _alerts;
    private readonly IGatewayStats _stats;
    private readonly IActionSuggestionEngine _engine;
    private readonly IGatewayNotifier _notifier;
    private readonly ILogger<GatewayHeartbeatService> _logger;
    private readonly TimeSpan _disconnectTimeout;
    private string _lastSuggestionSignature = string.Empty;

    public GatewayHeartbeatService(IDeviceStateStore devices, IAlertTracker alerts, IGatewayStats stats,
        IActionSuggestionEngine engine, IGatewayNotifier notifier, IConfiguration config,
        ILogger<GatewayHeartbeatService> logger)
    {
        _devices = devices;
        _alerts = alerts;
        _stats = stats;
        _engine = engine;
        _notifier = notifier;
        _logger = logger;
        _disconnectTimeout = TimeSpan.FromSeconds(config.GetValue("SmartX:Health:DisconnectTimeoutSeconds", 15));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var tick = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckForDisconnectsAsync(stoppingToken);
                    await _notifier.StatsAsync(_stats.Build(), stoppingToken);

                    if (++tick % 3 == 0)
                    {
                        await PublishSuggestionsIfChangedAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gateway heartbeat iteration failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task CheckForDisconnectsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Every silent node is re-evaluated on every tick. The HashSet inside the alert tracker is what
        // stops a node that STAYS silent from raising (and processing) a fresh alert each second.
        foreach (var stale in _devices.FindStale(_disconnectTimeout, now))
        {
            var silentSeconds = (int)(now - stale.LastSeenUtc!.Value).TotalSeconds;

            var alert = new ActiveAlert
            {
                Key = ActiveAlert.BuildKey(stale.DeviceId, AlertKind.Disconnected),
                DeviceId = stale.DeviceId,
                DeviceName = stale.Name,
                Kind = AlertKind.Disconnected,
                Message = $"No telemetry received for {silentSeconds}s",
                Priority = AlertPriority.Critical,
                RaisedUtc = now
            };

            if (!_alerts.TryRaise(alert))
            {
                continue; // duplicate disconnect alert — suppressed
            }

            var updated = _devices.MarkDisconnected(stale.DeviceId);
            _engine.RecordAlert(alert);

            await _notifier.AlertRaisedAsync(alert, ct);
            if (updated is not null)
            {
                await _notifier.DeviceChangedAsync(updated, ct);
            }

            await PublishSuggestionsIfChangedAsync(ct, force: true);
        }
    }

    private async Task PublishSuggestionsIfChangedAsync(CancellationToken ct, bool force = false)
    {
        var snapshot = _engine.GetSuggestions();
        var signature = snapshot.Signature();

        if (!force && signature == _lastSuggestionSignature) return;

        _lastSuggestionSignature = signature;
        await _notifier.SuggestionsAsync(snapshot, ct);
    }
}
