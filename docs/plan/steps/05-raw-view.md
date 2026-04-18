# Step 5 — Raw view

Ships `#/raw`. The diagnostic surface that tells you exactly what files
the REST host has loaded and what's in them.

## Deliverables

1. File tree on the left (grouped by directory).
2. Content viewer on the right with minimal `.ccgnf` syntax highlighting.
3. Metadata panel: collected macros, declaration counts, diagnostics.
4. "Reload project" button that calls `GET /api/project?reload=1`.

## Frontend

```
web/src/pages/raw/
  index.ts                  controller; fetches /api/project
  file-tree.ts              groups files by directory; tracks selection
  file-viewer.ts            fetches + renders /api/project/file?path=…
  highlight.ts              regex-based .ccgnf highlighter
  style.css
```

### Highlighter

Tokens (by regex priority):

| Token | Pattern | Class |
|-------|---------|-------|
| line comment | `//[^\n]*` | `.hl-comment` |
| block comment | `/\*[\s\S]*?\*/` | `.hl-comment` |
| string | `"(?:[^"\\]\|\\.)*"` | `.hl-string` |
| int | `\b\d+\b` | `.hl-number` |
| keyword | `\b(Entity\|Card\|Token\|define\|for\|in\|let\|If\|Switch\|Cond\|When\|Default\|true\|false\|None\|Unbound\|NoOp)\b` | `.hl-keyword` |
| operator | `[∈∧∨∩∪⊆×¬]|->|\+=|==|!=|<=|>=` | `.hl-operator` |

Output: `<pre>` with `<span class="hl-…">`. No contentEditable; view
only.

## Backend

`GET /api/project` already exists from Step 2. Add query parameter
`?reload=1` to `ProjectCatalog.Get(reload)`. Update the endpoint
handler to honor it.

## Tests

`tests/Ccgnf.Rest.Tests/ProjectEndpointsTests.cs`:

- `/api/project?reload=1` returns a snapshot with a later
  `LoadedAt` than `/api/project`.
- `/api/project/file?path=../etc/passwd` still returns `400` / `404`.

Frontend: optional Vitest for `highlight.ts` on a few sample inputs.

## Design decisions

- **No editing.** Raw view is inspection-only. Editing belongs to the
  Interpreter page's textareas.
- **Path traversal guard is server-side.** The UI only links to paths
  the backend returned in `/api/project`; the backend still validates.

## Commit message template

```
Add #/raw page: project file tree with .ccgnf syntax highlighting

Renders /api/project as a directory tree; selecting a file fetches
its content and applies a small regex highlighter. Metadata panel
shows collected macros and declaration counts. Reload button
re-runs the full pipeline against the on-disk files.
```

## Done when

- Tree loads; clicking a file shows content.
- Highlighter renders comments, keywords, strings, and numbers.
- Reload button triggers a fresh load and updates `LoadedAt`.
- Devlog entry added.
