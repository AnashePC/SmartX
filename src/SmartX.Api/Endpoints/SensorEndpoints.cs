using SmartX.Api.Services;
using SmartX.Shared.Dtos;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    public static void MapSensorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sensors").WithTags("Sensors");

        group.MapGet("/", (ISensorRegistry registry) =>
            Results.Ok(registry.GetAll()));

        group.MapGet("/{id:guid}", (Guid id, ISensorRegistry registry) =>
        {
            var sensor = registry.GetById(id);
            return sensor is null ? Results.NotFound() : Results.Ok(sensor);
        });

        group.MapPost("/", (SensorRegistrationRequest request, ISensorRegistry registry, IDeviceStateStore devices) =>
        {
            if (string.IsNullOrWhiteSpace(request.MacAddress))
            {
                return Results.BadRequest("MAC address / unique identifier is required.");
            }

            var registration = registry.Register(request.MacAddress, request.DeploymentLocation, request.Category);

            // Part 2: a registered sensor also becomes an entry in the live device dictionary,
            // keyed by MAC address, so telemetry from it resolves in O(1).
            devices.RegisterSensor(registration);

            return Results.Created($"/api/sensors/{registration.Id}", registration);
        });

        // Media/log attachment: config files, deployment photos, hardware logs.
        group.MapPost("/{id:guid}/upload", async (Guid id, HttpRequest httpRequest, ISensorRegistry registry, IWebHostEnvironment env) =>
        {
            if (registry.GetById(id) is null)
            {
                return Results.NotFound($"No sensor registered with id '{id}'.");
            }

            if (!httpRequest.HasFormContentType)
            {
                return Results.BadRequest("Expected multipart/form-data with a 'file' field.");
            }

            var form = await httpRequest.ReadFormAsync();
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest("No file was included in the upload.");
            }

            var uploadsRoot = Path.Combine(env.WebRootPath, "uploads", id.ToString());
            Directory.CreateDirectory(uploadsRoot);

            var safeFileName = Path.GetFileName(file.FileName);
            var destinationPath = Path.Combine(uploadsRoot, safeFileName);

            await using (var stream = File.Create(destinationPath))
            {
                await file.CopyToAsync(stream);
            }

            registry.TryAttachFile(id, safeFileName);

            return Results.Ok(new { fileName = safeFileName, sizeBytes = file.Length });
        }).DisableAntiforgery();
    }
}
