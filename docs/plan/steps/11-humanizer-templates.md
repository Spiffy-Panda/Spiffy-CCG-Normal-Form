## Step 11 — User-adjustable humanization template library

The humanizer at
[`src/Ccgnf.Rest/Rendering/AstHumanizer.cs`](../../../src/Ccgnf.Rest/Rendering/AstHumanizer.cs)
turns AST ability nodes into the short English lines that appear on
the Cards page and in the tabletop inspector ("Deal 2 damage to a
target Unit or Conduit. EMBER 3: Deal 3 instead."). Today it's a
dictionary of C# format strings keyed by builtin name, compiled in.

Moving templates to a user-editable library lets non-programmers
fix wording, localise to other languages, and prototype card-text
changes without rebuilding. The code path stays fast — templates
load once at host startup and are cached per-session.

Read first: the existing humanizer and its golden tests
(`tests/Ccgnf.Rendering.Tests/AstHumanizerGolden.cs`), the Cards
browser
([`web/src/pages/cards/index.ts`](../../../web/src/pages/cards/index.ts))
so the UX of a wording change propagates cleanly.

## Scope — in

- Extract the in-code template table to a versioned data file
  (JSON; YAML is tempting but adds a dependency).
- Overlay mechanism: shipped defaults + per-project overrides + per-
  user overrides, resolved in that order.
- Template language: simple `{name}` substitution with a handful of
  conditional / pluralisation helpers (no full Mustache; we're not
  building a template engine).
- Web UI to view + edit templates, with a live "before / after"
  preview.
- Hot-reload via `ProjectCatalog` (same mechanism the raw-file
  viewer already uses).
- Regression test: the existing golden snapshots stay canonical —
  the default JSON must render byte-identical to the current C#
  table before and after extraction.

## Scope — out

- Full i18n / pluralisation rules (CLDR). Keep it to a couple of
  English-only helpers this step.
- A template-authoring mini-language with loops and computed
  expressions. A template should be one or two lines; anything
  richer goes back into C# as a new helper.
- Persistence across a new user's sessions — per-user overrides live
  in localStorage for now. A server-side user store is a separate
  product.

## Template language

A template is a string with `{name}` placeholders and an optional
`[?name: …|…]` ternary.

```
Draw {n}                                 → "Draw 3"
Deal {n} damage to {target}              → "Deal 2 damage to a target Unit"
Deal {n} damage to {target}[?target=self: yourself|<no-op>]
                                         → suppresses the else clause
Discard {n} card[?n=1:|s]                → simple pluraliser
```

- **`{name}`** — mandatory placeholder. Template fails loudly if the
  binding is missing (render emits `⟪{name}⟫` so gaps are visible,
  matching the current unknown-node fallback).
- **`[?binding=literal: THEN | ELSE]`** — ternary. Left-hand side is
  an equality test against a string binding value; empty branches
  are allowed.
- Intentionally no nested templates, no conditionals more complex
  than one equality.

If someone writes a template that needs more, they've outgrown the
library — the code path is still there for exotic cases.

## File layout

```
encoding/humanizer/
  README.md                     -- explains the language + overlay order
  defaults.json                 -- shipped library, canonical
  schema.json                   -- JSON schema for editor autocomplete

per-project (optional):
  encoding/humanizer/overrides.json

per-user (optional):
  localStorage `humanizer:overrides`   -- web UI writes here
```

`defaults.json` shape:

```json
{
  "version": 1,
  "templates": {
    "Draw":      "Draw {n}",
    "Damage":    "Deal {n} damage to {target}",
    "SetFlag":   "",
    "NoOp":      ""
  },
  "fallback": "⟪{__raw}⟫",
  "helpers": {
    "targetKind": {
      "Unit":    "a target Unit",
      "Conduit": "a target Conduit",
      "Player":  "a target player"
    }
  }
}
```

Keys are builtin names (matching the dispatch in
[`src/Ccgnf/Interpreter/Builtins.cs`](../../../src/Ccgnf/Interpreter/Builtins.cs)).
An empty-string template means "emit nothing" — used today for
`NoOp`, `Guard`, the phase-internal stubs.

## Sub-commits

### 11a. Extract defaults.json + loader

- Move the C# table into `encoding/humanizer/defaults.json`.
  Content identical, order preserved (one-to-one with current
  humanizer code).
- `HumanizerTemplates` class that loads the JSON at startup and
  exposes `TryRender(builtinName, bindings) → string?`.
- `AstHumanizer` uses `HumanizerTemplates` — no behavior change.
- Regression: re-run
  `tests/Ccgnf.Rendering.Tests/AstHumanizerGolden.cs`. Every faction
  file's snapshot must be byte-identical.

### 11b. Overlay resolution

- `ProjectCatalog` loads `encoding/humanizer/overrides.json` if
  present and merges on top of `defaults.json` (per-key override;
  unspecified keys fall through).
- `GET /api/humanizer/templates` returns the resolved merged table
  + a diff view (which keys came from defaults vs overrides).
- `POST /api/humanizer/templates` accepts a single template change
  in memory (no disk write — persistence is overrides.json which
  is version-controlled).
- Reload hook: the existing `reload=1` on `/api/project` also
  re-reads the humanizer files.

### 11c. Web editor

- New page `#/humanizer` in the web app.
- Left pane: filterable list of builtins. Right pane: current
  template (editable textarea) + a "live preview" panel that picks
  a representative AST node from the corpus and renders it with
  the edited template.
- Shows source: "default" / "project override" / "your session
  (unsaved)" per entry; "save to overrides" button POSTs the
  session override back.
- "Reset to default" per-key.
- Per-user session overrides live in localStorage
  (`humanizer:overrides`). The web tabletop + cards page read
  localStorage and merge on top of the server's table.

### 11d. Editor UX refinements (polish)

- Syntax-highlight the `{name}` and `[?…]` placeholders.
- Inline validation: flag bindings that don't appear in the sample
  AST node the template applies to.
- Before/after diff of the affected card descriptions when you edit
  a shared template (e.g. editing `Damage` shows Spark, Smolder,
  MatchStrike changes all at once).
- Keyboard: Ctrl-Enter saves, Esc reverts.

### 11e. Localization hook (optional, may split out)

- Overlay files can be suffixed with a locale
  (`defaults.en.json`, `defaults.fr.json`). The host picks by
  `Accept-Language` header (or an explicit `?lang=` query).
- Shipping a French pack is out of scope — this lands only the
  resolution mechanism so a community pack is possible.

## Things to watch

- **Golden snapshot drift.** If the extraction changes even
  whitespace, every faction snapshot diff looks noisy. Keep the
  extraction mechanical (compare in CI, fail if differ). Land the
  JSON separately from any wording tweak.
- **Cache invalidation.** Overrides that change in JSON should
  re-render live cards without a server restart. Hook
  `ProjectCatalog.Reload` and invalidate the cards-endpoint cache
  in the same call.
- **User overrides are load-bearing.** If a user breaks a template
  with bad syntax, the fallback must still render something the
  game can display — never an exception. The existing
  `⟪<raw-render>⟫` fallback handles this.

## Done when

- Defaults file ships with the repo; the existing golden
  snapshots pass unchanged.
- A user can open `#/humanizer`, edit a template, see every
  affected card update live on the cards browser, save the
  override, and have it persist across a page reload.
- The humanizer library resolves in this order for every query,
  documented and tested: user session → project overrides → shipped
  defaults → `⟪<raw>⟫` fallback.
- README status row for "Humanizer library" transitions from "In
  code" to "Data-driven, user-editable."
