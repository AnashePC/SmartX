using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface ITimelineStore
{
    void Append(TimelinePoint point);

    /// <summary>The most recent <paramref name="take"/> points for one sensor, oldest first (ready to plot).</summary>
    IReadOnlyList<TimelinePoint> GetTimeline(string sensorId, int take);

    IReadOnlyList<TimelinePoint> GetRange(string sensorId, DateTime fromUtc, DateTime toUtc);

    /// <summary>Mesh-wide sensor log, newest first.</summary>
    IReadOnlyList<TimelinePoint> GetGlobalLog(int take);
}

/// <summary>
/// Historical sensor logs kept in <see cref="SortedDictionary{TKey, TValue}"/> instances keyed by
/// timestamp. SortedDictionary is a self-balancing red-black tree: inserts are O(log n) and enumeration is
/// ALWAYS in timestamp order, so the dashboard never has to sort before drawing a timeline graph.
///
///   _perSensor : Dictionary&lt;sensorId, SortedDictionary&lt;DateTime, TimelinePoint&gt;&gt; — one ordered series per node.
///   _global    : one ordered SortedDictionary across the whole mesh for the History log.
///
/// Both are capped so a long-running gateway can't grow without bound.
/// </summary>
public sealed class TimelineStore : ITimelineStore
{
    private const int MaxPointsPerSensor = 500;
    private const int MaxGlobalPoints = 1000;

    private readonly Dictionary<string, SortedDictionary<DateTime, TimelinePoint>> _perSensor =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SortedDictionary<DateTime, TimelinePoint> _global = new();
    private readonly object _gate = new();

    public void Append(TimelinePoint point)
    {
        lock (_gate)
        {
            if (!_perSensor.TryGetValue(point.SensorId, out var series))
            {
                series = new SortedDictionary<DateTime, TimelinePoint>();
                _perSensor[point.SensorId] = series;
            }

            AddUnique(series, point, MaxPointsPerSensor);
            AddUnique(_global, point, MaxGlobalPoints);
        }
    }

    public IReadOnlyList<TimelinePoint> GetTimeline(string sensorId, int take)
    {
        lock (_gate)
        {
            if (!_perSensor.TryGetValue(sensorId, out var series)) return Array.Empty<TimelinePoint>();

            // Values enumerate in key (timestamp) order — no sort needed.
            return series.Values.Skip(Math.Max(0, series.Count - take)).ToList();
        }
    }

    public IReadOnlyList<TimelinePoint> GetRange(string sensorId, DateTime fromUtc, DateTime toUtc)
    {
        lock (_gate)
        {
            if (!_perSensor.TryGetValue(sensorId, out var series)) return Array.Empty<TimelinePoint>();

            return series
                .SkipWhile(kv => kv.Key < fromUtc)
                .TakeWhile(kv => kv.Key <= toUtc)
                .Select(kv => kv.Value)
                .ToList();
        }
    }

    public IReadOnlyList<TimelinePoint> GetGlobalLog(int take)
    {
        lock (_gate)
        {
            return _global.Values.Reverse().Take(take).ToList();
        }
    }

    private static void AddUnique(SortedDictionary<DateTime, TimelinePoint> map, TimelinePoint point, int capacity)
    {
        // Two packets can land on the same clock tick; nudge by 1 tick so keys stay unique.
        var key = point.TimestampUtc;
        while (!map.TryAdd(key, point))
        {
            key = key.AddTicks(1);
        }

        while (map.Count > capacity)
        {
            map.Remove(map.Keys.First()); // oldest entry = first in key order
        }
    }
}
