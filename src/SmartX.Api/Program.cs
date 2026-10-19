using SmartX.Api.Endpoints;
using SmartX.Api.Hubs;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string ClientCorsPolicy = "SmartXClient";

builder.Services.AddOpenApi(); // built-in ASP.NET Core OpenAPI (no external NuGet needed)
builder.Services.AddSignalR();

builder.Services.AddSingleton<ISensorRegistry, SensorRegistry>();
builder.Services.AddSingleton<ITelemetryStore, TelemetryStore>();
builder.Services.AddHostedService<MockTelemetrySeeder>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy =>
    {
        // Blazor WASM dev server + typical localhost ports. Tighten this to
        // your deployed client origin before shipping to production.
        policy.WithOrigins(
                "https://localhost:5001",
                "http://localhost:5001",
                "http://localhost:5000",
                "https://localhost:7080",
                "http://localhost:5080")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // serves the OpenAPI document at /openapi/v1.json
}

app.UseCors(ClientCorsPolicy);
app.UseStaticFiles(); // serves wwwroot/uploads/** for attached sensor media/logs

app.MapGet("/", () => Results.Ok(new { service = "SmartX.Api", status = "running" }));

app.MapSensorEndpoints();
app.MapTelemetryEndpoints();
app.MapDeploymentEndpoints();
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();
