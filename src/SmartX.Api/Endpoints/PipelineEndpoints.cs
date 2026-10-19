using SmartX.Api.Services;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints;

public static class PipelineEndpoints
{
    public static void MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Pipeline");

        // Queue depths, per-lane counters, average wait per lane, duplicates suppressed...
        group.MapGet("/pipeline/stats", (IGatewayStats stats) => Results.Ok(stats.Build()));

        // The order packets were ACTUALLY processed in, with lane and queue wait — the proof that the
        // priority lane bypasses the standard queue.
        group.MapGet("/pipeline/recent", (int? count, ITelemetryPipeline pipeline) =>
            Results.Ok(pipeline.GetRecentlyProcessed(Math.Clamp(count ?? 25, 1, 100))));

        // Currently active (de-duplicated) alerts — the contents of the HashSet.
        group.MapGet("/alerts", (IAlertTracker alerts) => Results.Ok(alerts.Snapshot()));
    }
}
