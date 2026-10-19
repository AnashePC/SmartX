namespace SmartX.Shared.Models;

/// <summary>
/// High-level classification of a sensor node in the Smart-X mesh.
/// Drives which validation rules and dashboard widgets apply to a device.
/// </summary>
public enum SensorCategory
{
    Environmental,      // e.g. soil moisture, temperature, humidity (float)
    PowerConsumption,   // e.g. smart meter wattage (int)
    Actuator            // e.g. valve / relay / switch state (bool)
}

/// <summary>
/// Describes the CLR value type carried by a TelemetryPacket without
/// requiring reflection at the hot ingestion path. Used as a cheap
/// discriminator when a packet has to cross a non-generic boundary
/// (e.g. JSON transport, storage list, SignalR broadcast).
/// </summary>
public enum TelemetryValueKind
{
    Float,
    Integer,
    Boolean
}
