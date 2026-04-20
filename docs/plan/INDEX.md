# Plan index

Entry point for the web-app planning doc set. **Read this first, then jump
to the one or two files relevant to the task.** Every file below is sized
to be readable in a single pass.

## Orientation

- [00-overview.md](00-overview.md) — goals, scope, delivery sequence, what's in vs out.
- [01-architecture.md](01-architecture.md) — system shape (pipeline, data model, host composition) as UML-flat markdown.

## APIs

- [api/rest.md](api/rest.md) — REST endpoints: live + planned. Shapes, status codes, examples.
- [api/library.md](api/library.md) — C# library surface for host consumers (CLI, REST, Godot).

## Existing-code reference (saves re-reading files)

- [reference/README.md](reference/README.md) — index of per-module digests.
- [reference/ccgnf-lib.md](reference/ccgnf-lib.md) — Preprocessor → Parser → AST → Validator.
- [reference/interpreter.md](reference/interpreter.md) — GameState, Evaluator, Scheduler, Interpreter facade.
- [reference/builtins.md](reference/builtins.md) — every builtin, its signature, and its semantics.
- [reference/ast-nodes.md](reference/ast-nodes.md) — AST record hierarchy.
- [reference/rest.md](reference/rest.md) — REST host composition (endpoints, DTOs, sessions).

## Audit + migration

- [audit/current.md](audit/current.md) — what exists today: strengths, known gaps, coverage gaps.
- [migration/naming.md](migration/naming.md) — proposed renames (files, classes, namespaces) and why.
- [migration/splits.md](migration/splits.md) — files that should split, with suggested cut lines.

## Web-app plan

- [web/overview.md](web/overview.md) — stack choice (Vite), routing, project layout.
- [web/pages.md](web/pages.md) — cards, decks, interpreter, play, raw — detail per page.
- [web/rooms-protocol.md](web/rooms-protocol.md) — room lifecycle, action/event shapes, TTL, auth tokens.
- [web/verification.md](web/verification.md) — agent-facing preview runbook; covers every step's features plus regression signatures.

## Step-by-step plans

- [steps/README.md](steps/README.md) — the ordered sequence; what blocks what.
- [steps/01-vite-scaffold.md](steps/01-vite-scaffold.md)
- [steps/02-cards-project-endpoints.md](steps/02-cards-project-endpoints.md)
- [steps/03-cards-rules-page.md](steps/03-cards-rules-page.md)
- [steps/04-decks-page.md](steps/04-decks-page.md)
- [steps/05-raw-view.md](steps/05-raw-view.md)
- [steps/06-rooms.md](steps/06-rooms.md)
- [steps/07-playtest-mvp.md](steps/07-playtest-mvp.md)
- [steps/08-full-game.md](steps/08-full-game.md)
- [steps/09-godot-client.md](steps/09-godot-client.md)
- [steps/10-long-term-ai.md](steps/10-long-term-ai.md)
- [steps/11-humanizer-templates.md](steps/11-humanizer-templates.md)
- **[steps/12.INDEX-2026-04-20.md](steps/12.INDEX-2026-04-20.md) — state of the step-12 arc. Read this before touching any 12.x file.**
- [steps/12.0-balance-baseline.md](steps/12.0-balance-baseline.md) — balance arc opens with a measurement snapshot. *(closed)*
- [steps/12.1-ai-floor-fix.md](steps/12.1-ai-floor-fix.md) — AI floor rule + ongoing iteration point. *(closed — 2 passes landed)*
- [steps/12.2-engine-sanity-pass.md](steps/12.2-engine-sanity-pass.md) — rules can reach lethal in ≤ 8 turns. *(closed — Knob 3 only)*
- [steps/12.3-card-threat-audit.md](steps/12.3-card-threat-audit.md) — card tags + per-faction closer ratios. *(closed — 19 new closers authored)*
- [steps/12.4-deck-construction.md](steps/12.4-deck-construction.md) — decks use the fixed pool honestly. *(shipped, partial — matched-AI bar deferred to 12.5)*
- [steps/12.5-matched-ai-tune.md](steps/12.5-matched-ai-tune.md) — each deck's matched AI beats mismatched by 15 pp. *(open — next)*
- [steps/12.6-cross-matchup-polish.md](steps/12.6-cross-matchup-polish.md) — matrix inside [25%, 75%]. *(blocked on 12.5)*

## Progress

- [devlog.md](devlog.md) — append-only log, newest first. Update at the end of each work session.

## Conventions for these docs

- Keep each file under ~200 lines. If a section grows, split to a sibling doc.
- Use absolute paths from repo root for code references (`src/Ccgnf/Interpreter/Evaluator.cs:42`).
- Prefer prose + small tables over ASCII diagrams. UML collapses to "X has-a Y" / "X → Y" lines.
- Use present tense for current state, imperative for plans. "The Evaluator dispatches calls through Builtins." vs "Add the /api/cards endpoint."
- Dates in the devlog are YYYY-MM-DD.
- If you revise an invariant in the reference/ docs, also update the code if it's drifted — drift is the whole thing these docs exist to prevent.
