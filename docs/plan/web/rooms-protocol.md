# Rooms protocol

Multi-consumer play lives in **rooms**. One server-authoritative
`GameState` per room; clients (web, curl, Godot) observe via SSE and
submit actions via POST. Pure client-server; the server is the single
source of truth.

## Invariants

1. Every action is appended to the room's `IHostInputQueue` in receipt
   order. Replaying the same queue against a fresh `Scheduler(seed)`
   reproduces the game bit-for-bit — the determinism contract the
   interpreter already enforces.
2. A room is in exactly one state: `WaitingForPlayers` → `Active` →
   `Finished`. Transitions are atomic under a per-room lock.
3. Rooms are evicted when (a) they are `Finished` **and** empty for
   the TTL, or (b) they are `WaitingForPlayers` with zero joined
   players for the TTL. Default TTL: 10 minutes. Configurable via
   `CCGNF_ROOM_TTL_SECONDS` env var.
4. Tokens returned on `join` are non-cryptographic random 128-bit
   strings, unique to the (room, playerId) pair. Every action must
   carry its token. Lost token → ask the host to kick and re-join.

## Lifecycle

### Create — `POST /api/rooms`

```jsonc
// Request
{
  "files": [ SourceFileDto, … ],    // same as /api/run
  "seed": 1234,
  "playerSlots": 2,                 // 2 for Resonance
  "deckSize": 30                    // optional
}

// Response 201
{
  "roomId": "r_8f…",
  "state": "WaitingForPlayers",
  "seed": 1234,
  "playerSlots": 2,
  "occupied": 0,
  "createdAt": "2026-04-18T…",
  "expiresAt": "2026-04-18T…"       // computed from TTL
}
```

### List — `GET /api/rooms`

Returns all non-evicted rooms, newest first. Public metadata only; no
tokens, no player identities.

### Metadata — `GET /api/rooms/{id}`

Same shape as the Create response, plus:

```jsonc
{ …, "players": [ { "playerId": 1, "name": "alice", "connected": true }, … ] }
```

### Join — `POST /api/rooms/{id}/join`

```jsonc
// Request
{ "name": "alice" }                  // optional display name

// Response 200
{
  "playerId": 1,                     // 1 or 2
  "token": "tok_…",                  // keep it; every action needs it
  "state": GameStateDto
}
```

- `409 Conflict` if the room is full or finished.
- When `occupied == playerSlots`, the room transitions to `Active`
  and the interpreter's event loop begins; `Event.GameStart` is
  enqueued. The first SSE event to all connected clients is
  `{ "type": "RoomStarted", "fields": {} }`.

### Actions — `POST /api/rooms/{id}/actions`

```jsonc
// Request
{
  "playerId": 1,
  "token": "tok_…",
  "action": "pass",                  // matches Choice keys / future action names
  "args": { }                        // per-action payload
}

// Response 202
{ "accepted": true }
```

- `401` if the token doesn't match `playerId`.
- `409` if the action would violate sequencing (not your turn,
  action not legal in the current phase). v1 enforcement is lenient
  — the engine accepts inputs and will respond with a diagnostic
  event if the input is malformed.
- The action is appended to the room's `IHostInputQueue`. The event
  loop picks it up on its next `Choice`/`Target` consumption.

### State — `GET /api/rooms/{id}/state`

Returns current `GameStateDto`. Unchanged from `/api/sessions/{id}/state`.

### Events — `GET /api/rooms/{id}/events` (SSE)

```
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive
```

Frame format:

```
event: game-event
data: { "step": 12, "event": EventDto, "stateDiff": { … } }

```

- `event:` is always `game-event` in v1. Future: `room-meta` for
  join/leave, `heartbeat` every 30s.
- `stateDiff` is omitted in v1; full state is re-fetched via
  `GET /api/rooms/{id}/state` when needed. Diffs are a later
  optimization.
- Sent when: a new event is committed by the interpreter; a player
  joins/leaves; the room state transitions.

### Close — `DELETE /api/rooms/{id}`

Host-only. Returns `204`. Forces the room to `Finished`; triggers
eviction immediately.

## Consumer patterns

### Web client

```ts
const room = await client.createRoom({ files, seed });
const { playerId, token } = await client.joinRoom(room.roomId);
const es = new EventSource(`/api/rooms/${room.roomId}/events`);
es.onmessage = ev => applyEvent(JSON.parse(ev.data));
// on user input:
await client.submitAction(room.roomId, { playerId, token, action: "pass" });
```

### curl / bot

```bash
# Create
ROOM=$(curl -s -X POST localhost:19397/api/rooms \
  -H 'content-type: application/json' \
  -d @room-request.json | jq -r .roomId)

# Join
RESP=$(curl -s -X POST localhost:19397/api/rooms/$ROOM/join \
  -H 'content-type: application/json' \
  -d '{"name":"bot"}')
PID=$(echo "$RESP" | jq -r .playerId)
TOK=$(echo "$RESP" | jq -r .token)

# Stream events
curl -N localhost:19397/api/rooms/$ROOM/events &

# Submit an action
curl -X POST localhost:19397/api/rooms/$ROOM/actions \
  -H 'content-type: application/json' \
  -d "{\"playerId\":$PID,\"token\":\"$TOK\",\"action\":\"pass\"}"
```

### Godot (future)

`HTTPRequest` for POST/GET, plus a minimal SSE client reading from a
long-running `HTTPClient`. No new protocol surface; Godot reuses the
same endpoints as the web client.

## Server implementation notes

- New files under `src/Ccgnf.Rest/Rooms/`:
  - `RoomStore.cs` — `ConcurrentDictionary<string, Room>` with TTL
    sweeper (`HostedService`).
  - `Room.cs` — holds `GameState`, `Scheduler`, player roster,
    token map, event subscribers. Per-room `SemaphoreSlim` for
    action serialization.
  - `RoomEndpoints.cs` — minimal-API registrations.
  - `SseBroadcaster.cs` — fan-out helper; keeps a list of
    `HttpResponse` streams, handles client disconnects.
- `IHostInputQueue` extends to support **live append**
  (`TryNext`/`WaitForNext`); the existing `QueuedInputs` stays as the
  pre-sequenced implementation. Add `LiveInputQueue : IHostInputQueue`.
- The interpreter's event loop runs on a dedicated `Task` per room.
  When the queue empties mid-game (awaiting input), the loop awaits
  the next append.

## Security posture

- `POST /api/rooms` is currently open. For LAN play this is fine. If
  the host ever listens on a non-loopback interface, put the service
  behind a reverse proxy with auth.
- Token entropy is 128 bits from `RandomNumberGenerator.GetBytes` —
  enough to resist guessing but not to protect against a local
  adversary with access to the REST API logs.

## What's explicitly deferred

- **Diff-based state updates.** Currently every observer re-fetches on
  demand; SSE events carry the small `EventDto` and the client refetches
  state if it cares. Diffs when observable lag becomes a problem.
- **Spectators.** A `GET /api/rooms/{id}/events` connection from a
  client without a `playerId` should work (read-only). Not in v1.
- **Reconnect / session resume.** Sockets drop, client reconnects with
  its stored token, server resumes the event stream from last-seen
  step. Not in v1 but the SSE format accommodates a `Last-Event-Id`
  header when we get there.
