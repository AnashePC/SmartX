using System.Diagnostics.CodeAnalysis;
using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>A packet sitting in one of the two lanes, waiting to be processed.</summary>
public sealed class QueuedPacket
{
    public long Sequence { get; init; }
    public TelemetryIngestRequest Request { get; init; } = new();
    public Classification Classification { get; init; } = Classification.Normal;
    public DateTime EnqueuedUtc { get; init; }
    public DateTime DequeuedUtc { get; set; }

    public AlertPriority Lane => Classification.Priority;
    public double WaitMs => Math.Max(0, (DequeuedUtc - EnqueuedUtc).TotalMilliseconds);
}

/// <summary>One row of the "what was processed, in which order" log (GET /api/pipeline/recent).</summary>
public sealed class ProcessedPacketInfo
{
    public long ProcessedOrder { get; init; }
    public long Sequence { get; init; }
    public string SensorId { get; init; } = string.Empty;
    public AlertPriority Lane { get; init; }
    public double WaitMs { get; init; }
    public DateTime ProcessedUtc { get; init; }
}

public sealed record PipelineCounters(
    int StandardDepth,
    int PriorityDepth,
    long Enqueued,
    long Processed,
    long StandardProcessed,
    long PriorityProcessed,
    long Rejected,
    double AverageStandardWaitMs,
    double AveragePriorityWaitMs);

public interface ITelemetryPipeline
{
    IngestAck Enqueue(TelemetryIngestRequest request);
    bool TryDequeue([NotNullWhen(true)] out QueuedPacket? packet);
    Task WaitForWorkAsync(CancellationToken ct);
    void MarkProcessed(QueuedPacket packet);
    IReadOnlyList<ProcessedPacketInfo> GetRecentlyProcessed(int count);
    PipelineCounters GetCounters();
}

/// <summary>
/// The two-lane ingestion pipeline at the heart of Part 2.
///
///   Standard lane : Queue&lt;QueuedPacket&gt;  — plain FIFO for routine telemetry.
///   Critical lane : PriorityQueue&lt;QueuedPacket, (Rank, Sequence)&gt; — severe power spikes and
///                   critical moisture readings. TryDequeue ALWAYS drains this lane first, so a
///                   critical alert bypasses every routine packet already waiting.
///
/// Inside the critical lane the most severe packet goes first (Rank = -Severity) and ties fall back to
/// arrival order (Sequence), so the priority queue is stable.
/// </summary>
public sealed class TelemetryPipeline : ITelemetryPipeline
{
    private const int RecentLogCapacity = 100;

    private readonly Queue<QueuedPacket> _standard = new();
    private readonly PriorityQueue<QueuedPacket, (int Rank, long Sequence)> _critical = new();
    private readonly Queue<ProcessedPacketInfo> _recent = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly IAlertClassifier _classifier;
    private readonly int _maxStandardDepth;

    private long _sequence;
    private long _enqueued;
    private long _processed;
    private long _standardProcessed;
    private long _priorityProcessed;
    private long _rejected;
    private double _standardWaitTotalMs;
    private double _priorityWaitTotalMs;

    public TelemetryPipeline(IAlertClassifier classifier, IConfiguration config)
    {
        _classifier = classifier;
        _maxStandardDepth = config.GetValue("SmartX:Pipeline:MaxStandardQueueDepth", 5000);
    }

    public IngestAck Enqueue(TelemetryIngestRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SensorId))
        {
            return new IngestAck { Accepted = false, Reason = "SensorId is required." };
        }

        var classification = _classifier.Classify(request);
        IngestAck ack;

        lock (_gate)
        {
            if (classification.Priority == AlertPriority.Normal && _standard.Count >= _maxStandardDepth)
            {
                _rejected++;
                return new IngestAck
                {
                    Accepted = false,
                    Reason = "Standard queue is full (back-pressure) — retry shortly.",
                    StandardQueueDepth = _standard.Count,
                    PriorityQueueDepth = _critical.Count
                };
            }

            var sequence = ++_sequence;
            _enqueued++;

            var packet = new QueuedPacket
            {
                Sequence = sequence,
                Request = request,
                Classification = classification,
                EnqueuedUtc = DateTime.UtcNow
            };

            if (classification.Priority == AlertPriority.Critical)
            {
                // Lower tuple sorts first, so negate severity: most severe -> smallest rank -> dequeued first.
                _critical.Enqueue(packet, (-classification.Severity, sequence));
            }
            else
            {
                _standard.Enqueue(packet);
            }

            ack = new IngestAck
            {
                Accepted = true,
                Lane = classification.Priority,
                Sequence = sequence,
                Reason = classification.Priority == AlertPriority.Critical
                    ? $"CRITICAL — priority lane. {classification.Message}"
                    : "Queued in the standard FIFO lane.",
                StandardQueueDepth = _standard.Count,
                PriorityQueueDepth = _critical.Count
            };
        }

        _signal.Release();
        return ack;
    }

    public bool TryDequeue([NotNullWhen(true)] out QueuedPacket? packet)
    {
        lock (_gate)
        {
            // Critical lane first — this is the "bypass the standard queue" guarantee.
            if (_critical.TryDequeue(out var urgent, out _))
            {
                urgent.DequeuedUtc = DateTime.UtcNow;
                packet = urgent;
                return true;
            }

            if (_standard.TryDequeue(out var routine))
            {
                routine.DequeuedUtc = DateTime.UtcNow;
                packet = routine;
                return true;
            }
        }

        packet = null;
        return false;
    }

    public async Task WaitForWorkAsync(CancellationToken ct)
    {
        try
        {
            await _signal.WaitAsync(TimeSpan.FromMilliseconds(250), ct);
        }
        catch (OperationCanceledException)
        {
            // Shutting down — the caller's loop condition handles it.
        }
    }

    public void MarkProcessed(QueuedPacket packet)
    {
        lock (_gate)
        {
            _processed++;

            if (packet.Lane == AlertPriority.Critical)
            {
                _priorityProcessed++;
                _priorityWaitTotalMs += packet.WaitMs;
            }
            else
            {
                _standardProcessed++;
                _standardWaitTotalMs += packet.WaitMs;
            }

            _recent.Enqueue(new ProcessedPacketInfo
            {
                ProcessedOrder = _processed,
                Sequence = packet.Sequence,
                SensorId = packet.Request.SensorId,
                Lane = packet.Lane,
                WaitMs = Math.Round(packet.WaitMs, 1),
                ProcessedUtc = DateTime.UtcNow
            });

            while (_recent.Count > RecentLogCapacity)
            {
                _recent.Dequeue();
            }
        }
    }

    public IReadOnlyList<ProcessedPacketInfo> GetRecentlyProcessed(int count)
    {
        lock (_gate)
        {
            return _recent.Reverse().Take(count).ToList();
        }
    }

    public PipelineCounters GetCounters()
    {
        lock (_gate)
        {
            return new PipelineCounters(
                _standard.Count,
                _critical.Count,
                _enqueued,
                _processed,
                _standardProcessed,
                _priorityProcessed,
                _rejected,
                _standardProcessed == 0 ? 0 : _standardWaitTotalMs / _standardProcessed,
                _priorityProcessed == 0 ? 0 : _priorityWaitTotalMs / _priorityProcessed);
        }
    }
}
