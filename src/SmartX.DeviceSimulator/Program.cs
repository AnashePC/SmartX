using System.Net.Http.Json;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

// -----------------------------------------------------------------------------------------------
// Smart-X device simulator — a stand-alone "fleet" of fake ESP32 nodes that talks to the gateway
// over plain HTTP, exactly like real hardware would (POST /api/telemetry).
//
//   dotnet run --project src/SmartX.DeviceSimulator -- [options]
//
//   --api <url>        Gateway base URL                         (default http://localhost:5000)
//   --devices <n>      Number of simulated nodes                (default 4)
//   --interval <ms>    Delay between rounds                     (default 1000)
//   --spike <0..1>     Chance a reading is a critical spike     (default 0.05)
//   --storm <n>        Send n routine packets in one go, then ONE critical packet, then exit
// -----------------------------------------------------------------------------------------------

var settings = ParseArguments(args);
using var http = new HttpClient { BaseAddress = new Uri(settings.Api), Timeout = TimeSpan.FromSeconds(5) };
var random = new Random();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Smart-X device simulator -> {settings.Api}");

if (settings.Storm > 0)
{
    await RunStormAsync(settings.Storm);
    return;
}

Console.WriteLine($"{settings.Devices} node(s), one reading each every {settings.Interval} ms, spike chance {settings.Spike:P0}. Ctrl+C to stop.");

while (!cts.IsCancellationRequested)
{
    for (var i = 1; i <= settings.Devices && !cts.IsCancellationRequested; i++)
    {
        var spike = random.NextDouble() < settings.Spike;
        var useMoisture = i % 2 == 1;

        var request = useMoisture
            ? new TelemetryIngestRequest
            {
                SensorId = $"SIM-{i:D2}",
                Kind = TelemetryValueKind.Float,
                NumericValue = spike ? 8 + random.NextDouble() * 6 : 35 + random.NextDouble() * 5,
                Unit = "% moisture"
            }
            : new TelemetryIngestRequest
            {
                SensorId = $"SIM-{i:D2}",
                Kind = TelemetryValueKind.Integer,
                NumericValue = spike ? 900 + random.Next(200) : 300 + random.Next(50),
                Unit = "W"
            };

        await SendAsync(request);
    }

    try
    {
        await Task.Delay(settings.Interval, cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

Console.WriteLine("Stopped.");

// ---------------------------------------------------------------------------------------------

async Task RunStormAsync(int count)
{
    Console.WriteLine($"Storm: {count} routine packets, then one critical packet behind them...");

    for (var i = 0; i < count; i++)
    {
        await PostAsync(new TelemetryIngestRequest
        {
            SensorId = $"SIM-{i % 4 + 1:D2}",
            Kind = TelemetryValueKind.Integer,
            NumericValue = 300 + random.Next(50),
            Unit = "W"
        });
    }

    var ack = await PostAsync(new TelemetryIngestRequest
    {
        SensorId = "SIM-02",
        Kind = TelemetryValueKind.Integer,
        NumericValue = 1100,
        Unit = "W"
    });

    if (ack is not null)
    {
        Console.WriteLine($"Critical packet #{ack.Sequence} accepted into the {ack.Lane} lane " +
                          $"(standard backlog {ack.StandardQueueDepth}, priority backlog {ack.PriorityQueueDepth}).");
        Console.WriteLine("Check GET /api/pipeline/recent — it was queued last but processed first.");
    }
}

async Task SendAsync(TelemetryIngestRequest request)
{
    var ack = await PostAsync(request);
    if (ack is null) return;

    var lane = ack.Lane == AlertPriority.Critical ? "PRIORITY" : "standard";
    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {request.SensorId}  {request.NumericValue,8:F1} {request.Unit,-11} -> {lane,-8} (std {ack.StandardQueueDepth}, pri {ack.PriorityQueueDepth})");
}

async Task<IngestAck?> PostAsync(TelemetryIngestRequest request)
{
    try
    {
        var response = await http.PostAsJsonAsync("/api/telemetry", request);
        return await response.Content.ReadFromJsonAsync<IngestAck>();
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"Gateway unreachable ({ex.Message}). Is SmartX.Api running?");
        return null;
    }
}

static Settings ParseArguments(string[] args)
{
    var api = "http://localhost:5000";
    var devices = 4;
    var interval = 1000;
    var spike = 0.05;
    var storm = 0;

    for (var i = 0; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--api": api = args[++i]; break;
            case "--devices": devices = int.Parse(args[++i]); break;
            case "--interval": interval = int.Parse(args[++i]); break;
            case "--spike": spike = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
            case "--storm": storm = int.Parse(args[++i]); break;
        }
    }

    return new Settings(api, Math.Max(1, devices), Math.Max(50, interval), Math.Clamp(spike, 0, 1), Math.Max(0, storm));
}

internal sealed record Settings(string Api, int Devices, int Interval, double Spike, int Storm);
