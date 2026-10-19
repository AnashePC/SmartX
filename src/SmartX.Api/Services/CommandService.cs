using SmartX.Shared.Dtos;
using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface ICommandService
{
    CommandResult Issue(IssueCommandRequest request);
    CommandResult UndoLast();
    CommandHistorySnapshot GetSnapshot(int historyTake = 50);
    int UndoDepth { get; }
}

/// <summary>
/// Manual override commands with an operator "Undo".
///
/// Every applied command is pushed onto a <see cref="Stack{T}"/>. Because a stack is LIFO, popping it
/// always yields the most recently issued command, which is exactly the one an operator wants to revert.
/// Each command carries a snapshot of the actuator state(s) from BEFORE it ran, so undoing even a bulk
/// "shut down all valves" restores every valve to what it was.
/// </summary>
public sealed class CommandService : ICommandService
{
    private const int HistoryCapacity = 200;

    private readonly Stack<DeviceCommand> _undo = new();
    private readonly List<DeviceCommand> _history = new();
    private readonly object _gate = new();
    private readonly IDeviceStateStore _devices;
    private readonly IActionSuggestionEngine _engine;
    private long _sequence;

    public CommandService(IDeviceStateStore devices, IActionSuggestionEngine engine)
    {
        _devices = devices;
        _engine = engine;
    }

    public int UndoDepth
    {
        get { lock (_gate) { return _undo.Count; } }
    }

    public CommandResult Issue(IssueCommandRequest request)
    {
        DeviceCommand command;
        string targetKey;

        lock (_gate)
        {
            List<DeviceState> targets;
            string displayName;

            if (request.Action == CommandAction.ShutDownAllValves)
            {
                targets = _devices.GetActuators()
                    .Where(d => d.Name.Contains("valve", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targets.Count == 0)
                {
                    return Fail("No valves are registered, so there is nothing to shut down.");
                }

                targetKey = "*";
                displayName = "All valves";
            }
            else
            {
                if (!_devices.TryGetDevice(request.DeviceId ?? string.Empty, out var device))
                {
                    return Fail($"Unknown device '{request.DeviceId}'.");
                }

                if (device.Category != SensorCategory.Actuator)
                {
                    return Fail($"{device.Name} is a sensor, not an actuator — it cannot be commanded.");
                }

                if (device.Status == DeviceStatus.Disconnected)
                {
                    return Fail($"{device.Name} is disconnected — the command could not be delivered.");
                }

                targets = new List<DeviceState> { device };
                targetKey = device.DeviceId;
                displayName = device.Name;
            }

            // Snapshot BEFORE applying, so undo can restore exactly this.
            var previous = targets.ToDictionary(t => t.DeviceId, t => t.ActuatorOn ?? false);

            foreach (var target in targets)
            {
                var desired = request.Action switch
                {
                    CommandAction.TurnOn => true,
                    CommandAction.TurnOff => false,
                    CommandAction.Toggle => !(target.ActuatorOn ?? false),
                    _ => false
                };

                _devices.TrySetActuatorState(target.DeviceId, desired, out _);
            }

            command = new DeviceCommand
            {
                Sequence = ++_sequence,
                Action = request.Action,
                DeviceId = targetKey,
                DeviceName = displayName,
                Description = Describe(request.Action, displayName, targets.Count),
                PreviousStates = previous
            };

            _undo.Push(command);
            _history.Add(command);
            Trim();
        }

        // Teach the prediction engine outside the lock (it has its own lock).
        _engine.RecordCommand(request.Action, targetKey);

        return new CommandResult { Success = true, Message = command.Description, Command = command };
    }

    public CommandResult UndoLast()
    {
        lock (_gate)
        {
            if (!_undo.TryPop(out var original))
            {
                return Fail("Nothing to undo.");
            }

            foreach (var (deviceId, wasOn) in original.PreviousStates)
            {
                _devices.TrySetActuatorState(deviceId, wasOn, out _);
            }

            original.Status = CommandStatus.Undone;

            var entry = new DeviceCommand
            {
                Sequence = ++_sequence,
                Action = original.Action,
                DeviceId = original.DeviceId,
                DeviceName = original.DeviceName,
                Description = $"Undo: {original.Description}",
                IsUndoEntry = true,
                PreviousStates = original.PreviousStates
            };

            _history.Add(entry);
            Trim();

            return new CommandResult { Success = true, Message = entry.Description, Command = entry };
        }
    }

    public CommandHistorySnapshot GetSnapshot(int historyTake = 50)
    {
        lock (_gate)
        {
            return new CommandHistorySnapshot
            {
                History = Enumerable.Reverse(_history).Take(historyTake).ToList(),
                UndoStack = _undo.ToArray().ToList() // Stack<T>.ToArray() is top-first
            };
        }
    }

    private void Trim()
    {
        if (_history.Count > HistoryCapacity)
        {
            _history.RemoveRange(0, _history.Count - HistoryCapacity);
        }
    }

    private static string Describe(CommandAction action, string name, int count) => action switch
    {
        CommandAction.TurnOn => $"Turn ON {name}",
        CommandAction.TurnOff => $"Turn OFF {name}",
        CommandAction.Toggle => $"Toggle {name}",
        _ => $"Shut down all valves ({count})"
    };

    private static CommandResult Fail(string message) => new() { Success = false, Message = message };
}
