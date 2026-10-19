using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SmartX.Client;
using SmartX.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["SmartX:ApiBaseUrl"] ?? "https://localhost:7080";

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl)
});

builder.Services.AddSingleton(new ApiOptions(apiBaseUrl));
builder.Services.AddScoped<SmartXApiClient>();
builder.Services.AddScoped<TelemetryHubClient>();

// One GatewayState for the whole app: owns the SignalR subscription and every page's live data + UI state.
builder.Services.AddScoped<GatewayState>();

await builder.Build().RunAsync();
