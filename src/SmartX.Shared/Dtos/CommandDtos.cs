using SmartX.Shared.Models;

namespace SmartX.Shared.Dtos;

/// <summary>POST /api/commands</summary>
public sealed class IssueCommandRequest
{
    public CommandAction Action { get; set; }

    /// <summary>Target device id. Ignored (may be "*") for ShutDownAllValves.</summary>
    public string DeviceId { get; set; } = string.Empty;
}

public sealed class CommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DeviceCommand? Command { get; set; }
}

/// <summary>POST /api/activity/search — records an operator search so the engine can learn from it.</summary>
public sealed class SearchActivityRequest
{
    public string Query { get; set; } = string.Empty;
}
