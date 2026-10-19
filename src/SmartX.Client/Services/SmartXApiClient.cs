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

    public async Task<TelemetryFeedItem?> IngestTelemetryAsync(TelemetryIngestRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/telemetry", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TelemetryFeedItem>();
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
}

/// <summary>Mirrors the anonymous JSON shape returned by GET /api/telemetry/aggregate.</summary>
public sealed record AggregateResult(string SensorId, double Value, string Unit, DateTime TimestampUtc);

/// <summary>Mirrors the anonymous JSON shape returned by POST /api/telemetry/seed-batch.</summary>
public sealed record BatchSeedResult(int[] RowLengths, int PacketCount, List<string> Sample);
