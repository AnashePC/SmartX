using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Outcome of inspecting one incoming packet. <see cref="Severity"/> orders critical packets
/// against each other inside the priority queue (bigger = more severe = processed first).
/// </summary>
public sealed record Classification(AlertPriority Priority, AlertKind? Kind, int Severity, string Message)
{
    public static readonly Classification Normal = new(AlertPriority.Normal, null, 0, string.Empty);
}

public interface IAlertClassifier
{
    Classification Classify(TelemetryIngestRequest request);
}

/// <summary>
/// Decides, at the front door of the gateway, whether a packet is routine or critical.
/// Critical packets (a severe power spike, or a hydroponics node whose moisture has collapsed)
/// are routed to the priority queue so they bypass every routine packet still waiting.
/// Thresholds come from configuration (SmartX:AlertRules).
/// </summary>
public sealed class AlertClassifier : IAlertClassifier
{
    private readonly double _powerCriticalWatts;
    private readonly double _moistureCriticalLow;
    private readonly double _moistureCriticalHigh;

    public AlertClassifier(IConfiguration config)
    {
        _powerCriticalWatts = config.GetValue("SmartX:AlertRules:PowerCriticalWatts", 800.0);
        _moistureCriticalLow = config.GetValue("SmartX:AlertRules:MoistureCriticalLow", 20.0);
        _moistureCriticalHigh = config.GetValue("SmartX:AlertRules:MoistureCriticalHigh", 80.0);
    }

    public Classification Classify(TelemetryIngestRequest request)
    {
        switch (request.Kind)
        {
            case TelemetryValueKind.Integer when IsPower(request.Unit):
                if (request.NumericValue >= _powerCriticalWatts)
                {
                    var over = (request.NumericValue - _powerCriticalWatts) / _powerCriticalWatts * 100;
                    return new Classification(
                        AlertPriority.Critical,
                        AlertKind.PowerSpike,
                        Math.Max(1, (int)Math.Round(over)),
                        $"Power draw {request.NumericValue:F0} W exceeds the {_powerCriticalWatts:F0} W critical limit");
                }
                break;

            case TelemetryValueKind.Float when IsMoisture(request.Unit):
                if (request.NumericValue < _moistureCriticalLow)
                {
                    var under = (_moistureCriticalLow - request.NumericValue) / _moistureCriticalLow * 100;
                    return new Classification(
                        AlertPriority.Critical,
                        AlertKind.MoistureCriticalLow,
                        Math.Max(1, (int)Math.Round(under)),
                        $"Moisture {request.NumericValue:F1}% is below the {_moistureCriticalLow:F0}% critical minimum");
                }

                if (request.NumericValue > _moistureCriticalHigh)
                {
                    var over = (request.NumericValue - _moistureCriticalHigh) / _moistureCriticalHigh * 100;
                    return new Classification(
                        AlertPriority.Critical,
                        AlertKind.MoistureCriticalHigh,
                        Math.Max(1, (int)Math.Round(over)),
                        $"Moisture {request.NumericValue:F1}% is above the {_moistureCriticalHigh:F0}% critical maximum");
                }
                break;
        }

        return Classification.Normal;
    }

    private static bool IsPower(string unit) => string.Equals(unit, "W", StringComparison.OrdinalIgnoreCase);

    private static bool IsMoisture(string unit) => unit.Contains("moisture", StringComparison.OrdinalIgnoreCase);
}
