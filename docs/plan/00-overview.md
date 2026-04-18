# Overview

## What we're building

A web app served by `Ccgnf.Rest` on port 19397 that exposes the CCGNF
engine to three front-end consumers: the browser, `curl` / REST bots, and
(later) the Godot host. Five pages:

| Page | Route | Purpose |
|------|-------|---------|
| Cards + Rules | `#/cards` | Browse cards parsed from `encoding/cards/`; rules render as a declaration tree (placeholder for future human-readable render). |
| Decks | `#/decks` | Build a deck with a format selector. Draft format uses a mocked pool until real drafting lands. |
| Interpreter | `#/interpreter` | The current playground, enriched: multi-file editor, per-stage buttons, state tree view. |
| Play | `#/play/lobby`, `#/play/tabletop/{id}` | Lobby for rooms; Tabletop for the active game. |
| Raw | `#/raw` | Files loaded, macros collected, declaration counts, syntax-highlighted view. |

## Why now

`src/Ccgnf.Rest` already exposes every pipeline stage as an independent
endpoint. The web app is the last mile — it turns the raw REST surface
into something a designer, a playtester, and a future Godot client can all
use without knowing the C# library.

## Scope

**In scope for the first delivery arc:**

- Vite frontend under `web/`, pushed through the Makefile so CI stays green.
- Read-only data plane: `/api/cards`, `/api/project`.
- Five pages listed above, in the order in [steps/README.md](steps/README.md).
- Rooms layer on top of the existing `Sessions` infrastructure (in-memory,
  TTL-evicted when empty).

**Out of scope for this arc:**

- Markdown rendering. `design/GameRules.md` and `design/Supplement.md`
  stay as design documents, not app content. The encoding is the source
  of truth for the Rules tab.
- Authentication beyond a per-player non-cryptographic join token.
- Session persistence across server restarts (pure in-memory).
- Interpreter mid-run input (waits on engine v2 — Interrupts, Clash).
- The `Ccgnf.Godot` host. Protocol is designed to accept it, but shipping
  it is a separate arc.

## Delivery order

Detailed plans per step live in [steps/](steps/). Summary:

1. **Vite scaffold** — `web/`, Makefile integration, migrate the current
   playground to `#/interpreter` without feature changes.
2. **Backend data plane** — `/api/cards` and `/api/project`. No engine
   changes; pure projection of the loaded `AstFile`.
3. **Cards + Rules page** — browser of cards; tree-view of declarations
   for the Rules side.
4. **Decks page** — format selector, distribution stats, Draft mock pool.
5. **Raw view** — project file tree, syntax-highlighted `.ccgnf`.
6. **Rooms** — `/api/rooms` + SSE events; Lobby + Tabletop routes.

Each step ends with a commit, passing CI, and an entry in [devlog.md](devlog.md).

## Non-goals of the docs themselves

- Not a spec. `grammar/GrammarSpec.md` is the spec; these docs explain the
  code that implements it.
- Not an auto-generated reference. If a type's public surface changes,
  a human updates [reference/](reference/) in the same PR.
- Not a replacement for reading code when behavior surprises you. These
  digests are accurate *when written*; they age. If a doc disagrees with
  the code, the code wins and the doc gets fixed.

## Reading order for new contributors

1. [01-architecture.md](01-architecture.md) — how the pieces fit.
2. [audit/current.md](audit/current.md) — where we are.
3. Whichever [reference/](reference/) digest covers the module you're
   touching.
4. The specific [steps/](steps/) file for the task, if it exists.
