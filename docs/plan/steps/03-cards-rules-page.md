# Step 3 — Cards + Rules page

Builds `#/cards`. Ships the first user-facing feature past the
migrated playground.

## Deliverables

1. Faceted card browser at `#/cards?tab=cards`.
2. Declaration-tree Rules view at `#/cards?tab=rules`.
3. Card detail pane with raw `.ccgnf` source block.
4. Deep-linkable selections (`#/cards?card=Spark`).

## Files to create / modify

```
web/src/pages/cards/
  index.ts            page controller; mounts list + detail + tabs
  filter.ts           pure functions: apply facets to a CardDto[]
  card-list.ts        renders the list; handles selection
  card-detail.ts      renders the detail pane
  rules-tree.ts       renders the declaration tree from ProjectDto
  style.css
web/src/shared/
  chip.ts             tiny reusable chip component (faction, keyword)
```

Update `shared/nav.ts` to surface the Cards link.

## Layout

```
┌──── nav ────────────────────────────────────────────────┐
│ CCGNF   Cards | Decks | Interpreter | Play | Raw   ● ok │
├──── tabs ───────────────────────────────────────────────┤
│ [Cards] [Rules]                                         │
├──── facets ────┬──── list ──────┬──── detail ───────────┤
│ faction  ▢ EMB │  Spark     (1) │  Spark                │
│          ▢ BUL │  Cinderling(1) │  EMBER · Maneuver · 1 │
│ type           │  …             │  rarity C             │
│ cost           │                │  Deal 2 damage…       │
│ rarity         │                │                       │
│ keyword        │                │  ── source ──         │
│                │                │  Card Spark {         │
│                │                │    factions: {EMBER}… │
└────────────────┴────────────────┴───────────────────────┘
```

## Filter semantics

- `faction` — OR across checked factions; empty = show all.
- `type` — OR.
- `cost` — OR across cost buckets ({1, 2, 3, 4, 5, 6+}).
- `rarity` — OR.
- `keyword` — case-insensitive substring match against any of the card's
  keywords.
- All filters AND across facets.

## Rules tree

Structure mirrors what's in `docs/plan/web/pages.md`:

```
Entities (4)                          [Entity 4]
├─ Game                               [Entity]
│   ├─ (field) kind = Game
│   ├─ (field) characteristics { round: 1, first_player: Unbound }
│   ├─ (field) child_zones { Arena[Left], … }
│   └─ abilities: Triggered × 6
│       ├─ on Event.GameStart        · expand to show effect AST
│       └─ on Event.PhaseBegin(Rise) · expand to show effect AST
Cards (250)                           [Card 250]
  └─ (grouped by faction)             expand to see the same list as #/cards?tab=cards
Tokens (3)                            [Token 3]
Augmentations (12)                    [Augment 12]
```

Each expanded leaf shows the raw `.ccgnf` for that declaration with a
note: *"A human-readable render of this will land in a later step."*

## Data flow

1. On route entry: fetch `/api/cards` and `/api/project` in parallel.
2. Store in a page-scoped `state` object.
3. Render tabs; mount the selected tab's subtree.
4. URL changes update `state.selection` / `state.filters` and re-render.

## Tests

- No backend changes, so no new integration tests. Optional: a small
  TS test for `filter.ts` (pure function) with Vitest. Not required
  for this step.

## Design decisions locked in

- **Rules tab does not render markdown.** The declaration tree + raw
  `.ccgnf` is the v1 surface; a later step will parse `.ccgnf` into
  human-readable prose.
- **Design docs remain out-of-app.** `design/GameRules.md` and
  `design/Supplement.md` are designer references, not app content.
- **Draft isn't relevant here.** Cards page shows the full pool;
  format filtering belongs to the Decks page.

## Commit message template

```
Add #/cards page: faceted card browser + rules-tree view

Ships the first real page beyond the migrated playground. Cards
load from /api/cards; rules tab renders /api/project's declaration
tree with raw .ccgnf expressions as placeholders for a future
human-readable render.
```

## Done when

- Cards page loads against a live REST host.
- Filters narrow the list as expected.
- Selecting a card shows a detail pane with the raw source block.
- Rules tab renders a tree with counts per declaration kind.
- `make DOTNET=dotnet.exe ci` passes.
- Devlog entry added.
