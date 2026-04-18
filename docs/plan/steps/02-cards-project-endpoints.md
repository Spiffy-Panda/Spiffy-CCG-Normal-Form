# Step 2 — Cards + Project endpoints

Ship the read-only data plane that the Cards, Decks, Raw, and
Interpreter pages all depend on.

## Deliverables

1. `GET /api/cards` — flat list of `CardDto`s projected from
   `AstCardDecl`s in the loaded project.
2. `GET /api/project` — files loaded, macros collected, declaration
   counts, and a tree of top-level declarations.
3. `GET /api/project/file?path=…` — a single file's raw content; used
   by the Raw view.
4. `POST /api/cards/distribution` — aggregate counts for the deck
   distribution panel.
5. Integration tests for each.

No frontend work in this step; pages that consume these endpoints
come in later steps.

## Data shapes

### `CardDto`

```jsonc
{
  "name": "Spark",
  "factions": ["EMBER"],
  "type": "Maneuver",
  "cost": 1,
  "rarity": "C",
  "keywords": [],
  "text": "Deal 2 damage to a Unit or Conduit. EMBER 3: Deal 3 instead.",
  "sourcePath": "encoding/cards/ember.ccgnf",
  "sourceLine": 13
}
```

- `text` is extracted from the `// text: …` line in the source if
  present; otherwise empty.
- `factions`, `type`, `cost`, `rarity`, `keywords` come from the card's
  body via AST walking.

### `ProjectDto`

```jsonc
{
  "files": [
    { "path": "encoding/engine/04-entities.ccgnf", "bytes": 3412 }
  ],
  "macros": ["SetupSequence", "ChooseFirstPlayer", …],
  "declarations": {
    "counts": { "Entity": 4, "Card": 250, "Token": 3, "Augment": 12 },
    "byFile": {
      "encoding/engine/04-entities.ccgnf": ["Entity Game", "Entity Arena[pos] for pos ∈ …", …]
    }
  }
}
```

### `DistributionDto`

```jsonc
{
  "faction": { "EMBER": 50, "BULWARK": 50, … },
  "type": { "Unit": 120, "Maneuver": 80, "Standard": 50 },
  "cost": { "1": 40, "2": 55, "3": 60, "4": 40, "5": 25, "6+": 30 },
  "rarity": { "C": 150, "U": 70, "R": 25, "M": 5 }
}
```

`POST /api/cards/distribution` body:

```jsonc
{ "cards": ["Spark", "Cinderling", …] }   // optional; null = full pool
```

## Implementation

New files under `src/Ccgnf.Rest/Endpoints/`:

- `CardsEndpoints.cs` — `/api/cards`, `/api/cards/distribution`.
- `ProjectEndpoints.cs` — `/api/project`, `/api/project/file`.

New serialization:

- `Serialization/CardDto.cs`, `ProjectDto.cs`, `DistributionDto.cs`.
- `Serialization/CardMapper.cs` — `AstCardDecl` → `CardDto`. Walks the
  card's `Body.Fields` looking for `factions`, `type`, `cost`, `rarity`,
  `keywords`. Extracts text from trailing `// text:` comments (the
  preprocessor keeps line-comment positions via the token stream; for
  v1, pull text from the raw file content using `AstCardDecl.Span`).

New backend service:

- `Services/ProjectCatalog.cs` — thin cache that holds the most-recent
  loaded `AstFile` + raw file contents, indexed by path. Populated on
  process start from `encoding/` (path configurable via
  `CCGNF_PROJECT_ROOT`, default `encoding/`). Re-loads on demand if a
  `?reload=1` query is passed.

### Loading strategy

The REST host currently loads projects per-request (via `ProjectLoader`
in each endpoint handler). For the read-only endpoints to be fast and
consistent, add a shared `ProjectCatalog`:

```csharp
public sealed class ProjectCatalog {
    ProjectCatalog(ProjectLoader loader, ILogger<ProjectCatalog> log);
    // Lazily loads from disk; reload(true) forces re-read.
    ProjectSnapshot Get(bool reload = false);
}

public sealed record ProjectSnapshot(
    AstFile File,
    IReadOnlyDictionary<string, string> RawContent,   // path -> content
    IReadOnlyList<string> Macros,
    DateTimeOffset LoadedAt);
```

Registered in `Program.cs` as `AddSingleton`. Endpoints depend on
`ProjectCatalog` via DI. Per-request `POST /api/run` etc. still go
through `ProjectLoader` with request-supplied files (they don't touch
the catalog).

## Tests

`tests/Ccgnf.Rest.Tests/CardsEndpointsTests.cs`:

- `/api/cards` against the real encoding returns ≥250 cards, each with
  non-empty `name`, `factions`, `type`.
- Every `CardDto.sourcePath` exists on disk.
- Distribution endpoint with no filter returns totals that sum to the
  card count.
- Distribution with a subset returns matching totals.

`tests/Ccgnf.Rest.Tests/ProjectEndpointsTests.cs`:

- `/api/project` returns ≥22 files and a non-empty macros list.
- `/api/project/file?path=encoding/engine/04-entities.ccgnf` returns
  the expected content.
- `/api/project/file?path=../etc/passwd` returns `400` or `404`
  (no path traversal).

## Commit message template

```
Add /api/cards, /api/project, /api/cards/distribution endpoints

Backs the upcoming web-app Cards / Decks / Raw pages with a
read-only data plane. ProjectCatalog caches the loaded AstFile
plus raw file contents; CardMapper projects AstCardDecls into
UI-friendly DTOs.
```

## Done when

- All three endpoints respond correctly against the real encoding.
- Integration tests pass.
- `docs/plan/api/rest.md` "Planned" section moves the three endpoints
  into "Live".
- Devlog entry added.
