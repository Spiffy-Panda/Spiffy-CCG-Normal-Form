# Web — overview

Frontend stack and project layout for the Resonance web app. Ships as a
Vite-built single-page app served by `Ccgnf.Rest`.

## Stack

- **Build tool:** Vite (plain TS + Vite; no React/Vue/Svelte v1).
- **Language:** TypeScript for all app code. Hand-written DTO types to
  match the REST surface (`src/Ccgnf.Rest/Serialization/Dtos.cs`).
  Codegen is a future concern.
- **Router:** hash-based (`#/cards`, `#/play/tabletop/{id}`) — no
  server-side routing dependency.
- **Styling:** single CSS file + CSS custom properties. No framework.
- **API client:** a tiny `api.ts` wrapper over `fetch` and `EventSource`.

Rationale: keeps the frontend honest (no megabyte runtime), aligns with
the library's "hosts don't drag in framework dependencies" stance, and
Vite gives us HMR without a framework opinion.

## Project layout

```
web/
  index.html                       Vite entry
  src/
    main.ts                        router + app shell bootstrap
    router.ts                      hash routing
    api/
      client.ts                    typed fetch wrappers
      dtos.ts                      TypeScript mirrors of Ccgnf.Rest DTOs
      sse.ts                       EventSource helpers (for Rooms)
    pages/
      cards/
        index.ts                   view + controller
        filter.ts                  faceted filter logic
        view.css
      decks/
        index.ts
        format-selector.ts
        distribution.ts            (consumes /api/cards/distribution)
      interpreter/
        index.ts                   migrated from wwwroot/index.html
      play/
        lobby.ts
        tabletop.ts
        room-protocol.ts           (EventSource + action client)
      raw/
        index.ts
        highlight.ts               minimal .ccgnf highlighter
    shared/
      nav.ts
      status-pill.ts
      zone-tree.ts                 for the interpreter + tabletop views
  public/                          static assets
  tsconfig.json
  vite.config.ts
  package.json
```

## Integration with the REST host

Vite builds to `web/dist/`. The REST host serves `wwwroot/`.
Two options:

1. **Copy at build time.** `make web` builds under `web/dist/`, a post-
   build step copies into `src/Ccgnf.Rest/wwwroot/`. `make ci` triggers
   `make web` before packaging the REST app. The output is committed-
   gitignored so CI regenerates it.
2. **Reverse proxy in dev.** Vite dev server on 5173 proxies `/api/*` to
   19397. For `make rest`-style production, keep option 1.

Recommendation: **both**. Dev uses the Vite server with proxy; prod
bundles into `wwwroot/`. `make rest` serves the bundled prod build.

## Makefile additions

```make
.PHONY: web web-dev web-build

# Install the Node toolchain. Idempotent.
web:
	cd web && npm install

# HMR dev server. Expects Ccgnf.Rest running separately on 19397.
web-dev: web
	cd web && npm run dev

# Production build; writes into src/Ccgnf.Rest/wwwroot/.
web-build: web
	cd web && npm run build
```

`make ci` continues to run dotnet build + tests; it does NOT require
Node, so CI stays green without the frontend. A separate CI job builds
the frontend and can run `npm test` / `tsc --noEmit`.

## Routing

```
#/cards                     Cards + Rules browser
#/decks                     Deck construction
#/interpreter               Playground (successor to current index.html)
#/play/lobby                Rooms list / create
#/play/tabletop/{roomId}    Active tabletop
#/raw                       Loaded-project view
```

`router.ts` parses `location.hash`, matches against a table of
`{ pattern: RegExp, handler: (params) => void }` entries, unmounts the
previous page, and mounts the new one.

## State management

- **Page-scoped state:** plain variables in each page module.
- **App-scoped state (sparingly):** a single `shared/store.ts` module
  exporting typed getters/setters for things that must survive navigation
  (e.g., current deck-in-progress).
- **Server-authoritative state** (rooms, sessions) lives on the server;
  the client re-fetches or subscribes via SSE. No optimistic updates in
  v1.

## Accessibility and theming

- Native form controls wherever possible. No custom dropdowns.
- CSS custom properties for palette; `color-scheme: light dark` in
  `:root` so we inherit OS theme.
- Keyboard-first navigation; every interactive element reachable via tab.

## Related docs

- [pages.md](pages.md) — per-page detail.
- [rooms-protocol.md](rooms-protocol.md) — multi-client play protocol.
- [../api/rest.md](../api/rest.md) — the HTTP endpoints the app consumes.
