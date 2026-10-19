# Part 2 — Rubric mapping

Where to find each marked item, and the quickest way to show it working. Run the API and client first
(see the root [README](../README.md)).

## Main Menu Navigation and Active Pillar State — 30 marks

| Criterion | Evidence | Demo |
|---|---|---|
| Navigation menu with Part 1, Part 2 and Topology modules | `Shared/NavMenu.razor` groups the three pillars; the active page is highlighted, the Command Stream entry carries a live alert-count badge | Click through Landing → Sensor Registration → Telemetry Dashboard → Command Stream → Network Topology |
| Switching views never drops state | `Services/GatewayState.cs` is registered once for the whole app. It owns the single SignalR subscription, the live feed, devices, alerts, heartbeat streak and badges, and each page's form drafts, filters and selected device | Type `pump` in the search box and pick a device; navigate to the Telemetry Dashboard and back — search, filter and selection are intact. The streak counter keeps counting while you are on another page |
| Asynchronous, no UI freezing | All API calls are `async`; change notifications are coalesced to ~6 per second so heavy telemetry never blocks rendering | Run **Burst 300 + 1 critical** and keep clicking around |
| Does not crash the backend or the client | `MainLayout.razor` wraps pages in an `ErrorBoundary`; the connection loop retries and shows an offline banner instead of throwing | Stop the API: the banner appears and the page stays usable; restart it and the console reconnects by itself |
| Part 1 still works | Sensor Registration and Telemetry Dashboard are unchanged in behaviour (now state-preserving); all Part 1 endpoints remain | Register a sensor, attach a file, send a manual reading, run the aggregate and batch demos |

## Advanced Collections: Stacks and Queues — 15 marks

| Criterion | Evidence | Demo |
|---|---|---|
| `Queue<T>` for FIFO ingestion | `TelemetryPipeline._standard` (`Queue<QueuedPacket>`) | README test 2: processed `sequence` values rise in order within the standard lane |
| Priority queue — critical alerts bypass the standard stream | `TelemetryPipeline._critical` (`PriorityQueue<QueuedPacket, (int Rank, long Sequence)>`); `TryDequeue` drains it first; classifier in `AlertClassifier.cs` | **Burst 300 + 1 critical** — the critical packet is processed within the first few packets and waits milliseconds, not seconds |
| `Stack<T>` for manual-command undo | `CommandService._undo` (`Stack<DeviceCommand>`); every command snapshots prior actuator state, so even bulk "shut down all valves" undoes cleanly | Shut down all valves → **↩ Undo last** — both valves return to ON |

## Lookup Structures: Dictionaries and Key-Value Maps — 15 marks

| Criterion | Evidence | Demo |
|---|---|---|
| `Dictionary<TKey,TValue>` for O(1) device lookups by MAC / node id | `DeviceStateStore._devices`; each packet resolves its device with one `TryGetValue` | `GET /api/devices/ESP32-03`; register a sensor and it joins the same dictionary |
| Sorted dictionary for timelines | `TimelineStore` — `SortedDictionary<DateTime, TimelinePoint>` per sensor plus a global log; always enumerates in timestamp order | Click a device card: the timeline graph; `GET /api/history` for the ordered mesh log |

## Unique Collections: Sets — 10 marks

| Criterion | Evidence | Demo |
|---|---|---|
| `HashSet<T>` of active unique error states / disconnected nodes | `AlertTracker._activeKeys` (`HashSet<string>` of `"deviceId\|kind"`); `Add` returning `false` is the duplicate guard. `SimulationControl` also uses a set for silenced nodes | Fire the same critical alert twice → one alert, *Duplicates suppressed* rises. Disconnect a node → one Disconnected alert even though the heartbeat re-checks it every second |

## Predictive Action and Suggestion Engine — 30 marks

| Criterion | Evidence | Demo |
|---|---|---|
| Tracks query / command history and finds patterns | `ActionSuggestionEngine`: searches, commands and alerts become tokens; a weighted transition graph (`Dictionary<string, Dictionary<string,double>>`, first-order Markov chain with a skip-gram) counts what follows what within a 10-minute window | README "closed-loop walkthrough" — the engine reproduces "operator checks Pump A right after Moisture Sensor B drops below 20%" |
| Algorithmic suggestion using an appropriate data structure | Weights → probabilities, low-evidence damping, candidates ranked on a `PriorityQueue` (binary heap). A second pass projects sensor trends with a least-squares fit and warns before a threshold is crossed | The 💡 *"Moisture Sensor B is drying out — projected to drop below 20% in ~Ns"* insight appears ~20 s before the alert |
| Presented as "Suggested Actions" / "Automated Insights" | `Shared/SuggestionsPanel.razor`; pushed over SignalR the moment an alert fires or the operator acts — *before* a search. Insight devices are highlighted and sorted first in the grid | Suggestions appear on their own; **Run** executes a suggested command in one click |

## GitHub and README

| Criterion | Evidence |
|---|---|
| Repository with a good commit history | Small conventional commits (`feat`, `fix`, `refactor`, `chore`, `style`, `docs`), one concern each |
| README in Markdown with setup and test instructions | Root `README.md`: restore / build / run, Docker, and an explicit section for simulating a high-priority alert and testing the queue, stack, dictionary and set implementations |
| Docker | `docker-compose.yml`, `src/SmartX.Api/Dockerfile`, `src/SmartX.Client/Dockerfile` (+ nginx SPA config), optional simulator profile |
