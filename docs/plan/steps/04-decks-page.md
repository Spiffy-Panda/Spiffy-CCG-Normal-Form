# Step 4 — Decks page

Adds `#/decks`. The first page with non-trivial client state (deck in
progress, persistence, mock-pool fallback).

## Deliverables

1. Format selector (Constructed, Draft; extensible table).
2. Card pool pane: Constructed = full `/api/cards`; Draft = mocked.
3. Deck list with `+` / `−` per card and a max-copies guard per format.
4. Live distribution panel (faction / type / cost / rarity bars).
5. Save / load to `localStorage`; "New deck" resets.
6. `POST /api/decks/mock-pool` backend endpoint.

## Backend

### `POST /api/decks/mock-pool`

```jsonc
// Request
{ "format": "draft", "seed": 1234, "size": 40 }
// Response
{ "format": "draft", "seed": 1234, "cards": ["Spark", "Cinderling", …] }
```

Implementation: sample N cards from the `ProjectCatalog` using the
scheduler RNG seeded with `seed`. Weighted by rarity (mirrors
`tools/cluster-rarity-weights.py`). Pool size defaults to 40 cards if
omitted. If `format == "constructed"`, return the full pool
(sanity mode for the UI to call uniformly).

Put in `src/Ccgnf.Rest/Endpoints/DecksEndpoints.cs`. Tests in
`tests/Ccgnf.Rest.Tests/DecksEndpointsTests.cs`:

- Same seed → same pool.
- Size <= 0 or > pool total returns `400`.
- Unknown format returns `400`.

## Frontend

```
web/src/pages/decks/
  index.ts                    controller
  format-selector.ts          reads a static FORMATS table; emits
                              { name, maxCopies, deckSize, needsMockPool }
  card-pool.ts                fetches + renders
  deck-list.ts                deck in progress + copy counts
  distribution.ts             pure function over deck -> bars
  persistence.ts              localStorage helpers; keys: "deck:<format>:<name>"
  style.css
```

### Formats table (v1)

Defined in `web/src/pages/decks/format-selector.ts`:

```ts
export const FORMATS = [
  { id: "constructed", label: "Constructed", maxCopies: 4, deckSize: 40, needsMockPool: false },
  { id: "draft",       label: "Draft (mock)", maxCopies: 4, deckSize: 40, needsMockPool: true },
];
```

When the user switches to a format with `needsMockPool`, call
`/api/decks/mock-pool` and use the returned subset as the pool.

### Max-copies enforcement

Pure UI guard. Backend does not validate deck composition in v1.

### Distribution bars

`distribution.ts` exports:

```ts
export function summarize(deck: { name: string; count: number }[], cards: CardDto[]): DistributionDto
```

Client-side computation so the UI is responsive. For a full-pool
summary (not a specific deck), optionally fetch from
`POST /api/cards/distribution` — the server authority.

## Persistence

```ts
type SavedDeck = {
  name: string;
  format: string;
  createdAt: string;
  cards: { name: string; count: number }[];
};

localStorage.setItem(`deck:${format}:${name}`, JSON.stringify(saved));
```

UI exposes a "Save" button (prompt for name) and a "Load" dropdown
that enumerates keys matching the current format.

## Tests

Backend:

- `DecksEndpointsTests.MockPool_DeterministicForSameSeed`
- `DecksEndpointsTests.MockPool_RespectsSize`
- `DecksEndpointsTests.MockPool_UnknownFormat_Returns400`

Frontend: optional Vitest for `distribution.ts` and the max-copies
guard.

## Design decisions locked in

- **Format selector is mandatory.** No "free build" mode that ignores
  format rules in v1.
- **Draft uses a mock pool until real drafting lands.** Clearly labeled
  in the UI.
- **Deck persistence is client-side only in v1.** No `/api/decks`.

## Commit message template

```
Add #/decks page with format selector and mock draft pool

POST /api/decks/mock-pool samples cards from the loaded project
using a seeded RNG; the frontend renders a format-aware pool, a
deck list with max-copies enforcement, and live distribution bars.
localStorage persists named decks per format.
```

## Done when

- Switching formats updates pool + constraints.
- Distribution bars animate as cards are added/removed.
- Decks save and reload by name.
- Backend test passes; frontend works against a live REST host.
- Devlog entry added.
