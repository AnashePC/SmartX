namespace SmartX.Shared.Models;

/// <summary>
/// A single numeric sensor sample (e.g. a smart-meter wattage reading) that
/// supports direct arithmetic and comparison via overloaded operators, so
/// consuming code can read like the domain instead of like plumbing:
///
///     var meter3 = meter1 + meter2;              // aggregate load
///     var delta  = latestReading - previousReading; // drift / spike check
///     if (delta > SensorReading.AnomalyThreshold) { ... }
///
/// Readings can only be combined when their units match, which is enforced
/// in the '+' and '-' operators to stop e.g. accidentally summing Watts with
/// Volts.
/// </summary>
public readonly struct SensorReading : IComparable<SensorReading>, IEquatable<SensorReading>
{
    public string SensorId { get; }
    public double Value { get; }
    public string Unit { get; }
    public DateTime TimestampUtc { get; }

    public SensorReading(string sensorId, double value, string unit = "W")
    {
        SensorId = sensorId;
        Value = value;
        Unit = unit;
        TimestampUtc = DateTime.UtcNow;
    }

    private SensorReading(string sensorId, double value, string unit, DateTime timestampUtc)
    {
        SensorId = sensorId;
        Value = value;
        Unit = unit;
        TimestampUtc = timestampUtc;
    }

    // ---- Arithmetic: aggregate two meters, e.g. Meter3 = Meter1 + Meter2 ----
    public static SensorReading operator +(SensorReading a, SensorReading b)
    {
        EnsureCompatibleUnits(a, b);
        return new SensorReading($"{a.SensorId}+{b.SensorId}", a.Value + b.Value, a.Unit, DateTime.UtcNow);
    }

    // ---- Arithmetic: delta between two readings of the same sensor over time ----
    public static SensorReading operator -(SensorReading a, SensorReading b)
    {
        EnsureCompatibleUnits(a, b);
        return new SensorReading($"{a.SensorId}-{b.SensorId}", a.Value - b.Value, a.Unit, DateTime.UtcNow);
    }

    // ---- Comparisons used for spike / threshold detection in the dashboard ----
    public static bool operator >(SensorReading a, SensorReading b) => a.Value > b.Value;
    public static bool operator <(SensorReading a, SensorReading b) => a.Value < b.Value;
    public static bool operator >=(SensorReading a, SensorReading b) => a.Value >= b.Value;
    public static bool operator <=(SensorReading a, SensorReading b) => a.Value <= b.Value;
    public static bool operator ==(SensorReading a, SensorReading b) => a.Equals(b);
    public static bool operator !=(SensorReading a, SensorReading b) => !a.Equals(b);

    private static void EnsureCompatibleUnits(SensorReading a, SensorReading b)
    {
        if (!string.Equals(a.Unit, b.Unit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot combine readings with mismatched units: '{a.Unit}' vs '{b.Unit}'.");
        }
    }

    public int CompareTo(SensorReading other) => Value.CompareTo(other.Value);

    public bool Equals(SensorReading other) =>
        SensorId == other.SensorId && Value.Equals(other.Value) && Unit == other.Unit;

    public override bool Equals(object? obj) => obj is SensorReading other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(SensorId, Value, Unit);

    public override string ToString() => $"{SensorId}: {Value:F2}{Unit} @ {TimestampUtc:HH:mm:ss}";
}
