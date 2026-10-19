# Part 2 — Video demo script (~6 minutes)

A suggested running order that covers every rubric row. Have the API and client running and the browser on the
Landing page. Speak to the code as well as the screen — markers like the data-structure names are worth saying out loud.

## 0:00 — Intro (20 s)
- "This is Part 2 of the Smart-X PoE: the real-time command stream on top of the Part 1 ingestion gateway."
- Show the menu: three pillars, the third is a placeholder for the final PoE.

## 0:20 — Navigation and preserved state (45 s)
- Open **Telemetry Dashboard**: point at the Anomaly Heartbeat streak.
- Go to **Sensor Registration**, type half a MAC address, go to **Command Stream**, then back: the draft is still there.
- Explain: one `GatewayState` for the whole app owns the SignalR subscription and every page's state.

## 1:05 — The Command Stream page tour (45 s)
- Stats bar, device cards (live values), live stream with the **lane** column.
- Explain the flow: classify → `Queue<T>` or `PriorityQueue<T>` → processor → dictionary / sorted dictionary / hash set.
- Open the **Sensor Timeline** for Moisture Sensor B ("served from a `SortedDictionary` keyed by timestamp").

## 1:50 — Search and filter (30 s)
- Type `pump`; use the Category and State filters. Mention the server-side equivalent `GET /api/devices?query=`.
- Mention `Dictionary<string, DeviceState>`: every packet finds its device in O(1).

## 2:20 — Priority queue vs FIFO queue (60 s)
- Click **📦 Burst 300 + 1 critical**. Watch the *Standard queue* tile climb.
- Point at the stream: the `PRIORITY` row jumps the line. Show `waitMs` for both lanes via `GET /api/pipeline/recent`
  (or the *average wait* figures on the stats tiles).
- "The critical packet was queued last and processed first — `TryDequeue` always drains the priority lane."

## 3:20 — Alerts and the HashSet (45 s)
- Click **⚡ Critical power spike** twice. One alert, *Duplicates suppressed* goes up.
- Disconnect a node in *Simulation Tools*; after ~15 s the Disconnected alert appears once. Reconnect it.
- "`HashSet.Add` returns false for duplicates, so repeats never get processed twice."

## 4:05 — Manual override and the undo Stack (45 s)
- Turn **Pump A** OFF from its card; press **Shut down all valves**.
- Show the undo stack (top = most recent), press **↩ Undo last** — valves return to ON.
- "`Stack<DeviceCommand>` — last in, first out, with a snapshot of the previous state for each command."

## 4:50 — Predictive engine (70 s)
- With Pump A off, wait for the 💡 *drying out* insight, then the critical moisture alert.
- Show **Suggested Actions**: *Turn ON Pump A* with confidence and the reason. Click **Run**; moisture recovers.
- Explain the engine: transition graph of "what follows what", trend projection, results ranked in a priority queue,
  seeded with simulated history and learning from live use.

## 6:00 — Wrap-up (15 s)
- Mention Docker (`docker compose up --build`), the README test instructions and the commit history.
