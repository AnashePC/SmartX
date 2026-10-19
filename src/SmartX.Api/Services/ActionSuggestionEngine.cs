using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface IActionSuggestionEngine
{
    void RecordSearch(string query);
    void RecordCommand(CommandAction action, string deviceId);
    void RecordAlert(ActiveAlert alert);
    SuggestionsSnapshot GetSuggestions();
}

/// <summary>
/// Predictive Action and Recommendation Engine.
///
/// PATTERN ANALYSIS — every operator search, every manual command and every alert the gateway raises is
/// turned into a token ("search:ESP32-03", "cmd:TurnOn:ESP32-03", "alert:ESP32-04:MoistureCriticalLow").
/// A weighted directed graph — <c>Dictionary&lt;token, Dictionary&lt;nextToken, weight&gt;&gt;</c> — counts how often
/// an operator action follows an earlier event within a 10-minute window. It is a first-order Markov
/// chain with a skip-gram: the event immediately before counts 1.0, the one before that counts 0.5. This is
/// how "operator searches for Pump A right after Moisture Sensor B drops below 20%" is learned: that exact
/// sequence strengthens the edge alert:ESP32-04:MoistureCriticalLow -> search:ESP32-03.
///
/// ALGORITHMIC SUGGESTION — when an alert becomes active (or the operator acts) the engine looks up the
/// outgoing edges of the current context, turns weights into probabilities, damps low-evidence edges, merges
/// the scores and ranks candidates in a <see cref="PriorityQueue{TElement, TPriority}"/> (a binary heap) so the top few
/// are available without sorting everything. The result is pushed to the dashboard BEFORE the operator searches.
///
/// AUTOMATED INSIGHTS — a second, rule-and-trend pass over live device state (critical alerts, disconnected
/// nodes, unstable sensors, and a least-squares trend projection that warns when a reading is on course to
/// breach its critical threshold), also ranked with a priority queue.
/// </summary>
public sealed class ActionSuggestionEngine : IActionSuggestionEngine
{
    private sealed record ActionEvent(string Token, DateTime TimestampUtc);

    private static readonly double[] LagWeights = { 1.0, 0.5 };
    private static readonly TimeSpan ContextWindow = TimeSpan.FromMinutes(10);
    private const int MinSupport = 3;
    private const int MaxRecentEvents = 12;
    private const double MinScore = 0.15;
    private const int MaxSuggestions = 4;
    private const int MaxInsights = 5;

    /// <summary>
    /// Simulated historical operator sessions used to bootstrap the model, so the engine already has
    /// something to say on first launch. Live operator activity keeps adding to the same graph.
    /// Device ids match the seeded simulation catalogue (ESP32-04 = Moisture Sensor B, ESP32-03 = Pump A ...).
    /// </summary>
    private static readonly (int Repeats, string[] Sequence)[] SeedHistory =
    {
        (7, new[] { "alert:ESP32-04|MoistureCriticalLow", "search:ESP32-03", "cmd:TurnOn:ESP32-03" }),
        (2, new[] { "alert:ESP32-04|MoistureCriticalLow", "search:ESP32-03" }),
        (4, new[] { "alert:ESP32-01|MoistureCriticalLow", "search:ESP32-06", "cmd:TurnOn:ESP32-06" }),
        (5, new[] { "alert:ESP32-02|PowerSpike", "search:ESP32-08", "cmd:Toggle:ESP32-08" }),
        (3, new[] { "alert:ESP32-05|PowerSpike", "search:ESP32-08", "cmd:TurnOff:ESP32-08" }),
        (3, new[] { "alert:ESP32-04|MoistureCriticalHigh", "cmd:TurnOff:ESP32-03" }),
        (3, new[] { "alert:ESP32-04|Disconnected", "search:ESP32-03" })
    };

    private readonly Dictionary<string, Dictionary<string, double>> _transitions = new(StringComparer.Ordinal);
    private readonly List<ActionEvent> _recent = new();
    private readonly object _gate = new();
    private readonly IDeviceStateStore _devices;
    private readonly IAlertTracker _alerts;
    private readonly ITimelineStore _timeline;
    private readonly double _moistureLow;
    private readonly double _powerCritical;
    private int _observed;
    private int _learnedEdges;

    public ActionSuggestionEngine(IDeviceStateStore devices, IAlertTracker alerts, ITimelineStore timeline,
        IConfiguration config)
    {
        _devices = devices;
        _alerts = alerts;
        _timeline = timeline;
        _moistureLow = config.GetValue("SmartX:AlertRules:MoistureCriticalLow", 20.0);
        _powerCritical = config.GetValue("SmartX:AlertRules:PowerCriticalWatts", 800.0);

        foreach (var (repeats, sequence) in SeedHistory)
        {
            for (var i = 0; i < repeats; i++)
            {
                TrainOnSession(sequence);
            }
        }
    }

    // ------------------------------------------------------------------
    // Recording (learning)
    // ------------------------------------------------------------------

    public void RecordSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        var device = _devices.ResolveByText(query);
        var token = device is not null
            ? $"search:{device.DeviceId}"
            : $"search:{query.Trim().ToLowerInvariant()}";

        Observe(token, isUserAction: true);
    }

    public void RecordCommand(CommandAction action, string deviceId) =>
        Observe($"cmd:{action}:{deviceId}", isUserAction: true);

    public void RecordAlert(ActiveAlert alert) =>
        Observe(AlertToken(alert.DeviceId, alert.Kind), isUserAction: false);

    private void Observe(string token, bool isUserAction)
    {
        var now = DateTime.UtcNow;

        lock (_gate)
        {
            // Only operator actions are ever PREDICTED, so only they are learned as successors.
            // Alerts are context: they sit in the recent list so later actions can be linked to them.
            if (isUserAction)
            {
                var lag = 0;
                for (var i = _recent.Count - 1; i >= 0 && lag < LagWeights.Length; i--)
                {
                    var previous = _recent[i];
                    if (now - previous.TimestampUtc > ContextWindow) break;

                    if (!string.Equals(previous.Token, token, StringComparison.Ordinal))
                    {
                        AddTransition(previous.Token, token, LagWeights[lag]);
                    }

                    lag++;
                }
            }

            _recent.Add(new ActionEvent(token, now));
            if (_recent.Count > MaxRecentEvents) _recent.RemoveAt(0);
            _observed++;
        }
    }

    private void TrainOnSession(IReadOnlyList<string> sequence)
    {
        lock (_gate)
        {
            for (var i = 0; i < sequence.Count; i++)
            {
                _observed++;
                if (IsAlertToken(sequence[i])) continue;

                for (var lag = 0; lag < LagWeights.Length; lag++)
                {
                    var from = i - lag - 1;
                    if (from < 0) break;
                    AddTransition(sequence[from], sequence[i], LagWeights[lag]);
                }
            }
        }
    }

    private void AddTransition(string from, string to, double weight)
    {
        if (!_transitions.TryGetValue(from, out var edges))
        {
            edges = new Dictionary<string, double>(StringComparer.Ordinal);
            _transitions[from] = edges;
        }

        if (!edges.TryAdd(to, weight))
        {
            edges[to] += weight;
        }
        else
        {
            _learnedEdges++;
        }
    }

    // ------------------------------------------------------------------
    // Producing suggestions
    // ------------------------------------------------------------------

    public SuggestionsSnapshot GetSuggestions()
    {
        var now = DateTime.UtcNow;
        var activeAlerts = _alerts.Snapshot();
        var devices = _devices.GetAll();
        var byId = devices.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);

        var actions = BuildSuggestedActions(now, activeAlerts, byId, out var contextLabels);
        var insights = BuildInsights(activeAlerts, devices, now);

        int observed;
        int edges;
        lock (_gate)
        {
            observed = _observed;
            edges = _learnedEdges;
        }

        return new SuggestionsSnapshot
        {
            Actions = actions,
            Insights = insights,
            GeneratedUtc = now,
            ObservedEvents = observed,
            LearnedTransitions = edges,
            ContextSummary = contextLabels.Count == 0
                ? "No active alerts — the engine is watching operator activity and device trends."
                : "Reacting to: " + string.Join("; ", contextLabels)
        };
    }

    private List<SuggestedAction> BuildSuggestedActions(
        DateTime now,
        IReadOnlyList<ActiveAlert> activeAlerts,
        Dictionary<string, DeviceState> byId,
        out List<string> contextLabels)
    {
        contextLabels = new List<string>();
        var contexts = new List<(string Token, double Weight, string Label)>();

        foreach (var alert in activeAlerts)
        {
            var label = $"{alert.DeviceName}: {Humanise(alert.Kind)}";
            contexts.Add((AlertToken(alert.DeviceId, alert.Kind), 1.0, label));
            contextLabels.Add(label);
        }

        var scores = new Dictionary<string, (double Score, double Evidence, string Label, double Best)>(StringComparer.Ordinal);

        lock (_gate)
        {
            var lastAction = _recent.LastOrDefault(e => !IsAlertToken(e.Token) && now - e.TimestampUtc <= ContextWindow);
            if (lastAction is not null)
            {
                var label = $"you just did \"{DescribeToken(lastAction.Token, byId)}\"";
                contexts.Add((lastAction.Token, 0.6, label));
                contextLabels.Add(label);
            }

            foreach (var (token, weight, label) in contexts)
            {
                if (!_transitions.TryGetValue(token, out var next)) continue;

                var total = next.Values.Sum();
                var support = Math.Min(1.0, total / MinSupport); // damp edges backed by little evidence

                foreach (var (nextToken, edgeWeight) in next)
                {
                    var probability = edgeWeight / total;
                    var contribution = weight * probability * support;

                    if (scores.TryGetValue(nextToken, out var existing))
                    {
                        var bestLabel = contribution > existing.Best ? label : existing.Label;
                        scores[nextToken] = (existing.Score + contribution,
                            Math.Max(existing.Evidence, edgeWeight),
                            bestLabel,
                            Math.Max(existing.Best, contribution));
                    }
                    else
                    {
                        scores[nextToken] = (contribution, edgeWeight, label, contribution);
                    }
                }
            }
        }

        // Rank with a binary heap: highest score pops first.
        var heap = new PriorityQueue<string, double>();
        foreach (var (token, entry) in scores)
        {
            if (entry.Score >= MinScore) heap.Enqueue(token, -entry.Score);
        }

        var results = new List<SuggestedAction>();
        while (heap.Count > 0 && results.Count < MaxSuggestions)
        {
            var token = heap.Dequeue();
            var entry = scores[token];
            var suggestion = BuildSuggestion(token, entry.Score, entry.Evidence, entry.Label, byId);
            if (suggestion is not null) results.Add(suggestion);
        }

        return results;
    }

    private static SuggestedAction? BuildSuggestion(string token, double score, double evidence, string label,
        Dictionary<string, DeviceState> byId)
    {
        var confidence = Math.Min(0.99, score);
        var percent = (int)Math.Round(confidence * 100);
        var times = Math.Max(1, (int)Math.Round(evidence));
        var rationale = $"After {label}, this followed about {times} time{(times == 1 ? "" : "s")} before — {percent}% confidence.";

        if (token.StartsWith("cmd:", StringComparison.Ordinal))
        {
            var parts = token.Split(':', 3);
            if (parts.Length < 3 || !Enum.TryParse<CommandAction>(parts[1], out var action)) return null;

            var deviceId = parts[2];

            if (action == CommandAction.ShutDownAllValves)
            {
                return new SuggestedAction
                {
                    Id = token,
                    Title = "Shut down all valves",
                    Rationale = rationale,
                    Confidence = confidence,
                    Kind = SuggestionKind.Command,
                    DeviceId = "*",
                    Command = action
                };
            }

            if (!byId.TryGetValue(deviceId, out var device) || device.Category != SensorCategory.Actuator) return null;
            if (device.Status == DeviceStatus.Disconnected) return null;

            // Don't suggest something that would change nothing.
            if (action == CommandAction.TurnOn && device.ActuatorOn == true) return null;
            if (action == CommandAction.TurnOff && device.ActuatorOn == false) return null;

            var verb = action switch
            {
                CommandAction.TurnOn => "Turn ON",
                CommandAction.TurnOff => "Turn OFF",
                _ => "Toggle"
            };

            return new SuggestedAction
            {
                Id = token,
                Title = $"{verb} {device.Name}",
                Rationale = rationale,
                Confidence = confidence,
                Kind = SuggestionKind.Command,
                DeviceId = device.DeviceId,
                Command = action
            };
        }

        if (token.StartsWith("search:", StringComparison.Ordinal))
        {
            var target = token["search:".Length..];

            if (byId.TryGetValue(target, out var device))
            {
                return new SuggestedAction
                {
                    Id = token,
                    Title = $"Check {device.Name}",
                    Rationale = rationale,
                    Confidence = confidence,
                    Kind = SuggestionKind.OpenDevice,
                    DeviceId = device.DeviceId
                };
            }

            return new SuggestedAction
            {
                Id = token,
                Title = $"Search \"{target}\"",
                Rationale = rationale,
                Confidence = confidence,
                Kind = SuggestionKind.Investigate
            };
        }

        return null;
    }

    private List<AutomatedInsight> BuildInsights(
        IReadOnlyList<ActiveAlert> activeAlerts,
        IReadOnlyList<DeviceState> devices,
        DateTime now)
    {
        var heap = new PriorityQueue<AutomatedInsight, double>();

        void Add(AutomatedInsight insight) => heap.Enqueue(insight, -insight.Score);

        foreach (var alert in activeAlerts)
        {
            var ageSeconds = Math.Max(0, (int)(now - alert.RaisedUtc).TotalSeconds);
            Add(new AutomatedInsight
            {
                Title = $"{alert.DeviceName}: {Humanise(alert.Kind)}",
                Detail = $"{alert.Message}. Active for {ageSeconds}s.",
                DeviceId = alert.DeviceId,
                Severity = InsightSeverity.Critical,
                Score = 100 + Math.Min(ageSeconds, 60) / 6.0
            });
        }

        var alerting = activeAlerts.Select(a => a.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            if (device.RecentAnomalyCount >= 3 && !alerting.Contains(device.DeviceId))
            {
                Add(new AutomatedInsight
                {
                    Title = $"{device.Name} looks unstable",
                    Detail = $"{device.RecentAnomalyCount} anomalous readings in the last minute.",
                    DeviceId = device.DeviceId,
                    Severity = InsightSeverity.Warning,
                    Score = 60 + device.RecentAnomalyCount
                });
            }

            if (device.Category != SensorCategory.Actuator
                && device.Status is DeviceStatus.Online or DeviceStatus.Alert)
            {
                var trend = ProjectTrend(device);
                if (trend is not null) Add(trend);
            }
        }

        var results = new List<AutomatedInsight>();
        while (heap.Count > 0 && results.Count < MaxInsights)
        {
            results.Add(heap.Dequeue());
        }

        return results;
    }

    /// <summary>
    /// Least-squares line through the device's last few readings. If the line says the value will cross its
    /// critical threshold within about a minute and a half, raise a heads-up before it actually does.
    /// </summary>
    private AutomatedInsight? ProjectTrend(DeviceState device)
    {
        var points = _timeline.GetTimeline(device.DeviceId, 8);
        if (points.Count < 5 || points.Any(p => p.IsAnomaly)) return null;

        var t0 = points[0].TimestampUtc;
        var xs = points.Select(p => (p.TimestampUtc - t0).TotalSeconds).ToArray();
        var ys = points.Select(p => p.Value).ToArray();

        var meanX = xs.Average();
        var meanY = ys.Average();
        var denominator = xs.Sum(x => (x - meanX) * (x - meanX));
        if (denominator < 1e-6) return null;

        var slope = xs.Zip(ys, (x, y) => (x - meanX) * (y - meanY)).Sum() / denominator; // units per second
        var current = meanY + slope * (xs[^1] - meanX);

        if (device.Unit.Contains("moisture", StringComparison.OrdinalIgnoreCase))
        {
            if (slope <= -0.3 && current > _moistureLow)
            {
                var seconds = (current - _moistureLow) / -slope;
                if (seconds <= 90)
                {
                    return new AutomatedInsight
                    {
                        Title = $"{device.Name} is drying out",
                        Detail = $"Falling about {-slope:F1}%/s — projected to drop below {_moistureLow:F0}% in ~{seconds:F0}s.",
                        DeviceId = device.DeviceId,
                        Severity = InsightSeverity.Warning,
                        Score = 70 + (90 - seconds) / 90 * 20
                    };
                }
            }
        }
        else if (string.Equals(device.Unit, "W", StringComparison.OrdinalIgnoreCase))
        {
            if (slope >= 20 && current < _powerCritical)
            {
                var seconds = (_powerCritical - current) / slope;
                if (seconds <= 60)
                {
                    return new AutomatedInsight
                    {
                        Title = $"{device.Name} load is climbing",
                        Detail = $"Rising about {slope:F0} W/s — projected to pass {_powerCritical:F0} W in ~{seconds:F0}s.",
                        DeviceId = device.DeviceId,
                        Severity = InsightSeverity.Warning,
                        Score = 70 + (60 - seconds) / 60 * 20
                    };
                }
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Token helpers
    // ------------------------------------------------------------------

    private static string AlertToken(string deviceId, AlertKind kind) => $"alert:{deviceId}|{kind}";

    private static bool IsAlertToken(string token) => token.StartsWith("alert:", StringComparison.Ordinal);

    private static string Humanise(AlertKind kind) => kind switch
    {
        AlertKind.PowerSpike => "power spike",
        AlertKind.MoistureCriticalLow => "moisture critically low",
        AlertKind.MoistureCriticalHigh => "moisture critically high",
        AlertKind.Disconnected => "disconnected",
        _ => kind.ToString()
    };

    private static string DescribeToken(string token, Dictionary<string, DeviceState> byId)
    {
        if (token.StartsWith("search:", StringComparison.Ordinal))
        {
            var target = token["search:".Length..];
            return byId.TryGetValue(target, out var device) ? $"searched {device.Name}" : $"searched \"{target}\"";
        }

        if (token.StartsWith("cmd:", StringComparison.Ordinal))
        {
            var parts = token.Split(':', 3);
            if (parts.Length == 3)
            {
                var name = parts[2] == "*" ? "all valves" : byId.TryGetValue(parts[2], out var d) ? d.Name : parts[2];
                return $"{parts[1]} {name}";
            }
        }

        return token;
    }
}
