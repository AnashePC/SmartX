namespace SmartX.Shared.Models;

/// <summary>
/// Non-generic facet of a telemetry packet. Every ESP32 node on the mesh emits
/// a different CLR value type (float soil moisture, int wattage, bool valve
/// state). Storing them together (e.g. in a single List for the dashboard feed
/// or a SignalR broadcast) needs a common type. That common type is this
/// interface, NOT object — the generic TelemetryPacket&lt;T&gt; below only
/// touches <see cref="RawValue"/> (which boxes) at that single boundary point.
/// Every validation/aggregation/comparison step upstream of that boundary
/// stays fully generic over T and never boxes.
/// </summary>
public interface ITelemetryPacket
{
    string SensorId { get; }
    DateTime TimestampUtc { get; }
    TelemetryValueKind Kind { get; }
    string Unit { get; }

    /// <summary>Boxes on access. Only used by the serialization/UI boundary.</summary>
    object RawValue { get; }

    string ToDisplayString();
}

/// <summary>
/// Strongly-typed, allocation-light wrapper around a single telemetry sample.
/// T is constrained to value types that implement IComparable&lt;T&gt; so the
/// packet can be sorted/thresholded generically (float, int, bool all qualify)
/// without ever casting Value to object during that work.
/// </summary>
/// <typeparam name="T">float for environmental sensors, int for power meters,
/// bool for actuator/valve state.</typeparam>
public sealed class TelemetryPacket<T> : ITelemetryPacket where T : struct, IComparable<T>
{
    public string SensorId { get; }
    public T Value { get; }
    public TelemetryValueKind Kind { get; }
    public string Unit { get; }
    public DateTime TimestampUtc { get; }

    public TelemetryPacket(string sensorId, T value, TelemetryValueKind kind, string unit = "")
    {
        if (string.IsNullOrWhiteSpace(sensorId))
            throw new ArgumentException("SensorId is required.", nameof(sensorId));

        SensorId = sensorId;
        Value = value;
        Kind = kind;
        Unit = unit;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>Generic, box-free comparison used by the anomaly/threshold pipeline.</summary>
    public int CompareValue(TelemetryPacket<T> other) => Value.CompareTo(other.Value);

    /// <summary>Generic, box-free equality on the payload only.</summary>
    public bool ValueEquals(TelemetryPacket<T> other) => Value.CompareTo(other.Value) == 0;

    object ITelemetryPacket.RawValue => Value;

    public string ToDisplayString() => $"[{TimestampUtc:HH:mm:ss}] {SensorId} = {Value}{Unit}";
}
