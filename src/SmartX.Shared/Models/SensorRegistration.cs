namespace SmartX.Shared.Models;

/// <summary>A registered device profile in the Smart-X mesh.</summary>
public sealed class SensorRegistration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>Human-readable location, e.g. "Sub-Zone B -> Zone 1 -> Facility A".</summary>
    public string DeploymentLocation { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Server-side file names for any config/photo/log uploads attached to this sensor.</summary>
    public List<string> AttachedFileNames { get; set; } = new();
}
