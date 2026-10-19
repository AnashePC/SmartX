using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Simulates a mesh of ESP32 nodes. Every packet goes through <see cref="ITelemetryPipeline.Enqueue"/> — the
/// very same entry point POST /api/telemetry uses — so simulated data takes exactly the route real hardware
/// would: classify -> queue (standard or priority) -> process.
///
/// Part 2 makes the simulation respond to the operator. Actuators report the state set by manual override
/// commands, and each moisture sensor is irrigated by an actuator: while its pump/valve is ON the reading
/// stays healthy, and when it is switched OFF the reading drains until it breaches the critical limit.
/// That gives a closed loop to demonstrate: command -> physical effect -> alert -> suggestion -> command.
/// </summary>
public sealed class MockTelemetrySeeder : BackgroundService
{
    private sealed record SimDevice(
        string Id, string Name, SensorCategory Category, string Location, string? IrrigatedBy, bool InitiallyOn);

    private static readonly (string Name, SensorCategory Category, string Location, string? IrrigatedBy, bool On)[] Catalogue =
    {
        ("Moisture Sensor A", SensorCategory.Environmental,     "Bed 1 -> Zone 1 -> Greenhouse A",          "ESP32-06", false),
        ("Smart Meter 1",     SensorCategory.PowerConsumption,  "Panel A -> Zone 1 -> Greenhouse A",        null,       false),
        ("Pump A",            SensorCategory.Actuator,          "Pump Room -> Zone 1 -> Greenhouse A",      null,       true),
        ("Moisture Sensor B", SensorCategory.Environmental,     "Bed 2 -> Zone 1 -> Greenhouse A",          "ESP32-03", false),
        ("Smart Meter 2",     SensorCategory.PowerConsumption,  "Panel B -> Zone 2 -> Greenhouse A",        null,       false),
        ("Valve 1",           SensorCategory.Actuator,          "Bed 1 Feed Line -> Zone 1 -> Greenhouse A", null,      true),
        ("Valve 2",           SensorCategory.Actuator,          "Bed 2 Feed Line -> Zone 2 -> Greenhouse A", null,      true),
        ("Smart Relay 1",     SensorCategory.Actuator,          "Panel A -> Zone 1 -> Greenhouse A",        null,       false)
    };

    private const double HealthyMoisture = 38.0;

    private readonly ITelemetryPipeline _pipeline;
    private readonly IDeviceStateStore _devices;
    private readonly ISimulationControl _control;
    private readonly ILogger<MockTelemetrySeeder> _logger;
    private readonly bool _enabled;
    private readonly int _intervalMs;
    private readonly double _spikeProbability;
    private readonly List<SimDevice> _simDevices;
    private readonly Dictionary<string, double> _moisture = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();

    public MockTelemetrySeeder(ITelemetryPipeline pipeline, IDeviceStateStore devices, ISimulationControl control,
        IConfiguration config, ILogger<MockTelemetrySeeder> logger)
    {
        _pipeline = pipeline;
        _devices = devices;
        _control = control;
        _logger = logger;
        _enabled = config.GetValue("SmartX:MockSeeder:Enabled", true);
        _intervalMs = config.GetValue("SmartX:MockSeeder:IntervalMilliseconds", 2000);
        _spikeProbability = config.GetValue("SmartX:MockSeeder:SpikeProbability", 0.03);

        var deviceCount = Math.Max(1, config.GetValue("SmartX:MockSeeder:DeviceCount", Catalogue.Length));
        _simDevices = new List<SimDevice>();

        for (var i = 0; i < deviceCount; i++)
        {
            var id = $"ESP32-{i + 1:D2}";

            if (i < Catalogue.Length)
            {
                var c = Catalogue[i];
                _simDevices.Add(new SimDevice(id, c.Name, c.Category, c.Location, c.IrrigatedBy, c.On));
            }
            else
            {
                // Extra nodes beyond the named catalogue cycle deterministically through the categories.
                var category = (SensorCategory)(i % 3);
                _simDevices.Add(new SimDevice(id, $"Extra Node {i + 1}", category, "Spare Rack -> Zone 3", null, false));
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Mock telemetry seeder disabled via configuration.");
            return;
        }

        RegisterSimulatedDevices();
        _logger.LogInformation("Mock telemetry seeder started for {Count} simulated devices.", _simDevices.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var device in _simDevices)
                {
                    if (_control.IsMuted(device.Id)) continue; // simulated disconnect

                    _pipeline.Enqueue(GenerateSample(device));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mock telemetry seeder iteration failed.");
            }

            try
            {
                await Task.Delay(_intervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void RegisterSimulatedDevices()
    {
        foreach (var device in _simDevices)
        {
            var (kind, unit) = device.Category switch
            {
                SensorCategory.PowerConsumption => (TelemetryValueKind.Integer, "W"),
                SensorCategory.Actuator => (TelemetryValueKind.Boolean, ""),
                _ => (TelemetryValueKind.Float, "% moisture")
            };

            _devices.EnsureDevice(device.Id, device.Name, device.Category, device.Location, kind, unit,
                registered: true,
                initialActuatorState: device.Category == SensorCategory.Actuator ? device.InitiallyOn : null);

            if (device.Category == SensorCategory.Environmental)
            {
                _moisture[device.Id] = HealthyMoisture + _random.NextDouble() * 2 - 1;
            }
        }
    }

    private TelemetryIngestRequest GenerateSample(SimDevice device)
    {
        var injectSpike = _random.NextDouble() < _spikeProbability;

        switch (device.Category)
        {
            case SensorCategory.Environmental:
            {
                var level = _moisture.GetValueOrDefault(device.Id, HealthyMoisture);

                // Irrigated by an actuator: ON -> recover towards healthy; OFF -> drain.
                var irrigationOn = device.IrrigatedBy is null
                                   || (_devices.GetActuatorState(device.IrrigatedBy) ?? true);

                level = irrigationOn
                    ? level + (HealthyMoisture - level) * 0.25 + (_random.NextDouble() - 0.5) * 0.8
                    : level - 2.0 + (_random.NextDouble() - 0.5) * 0.4;

                level = Math.Clamp(level, 2, 60);
                _moisture[device.Id] = level;

                return new TelemetryIngestRequest
                {
                    SensorId = device.Id,
                    Kind = TelemetryValueKind.Float,
                    NumericValue = injectSpike ? 85 + _random.NextDouble() * 10 : Math.Round(level, 2),
                    Unit = "% moisture"
                };
            }

            case SensorCategory.PowerConsumption:
                return new TelemetryIngestRequest
                {
                    SensorId = device.Id,
                    Kind = TelemetryValueKind.Integer,
                    NumericValue = injectSpike ? 900 + _random.Next(200) : 300 + _random.Next(50),
                    Unit = "W"
                };

            default:
                // Actuators report the state the operator last commanded.
                return new TelemetryIngestRequest
                {
                    SensorId = device.Id,
                    Kind = TelemetryValueKind.Boolean,
                    BooleanValue = _devices.GetActuatorState(device.Id) ?? device.InitiallyOn,
                    Unit = ""
                };
        }
    }
}
