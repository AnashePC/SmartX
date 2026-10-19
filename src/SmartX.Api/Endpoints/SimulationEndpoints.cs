using SmartX.Api.Services;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

/// <summary>
/// Test hooks. Everything here pushes packets through ITelemetryPipeline.Enqueue — exactly the path a
/// real device's POST /api/telemetry takes — so the queues, priority lane and alert set are exercised for real.
/// </summary>
public static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/simulation").WithTags("Simulation");

        // POST /api/simulation/critical?kind=power&deviceId=ESP32-02
        // kind: power | moisture-low | moisture-high
        group.MapPost("/critical", (string? kind, string? deviceId, ITelemetryPipeline pipeline) =>
        {
            var request = (kind ?? "power").ToLowerInvariant() switch
            {
                "power" => new TelemetryIngestRequest
                {
                    SensorId = deviceId ?? "ESP32-02",
                    Kind = TelemetryValueKind.Integer,
                    NumericValue = 1050,
                    Unit = "W"
                },
                "moisture-low" => new TelemetryIngestRequest
                {
                    SensorId = deviceId ?? "ESP32-04",
                    Kind = TelemetryValueKind.Float,
                    NumericValue = 12,
                    Unit = "% moisture"
                },
                "moisture-high" => new TelemetryIngestRequest
                {
                    SensorId = deviceId ?? "ESP32-04",
                    Kind = TelemetryValueKind.Float,
                    NumericValue = 93,
                    Unit = "% moisture"
                },
                _ => null
            };

            if (request is null)
            {
                return Results.BadRequest("kind must be one of: power, moisture-low, moisture-high.");
            }

            return Results.Accepted(value: pipeline.Enqueue(request));
        });

        // POST /api/simulation/burst?count=300
        // Floods the STANDARD queue with routine packets and then sends ONE critical packet behind them.
        // The critical packet is still processed first — compare the two lanes' wait times in /api/pipeline/recent.
        group.MapPost("/burst", (int? count, ITelemetryPipeline pipeline, IDeviceStateStore devices) =>
        {
            var total = Math.Clamp(count ?? 300, 1, 2000);
            var sensors = devices.GetAll()
                .Where(d => d.Category != SensorCategory.Actuator)
                .ToList();

            if (sensors.Count == 0)
            {
                return Results.BadRequest("No sensors are registered yet — wait a moment for the simulator to start.");
            }

            var random = new Random();
            var accepted = 0;

            for (var i = 0; i < total; i++)
            {
                var sensor = sensors[i % sensors.Count];
                var isPower = sensor.Category == SensorCategory.PowerConsumption;

                var ack = pipeline.Enqueue(new TelemetryIngestRequest
                {
                    SensorId = sensor.DeviceId,
                    Kind = isPower ? TelemetryValueKind.Integer : TelemetryValueKind.Float,
                    NumericValue = isPower ? 300 + random.Next(50) : 36 + random.NextDouble() * 3,
                    Unit = isPower ? "W" : "% moisture"
                });

                if (ack.Accepted) accepted++;
            }

            var critical = pipeline.Enqueue(new TelemetryIngestRequest
            {
                SensorId = sensors.FirstOrDefault(s => s.Category == SensorCategory.PowerConsumption)?.DeviceId ?? "ESP32-02",
                Kind = TelemetryValueKind.Integer,
                NumericValue = 1100,
                Unit = "W"
            });

            return Results.Accepted(value: new
            {
                routinePacketsQueued = accepted,
                criticalPacketSequence = critical.Sequence,
                standardQueueDepth = critical.StandardQueueDepth,
                priorityQueueDepth = critical.PriorityQueueDepth,
                note = "The critical packet was queued LAST but will be processed FIRST. See GET /api/pipeline/recent."
            });
        });

        // POST /api/simulation/disconnect/ESP32-05  (call again to reconnect)
        group.MapPost("/disconnect/{deviceId}", (string deviceId, ISimulationControl control, IDeviceStateStore devices) =>
        {
            if (!devices.TryGetDevice(deviceId, out _))
            {
                return Results.NotFound($"Unknown device '{deviceId}'.");
            }

            var nowSilent = control.ToggleMute(deviceId);
            return Results.Ok(new { deviceId, silent = nowSilent });
        });

        group.MapGet("/status", (ISimulationControl control) =>
            Results.Ok(new { silentDevices = control.MutedDevices }));
    }
}
