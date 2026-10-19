using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface IDeviceStateStore
{
    int Count { get; }

    DeviceState EnsureDevice(string deviceId, string? name, SensorCategory category, string location,
        TelemetryValueKind kind, string unit, bool registered, bool? initialActuatorState = null);

    DeviceState RegisterSensor(SensorRegistration registration);

    DeviceState RecordTelemetry(string deviceId, TelemetryValueKind kind, double? numeric, bool? boolean,
        string unit, string display, bool isAnomaly, DateTime nowUtc);

    bool TryGetDevice(string deviceId, out DeviceState snapshot);
    DeviceState? ResolveByText(string text);
    IReadOnlyList<DeviceState> Query(string? text, SensorCategory? category, DeviceStatus? status);
    IReadOnlyList<DeviceState> GetAll();
    IReadOnlyList<DeviceState> GetActuators();

    bool? GetActuatorState(string deviceId);
    bool TrySetActuatorState(string deviceId, bool on, out bool previous);

    IReadOnlyList<DeviceState> FindStale(TimeSpan timeout, DateTime nowUtc);
    DeviceState? MarkDisconnected(string deviceId);
    DeviceState? SetActiveAlerts(string deviceId, IEnumerable<AlertKind> kinds);
}

/// <summary>
/// The live device registry. A plain <see cref="Dictionary{TKey, TValue}"/> keyed by MAC address /
/// node id gives every incoming telemetry packet an O(1) lookup of its device — no scanning, no nested
/// loops — even with thousands of nodes. A lock guards the dictionary; callers only ever receive
/// immutable-by-convention snapshots (clones), never the live objects.
/// </summary>
public sealed class DeviceStateStore : IDeviceStateStore
{
    private static readonly TimeSpan AnomalyWindow = TimeSpan.FromSeconds(60);

    private sealed class DeviceEntry
    {
        public DeviceState State { get; init; } = new();
        public Queue<DateTime> AnomalyTimes { get; } = new();
        public bool Disconnected { get; set; }
        public HashSet<AlertKind> AlertKinds { get; } = new();
    }

    private readonly Dictionary<string, DeviceEntry> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) { return _devices.Count; } }
    }

    public DeviceState EnsureDevice(string deviceId, string? name, SensorCategory category, string location,
        TelemetryValueKind kind, string unit, bool registered, bool? initialActuatorState = null)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var entry))
            {
                entry = new DeviceEntry
                {
                    State = new DeviceState
                    {
                        DeviceId = deviceId,
                        Name = string.IsNullOrWhiteSpace(name) ? deviceId : name,
                        Category = category,
                        Location = location,
                        IsRegistered = registered,
                        Kind = kind,
                        Unit = unit,
                        ActuatorOn = category == SensorCategory.Actuator ? initialActuatorState ?? false : null
                    }
                };
                RecomputeStatus(entry);
                _devices[deviceId] = entry;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(name)) entry.State.Name = name;
                entry.State.IsRegistered |= registered;
            }

            return Snapshot(entry, DateTime.UtcNow);
        }
    }

    public DeviceState RegisterSensor(SensorRegistration registration)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(registration.MacAddress, out var existing))
            {
                existing.State.IsRegistered = true;
                existing.State.Location = registration.DeploymentLocation;
                existing.State.Category = registration.Category;
                return Snapshot(existing, DateTime.UtcNow);
            }

            var (kind, unit) = DefaultsFor(registration.Category);
            var entry = new DeviceEntry
            {
                State = new DeviceState
                {
                    DeviceId = registration.MacAddress,
                    Name = registration.MacAddress,
                    Category = registration.Category,
                    Location = registration.DeploymentLocation,
                    IsRegistered = true,
                    Kind = kind,
                    Unit = unit,
                    ActuatorOn = registration.Category == SensorCategory.Actuator ? false : null
                }
            };
            RecomputeStatus(entry);
            _devices[registration.MacAddress] = entry;
            return Snapshot(entry, DateTime.UtcNow);
        }
    }

    public DeviceState RecordTelemetry(string deviceId, TelemetryValueKind kind, double? numeric, bool? boolean,
        string unit, string display, bool isAnomaly, DateTime nowUtc)
    {
        lock (_gate)
        {
            // O(1): the packet's sensor id IS the dictionary key.
            if (!_devices.TryGetValue(deviceId, out var entry))
            {
                entry = new DeviceEntry
                {
                    State = new DeviceState
                    {
                        DeviceId = deviceId,
                        Name = deviceId,
                        Category = CategoryFor(kind),
                        Kind = kind,
                        Unit = unit
                    }
                };
                _devices[deviceId] = entry;
            }

            var state = entry.State;
            state.Kind = kind;
            state.Unit = unit;
            state.LastDisplayValue = display;
            state.LastSeenUtc = nowUtc;
            state.PacketCount++;

            if (numeric.HasValue) state.LastNumericValue = numeric;
            if (boolean.HasValue) state.ActuatorOn = boolean;

            if (isAnomaly) entry.AnomalyTimes.Enqueue(nowUtc);

            entry.Disconnected = false; // any packet proves the node is alive again
            RecomputeStatus(entry);
            return Snapshot(entry, nowUtc);
        }
    }

    public bool TryGetDevice(string deviceId, out DeviceState snapshot)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var entry))
            {
                snapshot = Snapshot(entry, DateTime.UtcNow);
                return true;
            }
        }

        snapshot = new DeviceState();
        return false;
    }

    public DeviceState? ResolveByText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var needle = text.Trim();

        lock (_gate)
        {
            if (_devices.TryGetValue(needle, out var exactId))
            {
                return Snapshot(exactId, DateTime.UtcNow);
            }

            var now = DateTime.UtcNow;
            var matches = _devices.Values
                .Where(e => Matches(e.State, needle))
                .OrderBy(e => e.State.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var exactName = matches.FirstOrDefault(e =>
                string.Equals(e.State.Name, needle, StringComparison.OrdinalIgnoreCase));

            var chosen = exactName ?? matches.FirstOrDefault();
            return chosen is null ? null : Snapshot(chosen, now);
        }
    }

    public IReadOnlyList<DeviceState> Query(string? text, SensorCategory? category, DeviceStatus? status)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var needle = text?.Trim();

            return _devices.Values
                .Where(e => string.IsNullOrEmpty(needle) || Matches(e.State, needle))
                .Where(e => category is null || e.State.Category == category)
                .Select(e => Snapshot(e, now))
                .Where(s => status is null || s.Status == status)
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<DeviceState> GetAll() => Query(null, null, null);

    public IReadOnlyList<DeviceState> GetActuators() => Query(null, SensorCategory.Actuator, null);

    public bool? GetActuatorState(string deviceId)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(deviceId, out var entry) && entry.State.Category == SensorCategory.Actuator
                ? entry.State.ActuatorOn
                : null;
        }
    }

    public bool TrySetActuatorState(string deviceId, bool on, out bool previous)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var entry) && entry.State.Category == SensorCategory.Actuator)
            {
                previous = entry.State.ActuatorOn ?? false;
                entry.State.ActuatorOn = on;
                entry.State.LastDisplayValue = on ? "ON" : "OFF";
                return true;
            }
        }

        previous = false;
        return false;
    }

    public IReadOnlyList<DeviceState> FindStale(TimeSpan timeout, DateTime nowUtc)
    {
        lock (_gate)
        {
            return _devices.Values
                .Where(e => e.State.LastSeenUtc is { } seen && nowUtc - seen > timeout)
                .Select(e => Snapshot(e, nowUtc))
                .ToList();
        }
    }

    public DeviceState? MarkDisconnected(string deviceId)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var entry)) return null;
            entry.Disconnected = true;
            RecomputeStatus(entry);
            return Snapshot(entry, DateTime.UtcNow);
        }
    }

    public DeviceState? SetActiveAlerts(string deviceId, IEnumerable<AlertKind> kinds)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var entry)) return null;

            entry.AlertKinds.Clear();
            foreach (var kind in kinds.Where(k => k != AlertKind.Disconnected))
            {
                entry.AlertKinds.Add(kind);
            }

            RecomputeStatus(entry);
            return Snapshot(entry, DateTime.UtcNow);
        }
    }

    // ---------------- helpers (all called inside the lock) ----------------

    private static bool Matches(DeviceState state, string needle) =>
        state.DeviceId.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || state.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || state.Location.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static void RecomputeStatus(DeviceEntry entry)
    {
        var state = entry.State;
        state.ActiveAlerts = entry.AlertKinds.Select(k => k.ToString()).OrderBy(k => k).ToList();

        state.Status =
            state.LastSeenUtc is null ? DeviceStatus.AwaitingData
            : entry.Disconnected ? DeviceStatus.Disconnected
            : entry.AlertKinds.Count > 0 ? DeviceStatus.Alert
            : DeviceStatus.Online;
    }

    private static DeviceState Snapshot(DeviceEntry entry, DateTime nowUtc)
    {
        while (entry.AnomalyTimes.Count > 0 && nowUtc - entry.AnomalyTimes.Peek() > AnomalyWindow)
        {
            entry.AnomalyTimes.Dequeue();
        }

        entry.State.RecentAnomalyCount = entry.AnomalyTimes.Count;
        return entry.State.Clone();
    }

    private static SensorCategory CategoryFor(TelemetryValueKind kind) => kind switch
    {
        TelemetryValueKind.Integer => SensorCategory.PowerConsumption,
        TelemetryValueKind.Boolean => SensorCategory.Actuator,
        _ => SensorCategory.Environmental
    };

    private static (TelemetryValueKind Kind, string Unit) DefaultsFor(SensorCategory category) => category switch
    {
        SensorCategory.PowerConsumption => (TelemetryValueKind.Integer, "W"),
        SensorCategory.Actuator => (TelemetryValueKind.Boolean, ""),
        _ => (TelemetryValueKind.Float, "% moisture")
    };
}
