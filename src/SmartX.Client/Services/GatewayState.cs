using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Client.Services;

public enum ConnectionStatus
{
    Connecting,
    Connected,
    Reconnecting,
    Offline
}

/// <summary>Sensor-registration form values — kept here so they survive navigating away and back.</summary>
public sealed class RegistrationDraft
{
    public string MacAddress { get; set; } = string.Empty;
    public string DeploymentLocation { get; set; } = string.Empty;
    public SensorCategory Category { get; set; } = SensorCategory.Environmental;
    public string SelectedSensorId { get; set; } = string.Empty;
}

/// <summary>Manual-reading and aggregate form values for the Part 1 telemetry dashboard.</summary>
public sealed class ManualReadingDraft
{
    public string SensorId { get; set; } = "ESP32-01";
    public TelemetryValueKind Kind { get; set; } = TelemetryValueKind.Float;
    public double Numeric { get; set; } = 42;
    public bool Bool { get; set; }
    public string AggregateA { get; set; } = "ESP32-02";
    public string AggregateB { get; set; } = "ESP32-05";
}

/// <summary>Everything the Command Stream page remembers: filters, selection and the override form.</summary>
public sealed class CommandStreamUiState
{
    public string Search { get; set; } = string.Empty;
    public SensorCategory? Category { get; set; }
    public DeviceStatus? Status { get; set; }
    public string SelectedDeviceId { get; set; } = string.Empty;
    public string CommandDeviceId { get; set; } = string.Empty;
    public CommandAction CommandAction { get; set; } = CommandAction.TurnOn;
}

/// <summary>
/// App-wide client state. Blazor destroys a page component the moment you navigate away from it, so anything
/// kept in a page's fields is lost. This service is registered once for the whole app, owns the single SignalR
/// subscription, and holds the live data (feed, devices, alerts, commands, suggestions, heartbeat/streak/badges)
/// plus each page's form and filter state. Pages just render it, which is why navigating between the Part 1
/// and Part 2 modules never loses state, never re-subscribes, and never hammers the API.
/// </summary>
public sealed class GatewayState : IAsyncDisposable
{
    private const int FeedLimit = 60;
    private static readonly int[] BadgeMilestones = { 10, 25, 50, 100 };

    private readonly SmartXApiClient _api;
    private readonly TelemetryHubClient _hub;
    private readonly HashSet<int> _awardedMilestones = new();
    private bool _started;
    private bool _disposed;
    private bool _notifyScheduled;

    public GatewayState(SmartXApiClient api, TelemetryHubClient hub)
    {
        _api = api;
        _hub = hub;
    }

    /// <summary>Raised (coalesced to at most ~6 times a second) whenever anything above changes.</summary>
    public event Action? Changed;

    // ---------------------------------------------------------------- connection
    public ConnectionStatus Connection { get; private set; } = ConnectionStatus.Connecting;
    public string? LastError { get; private set; }

    // ---------------------------------------------------------------- live data
    public List<TelemetryFeedItem> Feed { get; private set; } = new();
    public Dictionary<string, DeviceState> Devices { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ActiveAlert> Alerts { get; private set; } = new();
    public PipelineStats? Stats { get; private set; }
    public CommandHistorySnapshot Commands { get; private set; } = new();
    public SuggestionsSnapshot? Suggestions { get; private set; }

    // ---------------------------------------------------------------- Anomaly Heartbeat (Part 1 engagement feature)
    public bool LastWasAnomaly { get; private set; }
    public string LastSensorId { get; private set; } = string.Empty;
    public int AnomalyFreeStreak { get; private set; }
    public List<string> EarnedBadges { get; } = new();
    public string? ToastMessage { get; private set; }

    // ---------------------------------------------------------------- per-page UI state
    public RegistrationDraft Registration { get; } = new();
    public ManualReadingDraft ManualReading { get; } = new();
    public CommandStreamUiState Ui { get; } = new();

    /// <summary>Lets a component ask every subscriber to re-render after it edits UI state.</summary>
    public void NotifyUiChanged() => NotifyChanged();

    // ================================================================ startup & connection handling

    public Task EnsureStartedAsync()
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;

        _hub.OnTelemetryReceived += HandleTelemetry;
        _hub.OnDeviceChanged += device => { Devices[device.DeviceId] = device; NotifyChanged(); };
        _hub.OnAlertRaised += alert => { UpsertAlert(alert); NotifyChanged(); };
        _hub.OnAlertCleared += alert => { Alerts.RemoveAll(a => a.Key == alert.Key); NotifyChanged(); };
        _hub.OnStats += stats => { Stats = stats; NotifyChanged(); };
        _hub.OnCommandsChanged += snapshot => { Commands = snapshot; NotifyChanged(); };
        _hub.OnSuggestions += snapshot => { Suggestions = snapshot; NotifyChanged(); };

        _hub.OnReconnecting += () => SetConnection(ConnectionStatus.Reconnecting);
        _hub.OnReconnected += () => _ = RecoverAfterReconnectAsync();
        _hub.OnClosed += () => _ = RestartAfterCloseAsync();

        _ = ConnectLoopAsync();
        return Task.CompletedTask;
    }

    private async Task ConnectLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                SetConnection(ConnectionStatus.Connecting);
                await LoadSnapshotAsync();
                await _hub.StartAsync();
                LastError = null;
                SetConnection(ConnectionStatus.Connected);
                return;
            }
            catch (Exception ex)
            {
                // API not up yet (or restarting): show the banner and keep trying instead of crashing the UI.
                LastError = ex.Message;
                SetConnection(ConnectionStatus.Offline);
                await Task.Delay(4000);
            }
        }
    }

    private async Task RecoverAfterReconnectAsync()
    {
        try
        {
            await LoadSnapshotAsync(); // anything pushed while we were disconnected was missed
            SetConnection(ConnectionStatus.Connected);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SetConnection(ConnectionStatus.Offline);
        }
    }

    private async Task RestartAfterCloseAsync()
    {
        if (_disposed) return;

        SetConnection(ConnectionStatus.Offline);
        await _hub.ResetAsync();
        _ = ConnectLoopAsync();
    }

    private async Task LoadSnapshotAsync()
    {
        var devicesTask = _api.GetDevicesAsync();
        var alertsTask = _api.GetAlertsAsync();
        var commandsTask = _api.GetCommandsAsync();
        var suggestionsTask = _api.GetSuggestionsAsync();
        var statsTask = _api.GetStatsAsync();
        var feedTask = _api.GetFeedAsync(30);

        await Task.WhenAll(devicesTask, alertsTask, commandsTask, suggestionsTask, statsTask, feedTask);

        Devices = devicesTask.Result.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
        Alerts = alertsTask.Result;
        Commands = commandsTask.Result;
        Suggestions = suggestionsTask.Result;
        Stats = statsTask.Result;
        Feed = feedTask.Result;
        NotifyChanged();
    }

    private void SetConnection(ConnectionStatus status)
    {
        Connection = status;
        NotifyChanged();
    }

    // ================================================================ event handlers

    private void HandleTelemetry(TelemetryFeedItem item)
    {
        LastWasAnomaly = item.IsAnomaly;
        LastSensorId = string.IsNullOrEmpty(item.DeviceName) ? item.SensorId : item.DeviceName;

        if (item.IsAnomaly)
        {
            AnomalyFreeStreak = 0;
        }
        else
        {
            AnomalyFreeStreak++;
            CheckForNewBadge();
        }

        Feed.Insert(0, item);
        if (Feed.Count > FeedLimit)
        {
            Feed.RemoveAt(Feed.Count - 1);
        }

        NotifyChanged();
    }

    private void UpsertAlert(ActiveAlert alert)
    {
        var index = Alerts.FindIndex(a => a.Key == alert.Key);
        if (index >= 0)
        {
            Alerts[index] = alert;
        }
        else
        {
            Alerts.Insert(0, alert);
        }
    }

    private void CheckForNewBadge()
    {
        foreach (var milestone in BadgeMilestones)
        {
            if (AnomalyFreeStreak == milestone && _awardedMilestones.Add(milestone))
            {
                var badgeName = milestone switch
                {
                    10 => "🥉 Bronze Streak",
                    25 => "🥈 Silver Streak",
                    50 => "🥇 Gold Streak",
                    _ => "💎 Platinum Streak"
                };

                EarnedBadges.Add(badgeName);
                ToastMessage = $"{badgeName} — {milestone} readings anomaly-free!";
                _ = ClearToastAfterDelayAsync();
            }
        }
    }

    private async Task ClearToastAfterDelayAsync()
    {
        await Task.Delay(4000);
        ToastMessage = null;
        NotifyChanged();
    }

    // ================================================================ change notification (coalesced)

    private void NotifyChanged()
    {
        if (_notifyScheduled || _disposed)
        {
            return;
        }

        _notifyScheduled = true;
        _ = FlushAsync();
    }

    private async Task FlushAsync()
    {
        // Telemetry can arrive many times a second; batching re-renders keeps the UI responsive.
        await Task.Delay(150);
        _notifyScheduled = false;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _hub.DisposeAsync();
    }
}
