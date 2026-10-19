using Microsoft.AspNetCore.SignalR.Client;
using SmartX.Shared.Dtos;

namespace SmartX.Client.Services;

/// <summary>
/// Wraps the SignalR connection to /hubs/telemetry so the dashboard gets
/// pushed updates instead of polling — the backbone of the real-time
/// engagement feature.
/// </summary>
public sealed class TelemetryHubClient : IAsyncDisposable
{
    private readonly ApiOptions _options;
    private HubConnection? _connection;

    public event Action<TelemetryFeedItem>? OnTelemetryReceived;
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public TelemetryHubClient(ApiOptions options)
    {
        _options = options;
    }

    public async Task StartAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_options.BaseUrl.TrimEnd('/')}/hubs/telemetry")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<TelemetryFeedItem>("TelemetryReceived", item => OnTelemetryReceived?.Invoke(item));

        await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
