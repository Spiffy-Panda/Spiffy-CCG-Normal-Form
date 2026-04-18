# Step 6 — Rooms: Lobby + Tabletop

The multi-consumer play layer. Largest step in the arc; implement
carefully, ship incrementally.

Read [../web/rooms-protocol.md](../web/rooms-protocol.md) first — that
doc is the protocol spec this step implements.

## Deliverables

1. Backend `Rooms` subsystem under `src/Ccgnf.Rest/Rooms/`.
2. All room endpoints live: `POST /api/rooms`, `GET /api/rooms`,
   `GET /api/rooms/{id}`, `POST /api/rooms/{id}/join`,
   `POST /api/rooms/{id}/actions`, `GET /api/rooms/{id}/state`,
   `GET /api/rooms/{id}/events` (SSE), `DELETE /api/rooms/{id}`.
3. TTL eviction via `HostedService`.
4. `LiveInputQueue : IHostInputQueue` for runtime-appended inputs.
5. Frontend `#/play/lobby` and `#/play/tabletop/{id}` routes.
6. Integration tests covering create → join → action → SSE event →
   state.

## Split this step into sub-commits

Rooms is too big for one commit. Split:

### 6a. Room primitives (backend only)

`src/Ccgnf.Rest/Rooms/`:

- `Room.cs` — holds `GameState`, `Scheduler`, `LiveInputQueue`,
  player roster, token map. Per-room `SemaphoreSlim`.
- `RoomStore.cs` — `ConcurrentDictionary<string, Room>`.
- `RoomTtlSweeper.cs` (`HostedService`) — periodic tick; evicts
  rooms past TTL.
- `SseBroadcaster.cs` — fans events out to a list of open responses.
- `LiveInputQueue.cs` — appends inputs from `POST .../actions`;
  await-next for the interpreter's consume.

Tests: unit tests for `LiveInputQueue` (append/drain interleaved),
`RoomStore` TTL eviction, token generation (uniqueness over 1k calls).

### 6b. Room endpoints

`src/Ccgnf.Rest/Endpoints/RoomEndpoints.cs`. Wire up the HTTP surface.
Tests in `tests/Ccgnf.Rest.Tests/RoomEndpointsTests.cs`:

- Create room → 201.
- Join → returns token; second join succeeds; third returns 409.
- Action with wrong token → 401.
- State endpoint reflects the last committed event.
- SSE stream receives `game-event` frames (use `HttpClient` with
  `HttpCompletionOption.ResponseHeadersRead` + line-reader).
- Delete room → 204.

### 6c. Interpreter integration

The event loop today runs synchronously in `Interpreter.Run`. For rooms
we need it to pause when the input queue is empty. Adjust:

- `Scheduler` now accepts an `IHostInputQueue` that can `WaitNextAsync`.
- `Interpreter.Run` gains an `async` variant that yields on empty queue
  until an input arrives OR the room is closed.
- The original synchronous `Run` remains for CLI / sessions (calls
  the async variant with a sealed queue).

This is the trickiest code in the arc. Tests cover:

- Pre-sequenced inputs still produce deterministic state.
- Async run awaits on empty queue and resumes after append.
- Room cancellation cleanly stops the loop.

### 6d. Lobby page

`web/src/pages/play/lobby.ts`. Implementation per
[../web/pages.md](../web/pages.md) §Lobby.

### 6e. Tabletop page

`web/src/pages/play/tabletop.ts`. Implementation per
[../web/pages.md](../web/pages.md) §Tabletop.

Scope of the v1 tabletop:

- Render the `GameStateDto` using `shared/zone-tree.ts`.
- Listen to SSE; refetch full state on each event (diffs later).
- Submit `pass` actions via `POST /api/rooms/{id}/actions`.
- Dropped connection → reconnect with the stored token.

## Configuration

New env vars:

- `CCGNF_ROOM_TTL_SECONDS` — default 600.
- `CCGNF_ROOM_MAX` — default 256.

Document both in `README.md` and `src/Ccgnf.Rest/Program.cs`.

## README status update

- Status-table row moves from "REST — Scaffolded" to "REST — v1
  (rooms)" when this lands.
- Test count updates.

## Commit message templates

```
6a: Add Room primitives: store, TTL sweeper, live input queue, SSE broadcaster
6b: Add /api/rooms endpoints with token-guarded actions and SSE events
6c: Make Interpreter.Run async-compatible with live input queues
6d: Add #/play/lobby page
6e: Add #/play/tabletop page with SSE-driven state refresh
```

## Done when

- All rooms endpoints live; curl flow in
  [../web/rooms-protocol.md](../web/rooms-protocol.md) works end-to-end
  against a running REST host.
- Web lobby + tabletop are usable: create → join → see state → submit
  pass → observe state update.
- TTL sweeper actually reaps; verified with a short-TTL test.
- README status updated.
- Devlog entries for each sub-commit.
