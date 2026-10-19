using Microsoft.AspNetCore.SignalR.Client;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

/// <summary>
/// Wraps the SignalR connection to /hubs/telemetry so the console gets pushed updates instead of polling.
/// It exposes one event per message the gateway publishes. GatewayState is the only subscriber; pages read
/// from GatewayState, so navigating between pages never drops or duplicates a subscription.
/// </summary>
public sealed class TelemetryHubClient : IAsyncDisposable
{
    private readonly ApiOptions _options;
    private HubConnection? _connection;

    public event Action<TelemetryFeedItem>? OnTelemetryReceived;
    public event Action<DeviceState>? OnDeviceChanged;
    public event Action<ActiveAlert>? OnAlertRaised;
    public event Action<ActiveAlert>? OnAlertCleared;
    public event Action<PipelineStats>? OnStats;
    public event Action<CommandHistorySnapshot>? OnCommandsChanged;
    public event Action<SuggestionsSnapshot>? OnSuggestions;

    public event Action? OnReconnecting;
    public event Action? OnReconnected;
    public event Action? OnClosed;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public TelemetryHubClient(ApiOptions options)
    {
        _options = options;
    }

    /// <summary>Connects (no-op if already connected). Throws if the API is unreachable so the caller can retry.</summary>
    public async Task StartAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        var connection = new HubConnectionBuilder()
            .WithUrl($"{_options.BaseUrl.TrimEnd('/')}/hubs/telemetry")
            .WithAutomaticReconnect()
            .Build();

        connection.On<TelemetryFeedItem>("TelemetryReceived", item => OnTelemetryReceived?.Invoke(item));
        connection.On<DeviceState>("DeviceChanged", device => OnDeviceChanged?.Invoke(device));
        connection.On<ActiveAlert>("AlertRaised", alert => OnAlertRaised?.Invoke(alert));
        connection.On<ActiveAlert>("AlertCleared", alert => OnAlertCleared?.Invoke(alert));
        connection.On<PipelineStats>("PipelineStats", stats => OnStats?.Invoke(stats));
        connection.On<CommandHistorySnapshot>("CommandsChanged", snapshot => OnCommandsChanged?.Invoke(snapshot));
        connection.On<SuggestionsSnapshot>("SuggestionsUpdated", snapshot => OnSuggestions?.Invoke(snapshot));

        connection.Reconnecting += _ =>
        {
            OnReconnecting?.Invoke();
            return Task.CompletedTask;
        };

        connection.Reconnected += _ =>
        {
            OnReconnected?.Invoke();
            return Task.CompletedTask;
        };

        connection.Closed += _ =>
        {
            OnClosed?.Invoke();
            return Task.CompletedTask;
        };

        await connection.StartAsync();
        _connection = connection;
    }

    /// <summary>Drops the current connection so StartAsync can build a fresh one (used after the link is lost for good).</summary>
    public async Task ResetAsync()
    {
        var connection = _connection;
        _connection = null;

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await ResetAsync();
}
