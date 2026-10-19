using System.Collections.Concurrent;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface ISensorRegistry
{
    SensorRegistration Register(string macAddress, string deploymentLocation, SensorCategory category);
    IReadOnlyCollection<SensorRegistration> GetAll();
    SensorRegistration? GetById(Guid id);
    bool TryAttachFile(Guid id, string storedFileName);
}

/// <summary>
/// Thread-safe in-memory store for registered sensors. A real deployment
/// would back this with a database; for the gateway simulation a
/// ConcurrentDictionary is sufficient and keeps the sample self-contained.
/// </summary>
public sealed class SensorRegistry : ISensorRegistry
{
    private readonly ConcurrentDictionary<Guid, SensorRegistration> _sensors = new();

    public SensorRegistration Register(string macAddress, string deploymentLocation, SensorCategory category)
    {
        var registration = new SensorRegistration
        {
            MacAddress = macAddress,
            DeploymentLocation = deploymentLocation,
            Category = category
        };

        _sensors[registration.Id] = registration;
        return registration;
    }

    public IReadOnlyCollection<SensorRegistration> GetAll() =>
        _sensors.Values.OrderByDescending(s => s.RegisteredUtc).ToList();

    public SensorRegistration? GetById(Guid id) =>
        _sensors.TryGetValue(id, out var reg) ? reg : null;

    public bool TryAttachFile(Guid id, string storedFileName)
    {
        if (!_sensors.TryGetValue(id, out var reg))
        {
            return false;
        }

        reg.AttachedFileNames.Add(storedFileName);
        return true;
    }
}
