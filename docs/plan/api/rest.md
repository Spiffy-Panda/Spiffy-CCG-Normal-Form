# REST API

Host: `Ccgnf.Rest` on `http://localhost:19397` (override: `CCGNF_HTTP_PORT`).
All endpoints return `application/json` unless noted.

## Shared shapes

```jsonc
// SourceFileDto
{ "path": "foo.ccgnf", "content": "Entity Foo { kind: Foo }" }

// ProjectRequest (body of all pipeline endpoints)
{ "files": [ SourceFileDto, … ] }

// DiagnosticDto
{
  "severity": "Error" | "Warning" | "Info",
  "code": "V100",
  "message": "Duplicate top-level name 'Foo' …",
  "file": "foo.ccgnf",
  "line": 2,
  "column": 1
}
```

## Live (v1)

### `GET /api/health`

Smoke check + port advertisement. Used by the playground header.

```jsonc
// 200
{ "ok": true, "service": "ccgnf.rest", "port": 19397 }
```

### `POST /api/preprocess`

Expand `define` macros, return post-preprocess text plus any diagnostics.

**Request:** `ProjectRequest`
**Response (200):**
```jsonc
{
  "ok": true,
  "expanded": "Entity Foo { kind: Foo }\n…",
  "diagnostics": []
}
```

### `POST /api/parse`

Preprocess + run through ANTLR. Returns whether the parse succeeded and a
coarse token-count signal (parse-tree direct child count).

**Request:** `ProjectRequest`
**Response (200):**
```jsonc
{ "ok": true, "tokenCount": 1126, "diagnostics": [] }
```

### `POST /api/ast`

Preprocess + parse + build typed AST. Returns declaration counts by kind.

**Request:** `ProjectRequest`
**Response (200):**
```jsonc
{
  "ok": true,
  "declarationCount": 11,
  "declarationsByKind": { "Entity": 4, "Augment": 6, "Card": 1, "Other": 0 },
  "diagnostics": []
}
```

### `POST /api/validate`

Full pipeline up to and including the validator. Returns diagnostics.

**Request:** `ProjectRequest`
**Response (200):**
```jsonc
{ "ok": true, "diagnostics": [] }
```

### `POST /api/run`

Execute the v1 interpreter (Setup → Round-1 Rise). Returns the full
`GameStateDto`.

**Request:**
```jsonc
{
  "files": [ SourceFileDto, … ],
  "seed": 42,
  "inputs": ["pass", "pass", "pass", "pass"],   // optional; FIFO host-input queue
  "deckSize": 30                                // optional; seeded default
}
```

**Response (200):**
```jsonc
{
  "ok": true,
  "state": GameStateDto,
  "diagnostics": []
}
```

`GameStateDto` shape is documented in
[../reference/rest.md](../reference/rest.md#gamestatedto).

### `POST /api/sessions`

Create a resident session by running the interpreter once and holding the
resulting `GameState` under a random id.

**Request:** same shape as `/api/run`.
**Response (201):**
```jsonc
{
  "sessionId": "3f2b…",
  "state": GameStateDto,
  "diagnostics": []
}
```

`400` if validation fails. The session is read-only in v1 (no actions yet).

### `GET /api/sessions`

List active sessions, newest first.

```jsonc
[
  {
    "sessionId": "3f2b…",
    "seed": 42,
    "createdAt": "2026-04-18T…",
    "stepCount": 3,
    "gameOver": false
  }
]
```

### `GET /api/sessions/{id}`

Metadata only:
```jsonc
{ "sessionId": "3f2b…", "seed": 42, "createdAt": "…" }
```
`404` if unknown.

### `GET /api/sessions/{id}/state`

Current `GameStateDto`. `404` if unknown.

### `DELETE /api/sessions/{id}`

`204 No Content` on success, `404` if unknown.

## Planned (web-app arc)

### `GET /api/project`

Advertises what's loaded: files, macro names, declaration counts. Backs
the Raw view and seeds the Interpreter editor.

```jsonc
{
  "files": [
    { "path": "encoding/engine/04-entities.ccgnf", "bytes": 3412, "stage": "validated" }
  ],
  "macros": ["SetupSequence", "ChooseFirstPlayer", …],
  "declarations": { "Entity": 4, "Augment": 12, "Card": 250 }
}
```

### `GET /api/cards`

Projection of `AstCardDecl`s into a UI-friendly list.

```jsonc
[
  {
    "name": "Spark",
    "factions": ["EMBER"],
    "type": "Maneuver",
    "cost": 1,
    "rarity": "C",
    "keywords": [],
    "text": "Deal 2 damage to a Unit or Conduit. EMBER 3: Deal 3 instead.",
    "abilitiesAst": "OnResolve(…)"    // raw .ccgnf expression, source for future pretty-print
  }
]
```

### `GET /api/cards/distribution?format={name}`

Returns aggregate counts per faction / type / cost / rarity for the
current card pool, filtered by format. Backs the deck-building stats panel.

### `POST /api/decks/mock-pool`

Returns a synthetic card pool for the Decks page when a format (e.g.
Draft) needs a pool before a real drafting system exists.

```jsonc
// Request
{ "format": "draft", "seed": 1234, "size": 40 }
// Response
{ "cards": [ /* 40 card names from /api/cards */ ] }
```

## Rooms (multi-consumer play)

Full protocol detail in [../web/rooms-protocol.md](../web/rooms-protocol.md).
Endpoint summary:

```
POST   /api/rooms                          create room
GET    /api/rooms                          list rooms (open ones first)
GET    /api/rooms/{id}                     room metadata
POST   /api/rooms/{id}/join                claim a seat; returns { playerId, token }
POST   /api/rooms/{id}/actions             submit an action (requires token)
GET    /api/rooms/{id}/state               current GameStateDto
GET    /api/rooms/{id}/events              Server-Sent Events stream
DELETE /api/rooms/{id}                     close (host only)
```

## Error conventions

- Validation failures return `200` with `ok: false` and the diagnostics
  list filled. These are expected outcomes, not errors.
- Malformed requests return `400`.
- Missing sessions / rooms return `404`.
- Server bugs return `500` with a minimal JSON body.
- Never leak server stack traces in responses.
