using SmartX.Api.Endpoints;
using SmartX.Api.Hubs;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string ClientCorsPolicy = "SmartXClient";

builder.Services.AddOpenApi(); // built-in ASP.NET Core OpenAPI
builder.Services.AddSignalR();

// ---- Part 1 services ----
builder.Services.AddSingleton<ISensorRegistry, SensorRegistry>();

// ---- Part 2: ingestion pipeline, data-structure stores and engines (all singletons: they hold live state) ----
builder.Services.AddSingleton<IAlertClassifier, AlertClassifier>();
builder.Services.AddSingleton<ITelemetryPipeline, TelemetryPipeline>();   // Queue<T> + PriorityQueue<T>
builder.Services.AddSingleton<IDeviceStateStore, DeviceStateStore>();     // Dictionary<TKey,TValue>
builder.Services.AddSingleton<ITimelineStore, TimelineStore>();           // SortedDictionary<TKey,TValue>
builder.Services.AddSingleton<IAlertTracker, AlertTracker>();             // HashSet<T>
builder.Services.AddSingleton<ISimulationControl, SimulationControl>();    // HashSet<T>
builder.Services.AddSingleton<IActionSuggestionEngine, ActionSuggestionEngine>();
builder.Services.AddSingleton<ICommandService, CommandService>();         // Stack<T> undo history
builder.Services.AddSingleton<IGatewayNotifier, GatewayNotifier>();
builder.Services.AddSingleton<IGatewayStats, GatewayStats>();
builder.Services.AddSingleton<ITelemetryStore, TelemetryStore>();

// Background workers: simulated devices -> pipeline -> processor, plus the health/heartbeat loop.
builder.Services.AddHostedService<TelemetryProcessor>();
builder.Services.AddHostedService<GatewayHeartbeatService>();
builder.Services.AddHostedService<MockTelemetrySeeder>();

var extraOrigins = builder.Configuration.GetSection("SmartX:Cors:Origins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy =>
    {
        // Any localhost / 127.0.0.1 origin is allowed (Blazor dev server, Docker-mapped client ports...),
        // plus anything listed under SmartX:Cors:Origins. Tighten this before real deployment.
        policy.SetIsOriginAllowed(origin =>
            {
                if (extraOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;

                return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                       && (uri.Host == "localhost" || uri.Host == "127.0.0.1");
            })
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
app.MapDeviceEndpoints();
app.MapCommandEndpoints();
app.MapPipelineEndpoints();
app.MapSuggestionEndpoints();
app.MapSimulationEndpoints();
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();
