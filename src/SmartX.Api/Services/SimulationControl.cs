namespace SmartX.Api.Services;

public interface ISimulationControl
{
    bool IsMuted(string deviceId);

    /// <summary>Flips a simulated device between "sending" and "silent". Returns true if it is now silent.</summary>
    bool ToggleMute(string deviceId);

    IReadOnlyCollection<string> MutedDevices { get; }
}

/// <summary>
/// Lets the tester make a simulated ESP32 go silent, which is how a sensor disconnect is demonstrated.
/// The set of silenced ids is a HashSet — membership checks happen on every simulated packet.
/// </summary>
public sealed class SimulationControl : ISimulationControl
{
    private readonly HashSet<string> _muted = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool IsMuted(string deviceId)
    {
        lock (_gate) { return _muted.Contains(deviceId); }
    }

    public bool ToggleMute(string deviceId)
    {
        lock (_gate)
        {
            if (_muted.Remove(deviceId)) return false;
            _muted.Add(deviceId);
            return true;
        }
    }

    public IReadOnlyCollection<string> MutedDevices
    {
        get { lock (_gate) { return _muted.OrderBy(m => m).ToList(); } }
    }
}
