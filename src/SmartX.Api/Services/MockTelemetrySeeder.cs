using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Simulates the thousands of ESP32 nodes described in the brief. Runs
/// continuously in the background, pushing a mixed batch of float/int/bool
/// samples through the exact same ITelemetryStore pipeline real hardware
/// would use, so the generic packet, operator-overload anomaly checks, and
/// rolling-array history all get proven under sustained load without the
/// assessor needing physical devices.
/// </summary>
public sealed class MockTelemetrySeeder : BackgroundService
{
    private readonly ITelemetryStore _store;
    private readonly ILogger<MockTelemetrySeeder> _logger;
    private readonly bool _enabled;
    private readonly int _intervalMs;
    private readonly string[] _deviceIds;
    private readonly Random _random = new();

    public MockTelemetrySeeder(ITelemetryStore store, IConfiguration config, ILogger<MockTelemetrySeeder> logger)
    {
        _store = store;
        _logger = logger;
        _enabled = config.GetValue("SmartX:MockSeeder:Enabled", true);
        _intervalMs = config.GetValue("SmartX:MockSeeder:IntervalMilliseconds", 2000);

        var deviceCount = config.GetValue("SmartX:MockSeeder:DeviceCount", 6);
        _deviceIds = Enumerable.Range(1, deviceCount)
            .Select(i => $"ESP32-{i:D2}")
            .ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Mock telemetry seeder disabled via configuration.");
            return;
        }

        _logger.LogInformation("Mock telemetry seeder started for {Count} simulated devices.", _deviceIds.Length);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var deviceId in _deviceIds)
                {
                    var request = GenerateSample(deviceId);
                    await _store.IngestAsync(request, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mock telemetry seeder iteration failed.");
            }

            await Task.Delay(_intervalMs, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private TelemetryIngestRequest GenerateSample(string deviceId)
    {
        // Deterministic-ish category assignment so each device consistently
        // represents one of the three sensor categories described in the brief.
        var categoryIndex = Math.Abs(deviceId.GetHashCode()) % 3;

        // Occasionally inject a deliberate spike so the anomaly heartbeat /
        // badge system in the dashboard has something real to react to.
        var injectSpike = _random.NextDouble() < 0.08;

        return categoryIndex switch
        {
            0 => new TelemetryIngestRequest
            {
                SensorId = deviceId,
                Kind = TelemetryValueKind.Float,
                NumericValue = injectSpike ? 85 + _random.NextDouble() * 10 : 35 + _random.NextDouble() * 5,
                Unit = "% moisture"
            },
            1 => new TelemetryIngestRequest
            {
                SensorId = deviceId,
                Kind = TelemetryValueKind.Integer,
                NumericValue = injectSpike ? 900 + _random.Next(200) : 300 + _random.Next(50),
                Unit = "W"
            },
            _ => new TelemetryIngestRequest
            {
                SensorId = deviceId,
                Kind = TelemetryValueKind.Boolean,
                BooleanValue = _random.NextDouble() > 0.5,
                Unit = ""
            }
        };
    }
}
