using Microsoft.AspNetCore.SignalR;

namespace SmartX.Api.Hubs;

/// <summary>
/// Pushes live telemetry + anomaly events to the Blazor dashboard so the
/// "Anomaly Heartbeat" engagement widget can react in real time instead of
/// polling. Clients don't call anything on this hub directly today — it's a
/// broadcast channel — but it's kept as a full Hub (rather than a bare
/// endpoint) so Part 2's Real-Time Command Stream can add client-to-server
/// methods here without reshaping the transport.
/// </summary>
public sealed class TelemetryHub : Hub
{
    public override Task OnConnectedAsync()
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, "dashboard-viewers");
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, "dashboard-viewers");
    }
}
