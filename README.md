# Smart-X IoT Mesh Ecosystem — Part 1: Data Ingestion & Validation Gateway

A hybrid IoT ingestion gateway for the Smart-X ecosystem, built on **.NET 10**:

- **SmartX.Api** — ASP.NET Core Minimal API backend: sensor registry, telemetry
  ingestion pipeline, anomaly detection, SignalR live feed, file uploads.
- **SmartX.Client** — Blazor WebAssembly dashboard: sensor registration, media
  uploads, live telemetry feed, and the **Anomaly Heartbeat** engagement widget.
- **SmartX.Shared** — class library shared by both: domain models and DTOs.

## Architecture

```
SmartX.sln
├── src/SmartX.Shared   (class library — models, DTOs, generics, operators, recursion)
├── src/SmartX.Api      (ASP.NET Core Minimal API + SignalR)
└── src/SmartX.Client   (Blazor WebAssembly)
```

## Where each required C# mechanic lives

| Requirement | Location | What it does |
|---|---|---|
| Generics | `SmartX.Shared/Models/TelemetryPacket.cs` | `TelemetryPacket<T>` wraps a float/int/bool sample without boxing until it crosses the `ITelemetryPacket.RawValue` boundary. |
| Operator overloading | `SmartX.Shared/Models/SensorReading.cs` | `+`, `-`, `==`, `!=`, `<`, `>` on `SensorReading` — used live in `TelemetryStore` for anomaly-delta detection and in `/api/telemetry/aggregate` for `Meter3 = Meter1 + Meter2`. |
| Advanced arrays | `SmartX.Shared/Models/TelemetryBatchBuffer.cs` | Jagged `double[][]` buffers uneven per-device sample counts before flattening into `List<TelemetryPacket<float>>`; `MeshTopologyGrid` is a true 2-D array for the future topology screen. |
| Recursion | `SmartX.Shared/Models/DeploymentNode.cs` | `DeploymentValidator.Validate` recursively walks a Facility → Zone → Sub-Zone → Device tree, checking uniqueness, depth, and leaf-only device nodes. |

> **Note:** the NuGet package versions pinned in the `.csproj` files (e.g.
> `Microsoft.AspNetCore.OpenApi`, `Microsoft.AspNetCore.SignalR.Client`) were
> written to match .NET 10, but this repo was authored without live access to
> nuget.org to confirm exact published version numbers. If `dotnet restore`
> reports a version that doesn't exist, run
> `dotnet add package <PackageName>` inside the affected project to pick up
> the latest compatible version automatically.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- (Optional) Docker Desktop, if you want to run via containers instead of the SDK.

## Restoring & building

```bash
git clone <your-repo-url>
cd SmartX
dotnet restore
dotnet build
```

## Running locally (two terminals)

**Terminal 1 — API:**
```bash
cd src/SmartX.Api
dotnet run
```
By default this listens on `https://localhost:7080` (check the console output —
the exact port comes from `Properties/launchSettings.json` / the `dotnet run`
banner on first launch). The API starts seeding mock telemetry for 6 simulated
ESP32 devices immediately (`SmartX:MockSeeder` in `appsettings.json`).

**Terminal 2 — Client:**
```bash
cd src/SmartX.Client
dotnet run
```
This serves the Blazor WebAssembly app, typically on `https://localhost:5001` or
similar — check the console output for the exact URL.

> **Important:** if the API's port differs from `https://localhost:7080`, update
> `src/SmartX.Client/wwwroot/appsettings.json` (`SmartX:ApiBaseUrl`) to match,
> and add the client's origin to the CORS policy in
> `src/SmartX.Api/Program.cs` if it isn't already listed.

Open the client URL in your browser. You should see:
1. The landing page with the three architectural pillars (two disabled).
2. **Sensor Registration** — register a device, attach a file, and run the
   recursive deployment-tree validator.
3. **Telemetry Dashboard** — the live feed and the Anomaly Heartbeat widget,
   which will already be pulsing from the mock seeder within a couple of seconds.

## Running with Docker Compose

```bash
docker compose up --build
```

- API → `http://localhost:8080`
- Client → `http://localhost:8081`

> Before building the client image for Docker, update
> `src/SmartX.Client/wwwroot/appsettings.json` so `SmartX:ApiBaseUrl` points to
> `http://localhost:8080` (the host-mapped API port), since the Blazor WASM app
> runs in the user's browser, not inside the Docker network.

## Key API endpoints

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/sensors` | List registered sensors |
| `POST` | `/api/sensors` | Register a new sensor |
| `POST` | `/api/sensors/{id}/upload` | Attach a config file / photo / log |
| `POST` | `/api/telemetry` | Ingest one telemetry sample |
| `GET` | `/api/telemetry/feed` | Recent feed items |
| `GET` | `/api/telemetry/aggregate?a=&b=` | Aggregate two meters via overloaded `+` |
| `POST` | `/api/telemetry/seed-batch` | Seed a jagged batch and flatten it |
| `GET` | `/api/deployment/sample` | Sample Facility→Zone→SubZone→Device tree |
| `POST` | `/api/deployment/validate` | Recursively validate a deployment tree |
| WebSocket | `/hubs/telemetry` | SignalR live feed |

With the API running, browse `/openapi/v1.json` (Development environment) for
the full OpenAPI document, or explore endpoints directly with `curl`/Postman.

## The engagement feature: Anomaly Heartbeat

Instead of a plain progress bar, the dashboard shows an animated ECG-style
pulse line. While all sensors are within their configured delta thresholds it
beats calmly in green; the instant a spike or disconnect crosses the
threshold, it turns red and beats faster. Alongside it, a streak counter
("N readings anomaly-free") awards Bronze/Silver/Gold/Platinum badges at
milestones, gamifying healthy monitoring behaviour rather than just showing
raw numbers. See the accompanying research report for the justification.

## Mock data seeding

`SmartX.Api/Services/MockTelemetrySeeder.cs` is a `BackgroundService` that
continuously pushes readings for 6 simulated ESP32 devices through the same
ingestion pipeline real hardware would use, occasionally injecting spikes so
the anomaly detection and heartbeat widget have real events to react to.
Configure device count / interval / on-off in `appsettings.json` under
`SmartX:MockSeeder`.

## Project status

- ✅ Part 1 — Sensor Data Ingestion & Telemetry (this repository state)
- ⬜ Part 2 — Real-Time Command Stream & History
- ⬜ Final PoE — Network Topology & Mesh Routing

## Repository conventions

- Commits are scoped conventionally (`feat`, `chore`, `style`, `docs`) and
  kept small — one concern per commit — so the history itself documents how
  the ingestion gateway was built up.
- `wwwroot/uploads/**` is git-ignored except for a `.gitkeep`; uploaded files
  are runtime data, not source.
