using SmartX.Api.Services;
using SmartX.Shared.Dtos;

namespace SmartX.Api.Endpoints;

public static class SuggestionEndpoints
{
    public static void MapSuggestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Suggestions");

        // "Suggested Actions" + "Automated Insights" for the current moment.
        group.MapGet("/suggestions", (IActionSuggestionEngine engine) => Results.Ok(engine.GetSuggestions()));

        // The dashboard reports each committed operator search so the engine can learn
        // which devices the operator tends to look at after which events.
        group.MapPost("/activity/search", async (SearchActivityRequest request, IActionSuggestionEngine engine,
            IGatewayNotifier notifier) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest("A search query is required.");
            }

            engine.RecordSearch(request.Query);

            var snapshot = engine.GetSuggestions();
            await notifier.SuggestionsAsync(snapshot);
            return Results.Ok(snapshot);
        });
    }
}
