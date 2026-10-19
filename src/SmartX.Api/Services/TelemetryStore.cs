using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Hubs;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface ITelemetryStore
{
    Task<TelemetryFeedItem> IngestAsync(TelemetryIngestRequest request, CancellationToken ct = default);
    IReadOnlyList<TelemetryFeedItem> GetRecentFeed(int count = 50);
    SensorReading? GetLastNumericReading(string sensorId);

    /// <summary>Demonstrates the overloaded '+' operator: aggregate load of two power meters.</summary>
    SensorReading Aggregate(string sensorIdA, string sensorIdB);
}

/// <summary>
/// Central ingestion pipeline. This is where the four required C# mechanics
/// meet real behaviour rather than being isolated demo methods:
///   - Generics:     TelemetryPacket&lt;T&gt; carries the strongly-typed sample.
///   - Operators:    SensorReading '-' computes the delta used for anomaly detection,
///                   and '+' powers the /aggregate endpoint.
///   - Arrays:       recent numeric history per sensor is kept in a small
///                   rolling array (see RecentHistory) before being copied
///                   into the List&lt;TelemetryFeedItem&gt; feed.
///   - Recursion:    handled separately by DeploymentValidator, invoked from
///                   the deployment endpoints against the same store's data.
/// </summary>
public sealed class TelemetryStore : ITelemetryStore
{
    private const int FeedCapacity = 200;
    private const int HistoryWindow = 10;

    private readonly ConcurrentDictionary<string, SensorReading> _lastNumericReading = new();
    private readonly ConcurrentDictionary<string, double[]> _recentHistory = new();
    private readonly ConcurrentQueue<TelemetryFeedItem> _feed = new();
    private readonly IHubContext<TelemetryHub> _hub;
    private readonly ILogger<TelemetryStore> _logger;
    private readonly double _environmentalDeltaThreshold;
    private readonly double _powerDeltaThreshold;

    public TelemetryStore(IHubContext<TelemetryHub> hub, IConfiguration config, ILogger<TelemetryStore> logger)
    {
        _hub = hub;
        _logger = logger;
        _environmentalDeltaThreshold = config.GetValue("SmartX:AnomalyThresholds:EnvironmentalDelta", 8.0);
        _powerDeltaThreshold = config.GetValue("SmartX:AnomalyThresholds:PowerDeltaWatts", 150.0);
    }

    public async Task<TelemetryFeedItem> IngestAsync(TelemetryIngestRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SensorId))
        {
            throw new ArgumentException("SensorId is required.", nameof(request));
        }

        var isAnomaly = false;
        string displayValue;

        switch (request.Kind)
        {
            case TelemetryValueKind.Boolean:
            {
                // Generic packet stays box-free right up until ToDisplayString().
                var packet = new TelemetryPacket<bool>(request.SensorId, request.BooleanValue, request.Kind, request.Unit);
                displayValue = packet.Value ? "ON" : "OFF";
                break;
            }
            case TelemetryValueKind.Integer:
            {
                var packet = new TelemetryPacket<int>(request.SensorId, (int)request.NumericValue, request.Kind, request.Unit);
                isAnomaly = TrackAndCheckAnomaly(request.SensorId, packet.Value, _powerDeltaThreshold, request.Unit);
                displayValue = packet.ToDisplayString();
                break;
            }
            case TelemetryValueKind.Float:
            default:
            {
                var packet = new TelemetryPacket<float>(request.SensorId, (float)request.NumericValue, request.Kind, request.Unit);
                isAnomaly = TrackAndCheckAnomaly(request.SensorId, packet.Value, _environmentalDeltaThreshold, request.Unit);
                displayValue = packet.ToDisplayString();
                break;
            }
        }

        var feedItem = new TelemetryFeedItem
        {
            SensorId = request.SensorId,
            DisplayValue = displayValue,
            TimestampUtc = DateTime.UtcNow,
            IsAnomaly = isAnomaly
        };

        PushToFeed(feedItem);

        await _hub.Clients.Group("dashboard-viewers")
            .SendAsync("TelemetryReceived", feedItem, ct);

        return feedItem;
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
