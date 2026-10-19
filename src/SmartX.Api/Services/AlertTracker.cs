using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface IAlertTracker
{
    /// <summary>Returns true only if the alert was NEW. A duplicate returns false and is counted as suppressed.</summary>
    bool TryRaise(ActiveAlert alert);

    /// <summary>Call for each routine packet; returns any alerts that have now stayed quiet long enough to clear.</summary>
    IReadOnlyList<ActiveAlert> ObserveNormal(string deviceId);

    ActiveAlert? ClearDisconnect(string deviceId);
    IReadOnlyList<ActiveAlert> Snapshot();
    IReadOnlyList<AlertKind> KindsForDevice(string deviceId);
    long DuplicatesSuppressed { get; }
    int ActiveCount { get; }
    int DisconnectedCount { get; }
}

/// <summary>
/// Tracks every currently-active alert in a <see cref="HashSet{T}"/> of unique keys ("deviceId|kind").
/// Because a set only stores unique elements, <c>Add</c> returning false is the cheap, O(1) way to
/// know an alert is already being handled — so a node that stays disconnected (or keeps spiking) does
/// not trigger duplicate processing, notifications or suggestion recomputation on every tick.
///
/// Alerts latch: they only clear after <c>ClearAfterNormalReadings</c> consecutive healthy readings,
/// which stops a flapping sensor from raising and clearing the same alert over and over.
/// </summary>
public sealed class AlertTracker : IAlertTracker
{
    private readonly HashSet<string> _activeKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveAlert> _details = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _normalStreak = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly int _clearAfter;
    private long _duplicates;

    public AlertTracker(IConfiguration config)
    {
        _clearAfter = Math.Max(1, config.GetValue("SmartX:AlertRules:ClearAfterNormalReadings", 3));
    }

    public long DuplicatesSuppressed
    {
        get { lock (_gate) { return _duplicates; } }
    }

    public int ActiveCount
    {
        get { lock (_gate) { return _activeKeys.Count; } }
    }

    public int DisconnectedCount
    {
        get { lock (_gate) { return _details.Values.Count(a => a.Kind == AlertKind.Disconnected); } }
    }

    public bool TryRaise(ActiveAlert alert)
    {
        lock (_gate)
        {
            _normalStreak[alert.Key] = 0;

            if (!_activeKeys.Add(alert.Key))
            {
                _duplicates++; // already active: suppress, don't reprocess
                return false;
            }

            _details[alert.Key] = alert;
            return true;
        }
    }

    public IReadOnlyList<ActiveAlert> ObserveNormal(string deviceId)
    {
        var cleared = new List<ActiveAlert>();

        lock (_gate)
        {
            var candidates = _details.Values
                .Where(a => a.Kind != AlertKind.Disconnected
                            && string.Equals(a.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var alert in candidates)
            {
                var streak = _normalStreak.GetValueOrDefault(alert.Key) + 1;

                if (streak >= _clearAfter)
                {
                    _activeKeys.Remove(alert.Key);
                    _details.Remove(alert.Key);
                    _normalStreak.Remove(alert.Key);
                    cleared.Add(alert);
                }
                else
                {
                    _normalStreak[alert.Key] = streak;
                }
            }
        }

        return cleared;
    }

    public ActiveAlert? ClearDisconnect(string deviceId)
    {
        var key = ActiveAlert.BuildKey(deviceId, AlertKind.Disconnected);

        lock (_gate)
        {
            if (!_activeKeys.Remove(key)) return null;

            _details.Remove(key, out var alert);
            _normalStreak.Remove(key);
            return alert;
        }
    }

    public IReadOnlyList<ActiveAlert> Snapshot()
    {
        lock (_gate)
        {
            return _details.Values
                .OrderByDescending(a => a.RaisedUtc)
                .ToList();
        }
    }

    public IReadOnlyList<AlertKind> KindsForDevice(string deviceId)
    {
        lock (_gate)
        {
            return _details.Values
                .Where(a => string.Equals(a.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Kind)
                .Distinct()
                .ToList();
        }
    }
}
