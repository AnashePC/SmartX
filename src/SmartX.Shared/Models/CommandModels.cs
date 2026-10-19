namespace SmartX.Shared.Models;

/// <summary>
/// Manual override actions an operator can send down to an actuator node.
/// </summary>
public enum CommandAction
{
    TurnOn,
    TurnOff,
    Toggle,

    /// <summary>Bulk command: closes every registered valve in one action.</summary>
    ShutDownAllValves
}

public enum CommandStatus
{
    Applied,
    Undone
}

/// <summary>
/// One manual override command. <see cref="PreviousStates"/> snapshots the actuator
/// state(s) BEFORE the command ran, which is exactly what the Stack&lt;T&gt;-backed
/// undo feature needs in order to revert it (including bulk commands that touch many devices).
/// </summary>
public sealed class DeviceCommand
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public CommandAction Action { get; init; }

    /// <summary>Target device id, or "*" for bulk commands.</summary>
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; init; } = DateTime.UtcNow;
    public CommandStatus Status { get; set; } = CommandStatus.Applied;

    /// <summary>True when this history entry records an undo rather than an operator command.</summary>
    public bool IsUndoEntry { get; set; }

    /// <summary>deviceId -> actuator state (true = ON) before this command was applied.</summary>
    public Dictionary<string, bool> PreviousStates { get; init; } = new();
}

/// <summary>The history list plus the current contents of the undo stack (top first).</summary>
public sealed class CommandHistorySnapshot
{
    public List<DeviceCommand> History { get; set; } = new();
    public List<DeviceCommand> UndoStack { get; set; } = new();
}
