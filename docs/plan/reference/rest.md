# Reference — REST host composition

Location: `src/Ccgnf.Rest/`. ASP.NET Core minimal-API host.

## Program entry

`Program.cs` — ~45 lines.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://localhost:{httpPort}");  // CCGNF_HTTP_PORT or 19397
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<ProjectCatalog>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/health", …);
PipelineEndpoints.Map(app);
SessionEndpoints.Map(app);
CardsEndpoints.Map(app);
ProjectEndpoints.Map(app);
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

### `CardsEndpoints` — `Endpoints/CardsEndpoints.cs`

`internal static`. Registers:

- `GET /api/cards` → `CardDto[]` projected from the catalog.
- `POST /api/cards/distribution` → `DistributionDto` aggregated over the
  full pool or an optional `cards` filter.

Helper `CardsFrom(ProjectSnapshot)` is reused by distribution aggregation.

### `ProjectEndpoints` — `Endpoints/ProjectEndpoints.cs`

`internal static`. Registers:

- `GET /api/project` → `ProjectDto` (files, macros, declaration counts +
  by-file index, snapshot timestamp).
- `GET /api/project/file?path=...` → raw `text/plain` content, or 400
  (malformed / traversal path) / 404 (unknown path).

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

## ProjectCatalog

### `ProjectCatalog` — `Services/ProjectCatalog.cs`

```csharp
public sealed class ProjectCatalog {
    ProjectCatalog(ILogger<ProjectCatalog>, ILoggerFactory);
    ProjectSnapshot Get(bool reload = false);   // lazy; locks; thread-safe
}

public sealed record ProjectSnapshot(
    AstFile? File,
    IReadOnlyDictionary<string, string> RawContent,
    IReadOnlyList<string> MacroNames,
    IReadOnlyDictionary<string, IReadOnlyList<FileDeclaration>> FileDeclarations,
    IReadOnlyDictionary<string, DeclarationLocation> CardLocations,
    IReadOnlyDictionary<string, DeclarationLocation> EntityLocations,
    IReadOnlyDictionary<string, DeclarationLocation> TokenLocations,
    DateTimeOffset LoadedAt);

public sealed record FileDeclaration(string Kind, string Name, string Label, int Line);
public sealed record DeclarationLocation(string Path, int Line);
```

Locates the repo by walking up from `AppContext.BaseDirectory` until
`Ccgnf.sln` is found, then loads every `*.ccgnf` under
`{repoRoot}/{CCGNF_PROJECT_ROOT ?? "encoding"}`. Paths stored in
`RawContent`, `FileDeclarations`, and `*Locations` are repo-relative with
forward slashes.

Declarations are recovered by regex-scanning the raw content for `Card`,
`Entity`, `Token`, and `Augment` forms (the last matches `target.path +=`
or `target.path = …` where the target contains at least one `.` or
`[…]`). The preprocessor concatenates sources before handing a single
string to the parser, so AST `SourceSpan.File` collapses to `<project>`
for every declaration — the scan is the only way to recover real file +
line metadata. `CardLocations` / `EntityLocations` / `TokenLocations` are
name-keyed views derived from the scan; `FileDeclarations` preserves the
full per-file list, which matters for `Augment` targets that recur
across files.

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

### `Serialization/CardDto.cs`

```csharp
CardDto(Name, Factions, Type, Cost, Rarity, Keywords, Text, SourcePath, SourceLine)
DistributionRequest(Cards)            // nullable; null filter = full pool
DistributionDto(Faction, Type, Cost, Rarity)   // all IReadOnlyDictionary<string,int>
```

### `Serialization/ProjectDto.cs`

```csharp
ProjectFileDto(Path, Bytes)
ProjectDeclarationEntry(Label, Line)
ProjectDeclarationsDto(Counts, ByFile)              // ByFile value: IReadOnlyList<ProjectDeclarationEntry>
ProjectDto(Files, Macros, Declarations, LoadedAt)   // LoadedAt is ISO-8601 string
```

### `Serialization/CardMapper.cs`

`public static`. Method `ToDto(AstCardDecl, string? rawContent, string sourcePath, int sourceLine)`
projects the card block into a `CardDto`. `factions` / `type` / `cost` /
`rarity` / `keywords` come from walking `Body.Fields`; `text` is
recovered by scanning `rawContent` from `sourceLine` forward for the
first `// text:` line inside the card's brace pair.

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
