using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices");

        // GET /api/devices?query=pump&category=2&status=2
        // Search by id / name / location and filter by operational category and alert state.
        group.MapGet("/", (string? query, SensorCategory? category, DeviceStatus? status, IDeviceStateStore devices) =>
            Results.Ok(devices.Query(query, category, status)));

        // O(1) dictionary lookup by MAC address / node id.
        group.MapGet("/{id}", (string id, IDeviceStateStore devices) =>
            devices.TryGetDevice(id, out var device) ? Results.Ok(device) : Results.NotFound());

        // Timeline served from the per-sensor SortedDictionary (already in timestamp order).
        group.MapGet("/{id}/timeline", (string id, int? take, ITimelineStore timeline) =>
            Results.Ok(timeline.GetTimeline(id, Math.Clamp(take ?? 60, 1, 500))));

        // Mesh-wide sensor log, newest first (SortedDictionary keyed by timestamp).
        app.MapGet("/api/history", (int? take, ITimelineStore timeline) =>
            Results.Ok(timeline.GetGlobalLog(Math.Clamp(take ?? 50, 1, 500))))
            .WithTags("Devices");
    }
}
