# Reference — REST host composition

Location: `src/Ccgnf.Rest/`. ASP.NET Core minimal-API host.

## Program entry

`Program.cs` — 40 lines.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://localhost:{httpPort}");  // CCGNF_HTTP_PORT or 19397
builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/health", …);
PipelineEndpoints.Map(app);
SessionEndpoints.Map(app);
app.Run();

public partial class Program { }   // exposed for WebApplicationFactory<Program>
```

## Endpoints

### `PipelineEndpoints` — `Endpoints/PipelineEndpoints.cs`

`internal static`. Registers:

- `POST /api/preprocess` → `Preprocess(ProjectRequest, ILoggerFactory)`
- `POST /api/parse` → `Parse(ProjectRequest, ILoggerFactory)`
- `POST /api/ast` → `BuildAst(ProjectRequest, ILoggerFactory)`
- `POST /api/validate` → `Validate(ProjectRequest, ILoggerFactory)`
- `POST /api/run` → `Run(RunRequest, ILoggerFactory)`

Helpers:
- `ToSourceFiles(ProjectRequest|SourceFileDto[])` — request → `SourceFile`s.
- `BuildInputs(string[]?)` — string array → `QueuedInputs`. Recognises `int`, `true`/`false`, bare identifier (→ `RtSymbol`), anything else → `RtString`.

### `SessionEndpoints` — `Endpoints/SessionEndpoints.cs`

`internal static`. Registers:

- `POST /api/sessions` → 201 + `SessionCreateResponse`, 400 on validation failure.
- `GET /api/sessions` → newest-first list.
- `GET /api/sessions/{id}` → metadata or 404.
- `GET /api/sessions/{id}/state` → `GameStateDto` or 404.
- `DELETE /api/sessions/{id}` → 204 or 404.

## Sessions

### `SessionStore` — `Sessions/SessionStore.cs`

```csharp
public sealed class SessionStore {
    GameSession Create(GameState state, int seed);   // new Guid id
    bool TryGet(string id, out GameSession session);
    bool Remove(string id);
    IEnumerable<GameSession> All { get; }
}

public sealed class GameSession {
    string Id { get; }
    GameState State { get; }
    int Seed { get; }
    DateTimeOffset CreatedAt { get; }
}
```

`ConcurrentDictionary` backed; in-memory only; no TTL (rooms will add TTL
in a future step — see [../web/rooms-protocol.md](../web/rooms-protocol.md)).

## Serialization

### `Serialization/Dtos.cs`

Request/response DTOs (plain records):

```csharp
SourceFileDto(Path, Content)
ProjectRequest(Files)
RunRequest(Files, Seed, Inputs, DeckSize)
SessionCreateRequest(Files, Seed, Inputs, DeckSize)
DiagnosticDto(Severity, Code, Message, File, Line, Column)
PreprocessResponse(Ok, Expanded, Diagnostics)
ParseResponse(Ok, TokenCount, Diagnostics)
AstResponse(Ok, DeclarationCount, DeclarationsByKind, Diagnostics)
ValidateResponse(Ok, Diagnostics)
RunResponse(Ok, State, Diagnostics)
SessionCreateResponse(SessionId, State, Diagnostics)
```

### `Serialization/DiagnosticMapper.cs`

`internal static`. Single method `ToDtos(IEnumerable<Diagnostic>)` →
`IReadOnlyList<DiagnosticDto>`.

### `Serialization/GameStateDto.cs`

#### `GameStateDto`

```jsonc
{
  "stepCount": 3,
  "gameOver": false,
  "gameId": 1,
  "playerIds": [6, 7],
  "arenaIds": [2, 3, 4],
  "entities": [ EntityDto, … ],
  "pending": [ EventDto, … ]
}
```

#### `EntityDto`

```jsonc
{
  "id": 6,
  "kind": "Player",
  "displayName": "Player1",
  "ownerId": null,
  "characteristics": { "integrity_start": "7", "aether_cap_schedule": "[3,4,…]" },
  "counters": { "aether": 3, "debt": 0 },
  "parameters": { "i": "1" },
  "zones": {
    "Arsenal": { "order": "Sequential", "capacity": null, "contents": [19, 20, …] },
    …
  },
  "abilityCount": 0
}
```

#### `ZoneDto`

```jsonc
{ "order": "Unordered", "capacity": 10, "contents": [19, 20, 21] }
```

#### `EventDto`

```jsonc
{ "type": "PhaseBegin", "fields": { "phase": "Rise", "player": "#6" } }
```

### `StateMapper`

`public static`. Methods:

- `ToDto(GameState)` → `GameStateDto`
- `ToEntityDto(Entity)` → `EntityDto`
- `ToEventDto(GameEvent)` → `EventDto`

RtValue formatting (in the `Format` private helper):

| Runtime value | DTO string |
|---------------|------------|
| `RtInt(7)` | `"7"` |
| `RtString("x")` | `"x"` |
| `RtBool(true)` | `"true"` |
| `RtSymbol("Rise")` | `"Rise"` |
| `RtEntityRef(6)` | `"#6"` |
| `RtZoneRef(6, "Hand")` | `"#6.Hand"` |
| `RtNone` | `"None"` |
| `RtUnbound` | `"Unbound"` |
| other | `ToString()` fallback |

All DTO values are strings (or dicts/lists thereof) to keep JSON schemas
stable across runtime-type additions. Numeric counters are preserved as
int to stay friendly for UI arithmetic.

## Static assets

`wwwroot/index.html` — single-file playground. Will be replaced by the
Vite-built frontend (step 1 of the web-app arc); the current file moves
to `#/interpreter` as the first page.

## Logging

Default ASP.NET Core logging (console). Library logs flow through the
DI-provided `ILoggerFactory`; every endpoint accepts `ILoggerFactory` as
a parameter and creates scoped loggers through it.

## Tests

`tests/Ccgnf.Rest.Tests/EndpointsTests.cs` uses
`WebApplicationFactory<Program>`. Covers health, each pipeline stage, a
full `/api/run` against the real encoding (asserts 2 players, 3 arenas,
6 conduits), session create/read/delete, and static playground HTML.
