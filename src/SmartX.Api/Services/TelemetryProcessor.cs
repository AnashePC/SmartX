namespace SmartX.Api.Services;

/// <summary>
/// The consumer side of the pipeline. A single loop drains the queues through
/// <see cref="ITelemetryPipeline.TryDequeue"/>, which always serves the critical lane first.
///
/// <c>SmartX:Pipeline:ProcessingDelayMilliseconds</c> models the per-packet cost of a constrained gateway.
/// With it set, a burst of routine packets builds a visible backlog in the standard queue — and a critical
/// packet that arrives behind that backlog is still processed next, which is the behaviour to demonstrate.
/// </summary>
public sealed class TelemetryProcessor : BackgroundService
{
    private readonly ITelemetryPipeline _pipeline;
    private readonly ITelemetryStore _store;
    private readonly ILogger<TelemetryProcessor> _logger;
    private readonly int _delayMs;

    public TelemetryProcessor(ITelemetryPipeline pipeline, ITelemetryStore store, IConfiguration config,
        ILogger<TelemetryProcessor> logger)
    {
        _pipeline = pipeline;
        _store = store;
        _logger = logger;
        _delayMs = Math.Max(0, config.GetValue("SmartX:Pipeline:ProcessingDelayMilliseconds", 20));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telemetry processor started (per-packet delay {Delay} ms).", _delayMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_pipeline.TryDequeue(out var packet))
            {
                await _pipeline.WaitForWorkAsync(stoppingToken);
                continue;
            }

            try
            {
                await _store.ProcessAsync(packet, stoppingToken);
                _pipeline.MarkProcessed(packet);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad packet must never stop the gateway.
                _logger.LogError(ex, "Failed to process packet #{Sequence} from {Sensor}.",
                    packet.Sequence, packet.Request.SensorId);
            }

            if (_delayMs > 0)
            {
                try
                {
                    await Task.Delay(_delayMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
