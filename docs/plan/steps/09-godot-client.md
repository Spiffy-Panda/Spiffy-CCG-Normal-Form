## Step 9 — Godot client against the same lobby

Bring a Godot 4.x C# front-end up to parity with the web tabletop,
pointed at the existing REST room server. Same games, same rooms,
cross-client play (a web player and a Godot player can share a room).
No new game logic — this is a presentation client.

Read first: [web/rooms-protocol.md](../web/rooms-protocol.md),
[reference/rest.md](../reference/rest.md), the commits
`6ddbc1f`..`699ba8d` for the room / SSE / LegalAction shapes that
this client has to match.

## Scope — in

- Godot 4.2+ C# project that renders Lobby + Tabletop.
- Uses the same REST API the web client does (`/api/rooms`, `/join`,
  `/actions`, `/state`, `/events`, `/cards`, `/decks/presets`).
- SSE event stream over HTTP (Godot has no native SSE — pattern
  below).
- Card / board / inspector rendering with the low-fi "chit" aesthetic
  so gameplay parity is obvious; polish iterates later.
- Identity persistence in `user://` (Godot's per-user save dir).
- Keyboard + mouse input. Gamepad is a follow-up.

## Scope — out

- No in-process interpreter for this step. CLAUDE.md notes the Godot
  host will eventually embed `Ccgnf` for single-player; that's a
  separate step (call it 9b) once the REST client shell works.
- No 3D board, no art assets, no animations. Flat panels.
- No spectator view. Godot client is a player client only.
- No lobby-level chat, no account system — just the
  `tok_<hex>` room token scheme from the rooms protocol.

## Sub-commits

### 9a. Godot project scaffold + REST client library

- New `src/Ccgnf.Godot/` project. SDK-style `.csproj`, `net8.0` to
  match. Add to `Ccgnf.sln`.
- Dependencies: Godot 4.2 C# SDK package, `Microsoft.Extensions.Logging`,
  reference `src/Ccgnf.Rest/Serialization` for DTO record types — the
  web client hand-rolls TS types; Godot reuses the C# records
  directly. No `Ccgnf` runtime reference yet (single-player = 9b).
- `CcgnfClient` C# class: `Create/List/Join/Action/State/Events`
  methods returning DTOs. Uses `System.Net.Http.HttpClient`.
- `RoomEventStream` — SSE reader that wraps a chunked HTTP response
  and yields `RoomEventFrame` as a `ChannelReader<T>`. Godot's
  `HttpClient.GetStreamAsync` works; just read line-by-line and
  parse the `event: game-event\ndata: {...}\n\n` delimiter.
- Smoke test: a one-scene demo that lists rooms via Godot label.
- README section: "Running the Godot client" — points at
  `src/Ccgnf.Godot/project.godot` and how to set
  `CCGNF_SERVER_URL` (default `http://localhost:19397`).

### 9b. Lobby scene

- `Lobby.tscn`. `VBoxContainer` with:
  - Room-list panel: `ItemList` of existing rooms (`roomId`, state,
    occupancy, seed, created-at). Refreshes every 3s via timer.
  - Create-room panel: seed spinbox, `+ Add CPU` button, per-CPU row
    (name `LineEdit` + deck `OptionButton` loaded from
    `/api/decks/presets`). "Create" primary button.
- Click "Open" → transitions to `Tabletop.tscn` with the room id as
  autoload state. Keyboard: Enter submits the create button.
- No deck-building here — same as web: deck picker uses presets only
  in 9b. Constructed/Draft builder is a later follow-up.

### 9c. Tabletop scene — board, hand, event log

- `Tabletop.tscn` grid:
  - Header: turn chip + phase tracker, room id, "Back to Lobby".
  - Centre: three arena columns. Each arena has opponent conduit row,
    opponent unit lane, divider, your unit lane, your conduit row.
  - Bottom: your hand (horizontal card row) + aether/hand/arsenal
    counters.
  - Right: event log panel (`RichTextLabel` scrollable).
- `Card.tscn` reusable scene: 63×88 panel, faction-coloured stripe,
  cost pip, name, humanized rules (or keywords/text fallback — same
  logic as the web inspector), clickable.
- `Inspector.tscn` side panel matching web layout; toggled by card
  click / Escape.
- State updates: `TabletopController.cs` subscribes to
  `/api/rooms/{id}/events` SSE, mirrors the web tabletop state
  machine (`pendingInput`, `currentPhase`, `cardCatalog`, …) as
  properties; nodes observe via signals.

### 9d. Action bar + input wiring

- Action bar: horizontal `HBoxContainer` at bottom, one button per
  `LegalAction`. Re-uses the same humanizer output the web frontend
  uses (shipped as a Godot-side reimplementation of
  `web/src/pages/play/tabletop.ts:humanizeAction`, or extract to
  `src/Ccgnf.Rest.Shared/LegalActionHumanizer.cs` so both web
  serverside JSON and Godot consume it — preferred).
- Click → POST `/actions` with `{playerId, token, action: label}`.
- Keyboard shortcuts: `1..9` fires the Nth button; `Escape` closes
  inspector; `Tab` cycles focus between hand cards.

### 9e. Identity persistence + reconnect

- On first room join, write `user://ccgnf-identity.json`:
  `{roomId, playerId, token, name}`. On relaunch, if the room is
  still Active, rejoin automatically via the token.
- If server returns 401 (unknown token) or 404 (room gone), clear
  and fall back to lobby.

### 9f. Parity test — Godot + web in one room

- Manual test checklist (to run before merging):
  1. Start REST server.
  2. Open web `#/play/lobby`; create a room with 1 CPU.
  3. Open Godot client; see the room in its lobby list.
  4. Claim the second seat from Godot; pick a preset deck.
  5. Both clients reach Active state; the room endpoint reports
     both players.
  6. Play a turn from Godot; SSE events arrive on both clients;
     conduit integrity updates on both boards.
  7. Reach GameEnd (TwoConduitsLost); both clients show Finished.

## Architecture notes

- **DTO sharing.** Option A: keep `Ccgnf.Rest.Serialization` in its
  current project and reference it from `Ccgnf.Godot`. Option B:
  extract to `src/Ccgnf.Contracts/` so Godot doesn't pull in the
  whole ASP.NET Core surface. Prefer B when it shows up as noise in
  the Godot build.
- **SSE in Godot.** No native EventSource. The cleanest path is
  `HttpClient.GetStreamAsync` + a hand-rolled line reader on a
  worker thread, posting frames back to the scene tree via
  `CallDeferred`. Alternative: swap the server-side SSE to WebSocket
  (Godot has first-class `WebSocketPeer`). Stay on SSE for now —
  keeps the web client unchanged.
- **Scene lifetimes.** Scene tree autoloads: `Client`
  (HttpClient wrapper), `Identity` (session storage), `RoomSession`
  (active room state + SSE reader). Tabletop scene reads from
  `RoomSession`; lobby transitions by setting `RoomSession.RoomId`.
- **Logging.** Reuse the existing `ILogger` abstraction; register a
  custom `ILoggerProvider` that routes to `GD.Print` /
  `GD.PushWarning` / `GD.PushError`. CLAUDE.md already calls this
  out as the Godot convention.

## Risks

- Godot's `HttpClient` chunked reading has edge cases around
  keep-alive and buffering; may need a small SSE parser test harness.
- Shared DTO types may drift if a future commit changes a record's
  shape in `Ccgnf.Rest` — add a compile-time reference from
  `Ccgnf.Godot` so the build catches it.
- Cross-client clock skew: the phase tracker reads step counts;
  if the two clients process SSE at different rates, both are
  correct but look inconsistent during fast sequences. Same issue
  the web client already has; acceptable.

## Done when

- A human on Godot and a CPU (pre-seated at room create) can play a
  full EMBER vs BULWARK game to GameEnd, all turns rendered on the
  Godot board, SSE frames flowing.
- Web client and Godot client can share a single room (both observe
  the same state, either can drive pending prompts).
- README status table gains a "Godot client" row transitioning from
  "Not started" → "Playable v1".
