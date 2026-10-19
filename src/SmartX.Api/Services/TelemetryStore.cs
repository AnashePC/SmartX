using System.Collections.Concurrent;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface ITelemetryStore
{
    /// <summary>Processes one packet that has been dequeued from the pipeline (priority lane first).</summary>
    Task<TelemetryFeedItem> ProcessAsync(QueuedPacket packet, CancellationToken ct = default);

    IReadOnlyList<TelemetryFeedItem> GetRecentFeed(int count = 50);
    SensorReading? GetLastNumericReading(string sensorId);

    /// <summary>Demonstrates the overloaded '+' operator: aggregate load of two power meters.</summary>
    SensorReading Aggregate(string sensorIdA, string sensorIdB);
}

/// <summary>
/// Processing stage of the ingestion pipeline. Part 1 mechanics are unchanged:
///   - Generics:   TelemetryPacket&lt;T&gt; carries the strongly-typed sample.
///   - Operators:  SensorReading '-' computes the delta used for anomaly detection, '+' powers /aggregate.
///   - Arrays:     a small rolling array holds each sensor's recent numeric history.
/// Part 2 adds what happens AFTER a packet is validated:
///   - the device registry Dictionary is updated (O(1) lookup by sensor id),
///   - the SortedDictionary timeline gets a new point,
///   - the alert HashSet raises (or suppresses a duplicate of) a critical alert,
///   - and the result is pushed to every connected dashboard over SignalR.
/// </summary>
public sealed class TelemetryStore : ITelemetryStore
{
    private const int FeedCapacity = 200;
    private const int HistoryWindow = 10;

    private readonly ConcurrentDictionary<string, SensorReading> _lastNumericReading = new();
    private readonly ConcurrentDictionary<string, double[]> _recentHistory = new();
    private readonly ConcurrentQueue<TelemetryFeedItem> _feed = new();
    private readonly IGatewayNotifier _notifier;
    private readonly IDeviceStateStore _devices;
    private readonly ITimelineStore _timeline;
    private readonly IAlertTracker _alerts;
    private readonly IActionSuggestionEngine _engine;
    private readonly ILogger<TelemetryStore> _logger;
    private readonly double _environmentalDeltaThreshold;
    private readonly double _powerDeltaThreshold;

    public TelemetryStore(
        IGatewayNotifier notifier,
        IDeviceStateStore devices,
        ITimelineStore timeline,
        IAlertTracker alerts,
        IActionSuggestionEngine engine,
        IConfiguration config,
        ILogger<TelemetryStore> logger)
    {
        _notifier = notifier;
        _devices = devices;
        _timeline = timeline;
        _alerts = alerts;
        _engine = engine;
        _logger = logger;
        _environmentalDeltaThreshold = config.GetValue("SmartX:AnomalyThresholds:EnvironmentalDelta", 8.0);
        _powerDeltaThreshold = config.GetValue("SmartX:AnomalyThresholds:PowerDeltaWatts", 150.0);
    }

    public async Task<TelemetryFeedItem> ProcessAsync(QueuedPacket queued, CancellationToken ct = default)
    {
        var request = queued.Request;
        var isAnomaly = false;
        string displayValue;
        double? numeric = null;
        bool? boolean = null;

        switch (request.Kind)
        {
            case TelemetryValueKind.Boolean:
            {
                // Generic packet stays box-free right up until ToDisplayString().
                var packet = new TelemetryPacket<bool>(request.SensorId, request.BooleanValue, request.Kind, request.Unit);
                displayValue = packet.Value ? "ON" : "OFF";
                boolean = packet.Value;
                break;
            }
            case TelemetryValueKind.Integer:
            {
                var packet = new TelemetryPacket<int>(request.SensorId, (int)request.NumericValue, request.Kind, request.Unit);
                isAnomaly = TrackAndCheckAnomaly(request.SensorId, packet.Value, _powerDeltaThreshold, request.Unit);
                displayValue = packet.ToDisplayString();
                numeric = packet.Value;
                break;
            }
            case TelemetryValueKind.Float:
            default:
            {
                var packet = new TelemetryPacket<float>(request.SensorId, (float)request.NumericValue, request.Kind, request.Unit);
                isAnomaly = TrackAndCheckAnomaly(request.SensorId, packet.Value, _environmentalDeltaThreshold, request.Unit);
                displayValue = packet.ToDisplayString();
                numeric = packet.Value;
                break;
            }
        }

        var now = DateTime.UtcNow;
        var isCritical = queued.Lane == AlertPriority.Critical;
        isAnomaly |= isCritical;

        // 1) Device registry: O(1) dictionary lookup/update by sensor id.
        var device = _devices.RecordTelemetry(request.SensorId, request.Kind, numeric, boolean,
            request.Unit, displayValue, isAnomaly, now);

        // 2) Ordered history: SortedDictionary keyed by timestamp.
        _timeline.Append(new TimelinePoint
        {
            SensorId = request.SensorId,
            TimestampUtc = now,
            Value = numeric ?? (boolean == true ? 1 : 0),
            IsAnomaly = isAnomaly,
            Display = boolean.HasValue ? displayValue : $"{numeric:0.##}{request.Unit}"
        });

        // 3) Alerts: HashSet-backed, duplicate-suppressing.
        var alertsChanged = await EvaluateAlertsAsync(queued, device, ct);

        // Re-read so the pushed snapshot reflects any alert changes just made.
        if (_devices.TryGetDevice(request.SensorId, out var latest))
        {
            device = latest;
        }

        var feedItem = new TelemetryFeedItem
        {
            SensorId = request.SensorId,
            DeviceName = device.Name,
            DisplayValue = displayValue,
            TimestampUtc = now,
            IsAnomaly = isAnomaly,
            Lane = queued.Lane,
            QueueWaitMs = (int)Math.Round(queued.WaitMs)
        };

        PushToFeed(feedItem);

        await _notifier.TelemetryAsync(feedItem, ct);
        await _notifier.DeviceChangedAsync(device, ct);

        if (alertsChanged)
        {
            await _notifier.SuggestionsAsync(_engine.GetSuggestions(), ct);
        }

        return feedItem;
    }

    private async Task<bool> EvaluateAlertsAsync(QueuedPacket queued, DeviceState device, CancellationToken ct)
    {
        var deviceId = device.DeviceId;
        var changed = false;

        // A packet from a node we had marked disconnected proves it is back.
        var reconnected = _alerts.ClearDisconnect(deviceId);
        if (reconnected is not null)
        {
            changed = true;
            await _notifier.AlertClearedAsync(reconnected, ct);
        }

        if (queued.Lane == AlertPriority.Critical && queued.Classification.Kind is { } kind)
        {
            var alert = new ActiveAlert
            {
                Key = ActiveAlert.BuildKey(deviceId, kind),
                DeviceId = deviceId,
                DeviceName = device.Name,
                Kind = kind,
                Message = queued.Classification.Message,
                Priority = AlertPriority.Critical,
                RaisedUtc = DateTime.UtcNow
            };

            if (_alerts.TryRaise(alert))
            {
                changed = true;
                _engine.RecordAlert(alert);
                await _notifier.AlertRaisedAsync(alert, ct);
            }
            // else: duplicate of an alert that is already active — nothing more to do.
        }
        else if (queued.Request.Kind != TelemetryValueKind.Boolean)
        {
            foreach (var cleared in _alerts.ObserveNormal(deviceId))
            {
                changed = true;
                await _notifier.AlertClearedAsync(cleared, ct);
            }
        }

        _devices.SetActiveAlerts(deviceId, _alerts.KindsForDevice(deviceId));
        return changed;
    }

    /// <summary>
    /// Uses the overloaded '-' operator on SensorReading to compute the delta
    /// between this sample and the sensor's previous one, then flags an
    /// anomaly if the magnitude crosses the configured threshold. Also
    /// maintains a small rolling array (not just the latest value) so the
    /// dashboard can sparkline recent history per sensor.
    /// </summary>
    private bool TrackAndCheckAnomaly(string sensorId, double value, double threshold, string unit)
    {
        var current = new SensorReading(sensorId, value, unit);
        var isAnomaly = false;

        if (_lastNumericReading.TryGetValue(sensorId, out var previous))
        {
            try
            {
                var delta = current - previous; // overloaded operator '-'
                isAnomaly = Math.Abs(delta.Value) > threshold;
            }
            catch (InvalidOperationException ex)
            {
                // Unit changed mid-stream for this sensor — log and treat as non-anomalous
                // rather than crashing ingestion for the whole gateway.
                _logger.LogWarning(ex, "Unit mismatch while comparing readings for {SensorId}", sensorId);
            }
        }

        _lastNumericReading[sensorId] = current;
        AppendToRollingHistory(sensorId, value);

        return isAnomaly;
    }

    private void AppendToRollingHistory(string sensorId, double value)
    {
        _recentHistory.AddOrUpdate(
            sensorId,
            _ => new[] { value },
            (_, existing) =>
            {
                if (existing.Length < HistoryWindow)
                {
                    var grown = new double[existing.Length + 1];
                    Array.Copy(existing, grown, existing.Length);
                    grown[^1] = value;
                    return grown;
                }

                // Shift left, drop oldest, append newest — fixed-size rolling window array.
                var shifted = new double[HistoryWindow];
                Array.Copy(existing, 1, shifted, 0, HistoryWindow - 1);
                shifted[^1] = value;
                return shifted;
            });
    }

    private void PushToFeed(TelemetryFeedItem item)
    {
        _feed.Enqueue(item);
        while (_feed.Count > FeedCapacity && _feed.TryDequeue(out _))
        {
            // trim oldest entries
        }
    }

    public IReadOnlyList<TelemetryFeedItem> GetRecentFeed(int count = 50) =>
        _feed.Reverse().Take(count).ToList();

    public SensorReading? GetLastNumericReading(string sensorId) =>
        _lastNumericReading.TryGetValue(sensorId, out var reading) ? reading : null;

    public SensorReading Aggregate(string sensorIdA, string sensorIdB)
    {
        if (!_lastNumericReading.TryGetValue(sensorIdA, out var a))
        {
            throw new KeyNotFoundException($"No numeric reading recorded yet for '{sensorIdA}'.");
        }

        if (!_lastNumericReading.TryGetValue(sensorIdB, out var b))
        {
            throw new KeyNotFoundException($"No numeric reading recorded yet for '{sensorIdB}'.");
        }

        return a + b; // overloaded operator '+', e.g. Meter3 = Meter1 + Meter2
    }
}
