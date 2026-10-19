using System.Net.Http.Json;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

public sealed record ApiOptions(string BaseUrl);

/// <summary>Thin typed wrapper around SmartX.Api so Razor pages stay free of raw HttpClient plumbing.</summary>
public sealed class SmartXApiClient
{
    private readonly HttpClient _http;

    public SmartXApiClient(HttpClient http)
    {
        _http = http;
    }

    // ---------------------------------------------------------------- Part 1

    public async Task<List<SensorRegistration>> GetSensorsAsync() =>
        await _http.GetFromJsonAsync<List<SensorRegistration>>("/api/sensors") ?? new();

    public async Task<SensorRegistration?> RegisterSensorAsync(SensorRegistrationRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/sensors", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SensorRegistration>();
    }

    public async Task<bool> UploadFileAsync(Guid sensorId, MultipartFormDataContent content)
    {
        var response = await _http.PostAsync($"/api/sensors/{sensorId}/upload", content);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Part 2: the API now answers 202 Accepted with the lane the packet was queued in.</summary>
    public async Task<IngestAck?> IngestTelemetryAsync(TelemetryIngestRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/telemetry", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IngestAck>();
    }

    public async Task<List<TelemetryFeedItem>> GetFeedAsync(int count = 50) =>
        await _http.GetFromJsonAsync<List<TelemetryFeedItem>>($"/api/telemetry/feed?count={count}") ?? new();

    public async Task<DeploymentNode?> GetSampleDeploymentAsync() =>
        await _http.GetFromJsonAsync<DeploymentNode>("/api/deployment/sample");

    public async Task<ValidationResult?> ValidateDeploymentAsync(DeploymentNode root)
    {
        var response = await _http.PostAsJsonAsync("/api/deployment/validate", root);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ValidationResult>();
    }

    public async Task<BatchSeedResult?> SeedBatchAsync(int devices, int samplesPerDevice)
    {
        var response = await _http.PostAsync(
            $"/api/telemetry/seed-batch?devices={devices}&samplesPerDevice={samplesPerDevice}", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BatchSeedResult>();
    }

    public async Task<AggregateResult?> GetAggregateAsync(string sensorIdA, string sensorIdB)
    {
        var response = await _http.GetAsync($"/api/telemetry/aggregate?a={Uri.EscapeDataString(sensorIdA)}&b={Uri.EscapeDataString(sensorIdB)}");
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(errorText);
        }

        return await response.Content.ReadFromJsonAsync<AggregateResult>();
    }

    // ---------------------------------------------------------------- Part 2: reads

    public async Task<List<DeviceState>> GetDevicesAsync() =>
        await _http.GetFromJsonAsync<List<DeviceState>>("/api/devices") ?? new();

    public async Task<List<TimelinePoint>> GetTimelineAsync(string deviceId, int take = 60) =>
        await _http.GetFromJsonAsync<List<TimelinePoint>>(
            $"/api/devices/{Uri.EscapeDataString(deviceId)}/timeline?take={take}") ?? new();

    public async Task<List<TimelinePoint>> GetHistoryAsync(int take = 15) =>
        await _http.GetFromJsonAsync<List<TimelinePoint>>($"/api/history?take={take}") ?? new();

    public async Task<List<ActiveAlert>> GetAlertsAsync() =>
        await _http.GetFromJsonAsync<List<ActiveAlert>>("/api/alerts") ?? new();

    public async Task<PipelineStats?> GetStatsAsync() =>
        await _http.GetFromJsonAsync<PipelineStats>("/api/pipeline/stats");

    public async Task<CommandHistorySnapshot> GetCommandsAsync() =>
        await _http.GetFromJsonAsync<CommandHistorySnapshot>("/api/commands") ?? new();

    public async Task<SuggestionsSnapshot?> GetSuggestionsAsync() =>
        await _http.GetFromJsonAsync<SuggestionsSnapshot>("/api/suggestions");

    // ---------------------------------------------------------------- Part 2: commands & activity

    public Task<CommandResult> IssueCommandAsync(CommandAction action, string deviceId) =>
        SendCommandAsync("/api/commands", new IssueCommandRequest { Action = action, DeviceId = deviceId });

    public Task<CommandResult> UndoLastCommandAsync() => SendCommandAsync("/api/commands/undo", null);

    private async Task<CommandResult> SendCommandAsync(string url, IssueCommandRequest? body)
    {
        var response = body is null
            ? await _http.PostAsync(url, content: null)
            : await _http.PostAsJsonAsync(url, body);

        // 400 responses still carry a CommandResult explaining why (unknown device, nothing to undo...).
        var result = await response.Content.ReadFromJsonAsync<CommandResult>();
        return result ?? new CommandResult { Success = false, Message = $"Unexpected response ({(int)response.StatusCode})." };
    }

    public async Task RecordSearchAsync(string query)
    {
        var response = await _http.PostAsJsonAsync("/api/activity/search", new SearchActivityRequest { Query = query });
        response.EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------------- Part 2: simulation hooks

    public async Task<IngestAck?> SimulateCriticalAsync(string kind, string deviceId)
    {
        var response = await _http.PostAsync(
            $"/api/simulation/critical?kind={Uri.EscapeDataString(kind)}&deviceId={Uri.EscapeDataString(deviceId)}", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IngestAck>();
    }

    public async Task<BurstResult?> SimulateBurstAsync(int count)
    {
        var response = await _http.PostAsync($"/api/simulation/burst?count={count}", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BurstResult>();
    }

    public async Task<DisconnectResult?> ToggleDisconnectAsync(string deviceId)
    {
        var response = await _http.PostAsync($"/api/simulation/disconnect/{Uri.EscapeDataString(deviceId)}", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DisconnectResult>();
    }
}

/// <summary>Mirrors the anonymous JSON shape returned by GET /api/telemetry/aggregate.</summary>
public sealed record AggregateResult(string SensorId, double Value, string Unit, DateTime TimestampUtc);

/// <summary>Mirrors the anonymous JSON shape returned by POST /api/telemetry/seed-batch.</summary>
public sealed record BatchSeedResult(int[] RowLengths, int PacketCount, List<string> Sample);

/// <summary>Mirrors the JSON returned by POST /api/simulation/burst.</summary>
public sealed record BurstResult(int RoutinePacketsQueued, long CriticalPacketSequence, int StandardQueueDepth,
    int PriorityQueueDepth, string Note);

/// <summary>Mirrors the JSON returned by POST /api/simulation/disconnect/{id}.</summary>
public sealed record DisconnectResult(string DeviceId, bool Silent);
