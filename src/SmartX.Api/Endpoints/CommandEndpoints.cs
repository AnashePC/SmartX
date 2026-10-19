using SmartX.Api.Services;
using SmartX.Shared.Dtos;

namespace SmartX.Api.Endpoints;

public static class CommandEndpoints
{
    public static void MapCommandEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commands").WithTags("Commands");

        // Command history + the current contents of the undo stack (top first).
        group.MapGet("/", (ICommandService commands) => Results.Ok(commands.GetSnapshot()));

        group.MapPost("/", async (IssueCommandRequest request, ICommandService commands,
            IDeviceStateStore devices, IActionSuggestionEngine engine, IGatewayNotifier notifier) =>
        {
            var result = commands.Issue(request);

            if (!result.Success)
            {
                return Results.BadRequest(result);
            }

            await PublishAftermathAsync(result, commands, devices, engine, notifier);
            return Results.Ok(result);
        });

        // Pops the Stack<DeviceCommand> and restores the actuator state(s) it captured.
        group.MapPost("/undo", async (ICommandService commands, IDeviceStateStore devices,
            IActionSuggestionEngine engine, IGatewayNotifier notifier) =>
        {
            var result = commands.UndoLast();

            if (!result.Success)
            {
                return Results.BadRequest(result);
            }

            await PublishAftermathAsync(result, commands, devices, engine, notifier);
            return Results.Ok(result);
        });
    }

    private static async Task PublishAftermathAsync(CommandResult result, ICommandService commands,
        IDeviceStateStore devices, IActionSuggestionEngine engine, IGatewayNotifier notifier)
    {
        await notifier.CommandsChangedAsync(commands.GetSnapshot());

        if (result.Command is not null)
        {
            foreach (var deviceId in result.Command.PreviousStates.Keys)
            {
                if (devices.TryGetDevice(deviceId, out var device))
                {
                    await notifier.DeviceChangedAsync(device);
                }
            }
        }

        await notifier.SuggestionsAsync(engine.GetSuggestions());
    }
}
