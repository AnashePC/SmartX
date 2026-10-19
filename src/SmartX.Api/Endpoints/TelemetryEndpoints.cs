using SmartX.Api.Services;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/telemetry").WithTags("Telemetry");

        // Part 2: the endpoint no longer processes inline. It classifies the packet and places it in the
        // standard FIFO queue or the priority queue, then answers 202 Accepted with the lane it landed in.
        group.MapPost("/", (TelemetryIngestRequest request, ITelemetryPipeline pipeline) =>
        {
            var ack = pipeline.Enqueue(request);

            if (ack.Accepted)
            {
                return Results.Accepted(value: ack);
            }

            return ack.Reason.StartsWith("Standard queue", StringComparison.Ordinal)
                ? Results.Json(ack, statusCode: StatusCodes.Status429TooManyRequests)
                : Results.BadRequest(ack);
        });

        group.MapGet("/feed", (int? count, ITelemetryStore store) =>
            Results.Ok(store.GetRecentFeed(count ?? 50)));

        // Demonstrates the overloaded '+' operator on SensorReading:
        // GET /api/telemetry/aggregate?a=ESP32-02&b=ESP32-05
        group.MapGet("/aggregate", (string a, string b, ITelemetryStore store) =>
        {
            try
            {
                var aggregate = store.Aggregate(a, b);
                return Results.Ok(new
                {
                    aggregate.SensorId,
                    aggregate.Value,
                    aggregate.Unit,
                    aggregate.TimestampUtc
                });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // Demonstrates the jagged-array batch buffer being flattened into a List<T>.
        group.MapPost("/seed-batch", (int? devices, int? samplesPerDevice) =>
        {
            var deviceCount = devices ?? 4;
            var sampleCount = samplesPerDevice ?? 5;

            var deviceIds = Enumerable.Range(1, deviceCount).Select(i => $"BATCH-{i:D2}").ToArray();
            var buffer = new TelemetryBatchBuffer(deviceIds);
            var random = new Random();

            for (var d = 0; d < deviceIds.Length; d++)
            {
                // Jagged on purpose: each device gets a different sample count,
                // simulating uneven duty cycles across the mesh.
                var thisDeviceSampleCount = sampleCount + random.Next(-2, 3);
                thisDeviceSampleCount = Math.Max(1, thisDeviceSampleCount);

                var samples = Enumerable.Range(0, thisDeviceSampleCount)
                    .Select(_ => Math.Round(20 + random.NextDouble() * 15, 2))
                    .ToArray();

                buffer.AppendBatch(d, samples);
            }

            var packets = buffer.FlattenToPackets("% moisture");

            return Results.Ok(new
            {
                rowLengths = buffer.RowLengths(),
                packetCount = packets.Count,
                sample = packets.Take(10).Select(p => p.ToDisplayString())
            });
        });
    }
}
