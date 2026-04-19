// Low-fi tabletop board. Renders a PlayView as three stacked Arenas
// with each player's Hand / Arsenal / Cache anchored at their side.
//
// Layout sketch (you are always the bottom seat):
//
//   ┌── opponent header (hand size, aether, cache) ──┐
//   │   ⌂ ⌂ ⌂ ⌂ ⌂  (face-down hand)                 │
//   ├────┬─────────────┬─────────────┬──────────┬─────┤
//   │    │  ArenaLeft  │  ArenaCenter│ ArenaRight    │
//   │    │  [opponent  │  [opponent  │ [opponent      │
//   │    │   conduit]  │   conduit]  │  conduit]      │
//   │    │  (units)    │  (units)    │ (units)        │
//   │    │  ─────────  │  ─────────  │ ───────        │
//   │    │  (units)    │  (units)    │ (units)        │
//   │    │  [your      │  [your      │ [your          │
//   │    │   conduit]  │   conduit]  │  conduit]      │
//   ├────┴─────────────┴─────────────┴────────────────┤
//   │   your hand (face-up)                           │
//   │   aether, arsenal, cache                        │
//   └─────────────────────────────────────────────────┘
//
// v1 NOTES:
//   * Unit placement inside arenas is empty until the engine supports
//     playing cards mid-game (gated on 7f).
//   * Hand cards render as placeholder chits (entity id) until decks
//     attach to rooms in 7c.

import { renderCardFromEntity } from "./card";
import {
  arenaPos,
  playerAether,
  playerIntegrity,
  zoneContents,
  type EntityDto,
  type PlayView,
} from "./play-state";

export interface BoardRenderOptions {
  viewerPlayerId: number | null;  // which seat is "you"; null = spectator
  onCardClick?: (entity: EntityDto) => void;
}

export function renderBoard(view: PlayView, opts: BoardRenderOptions): HTMLElement {
  const root = document.createElement("div");
  root.className = "board";

  const players = view.players;
  // Determine which player is "top" and "bottom". If viewer is not in
  // the match, pick players[1] as top, players[0] as bottom so the UI
  // is deterministic.
  const youId = opts.viewerPlayerId;
  const bottom = players.find((p) => p.id === youId) ?? players[0] ?? null;
  const top = players.find((p) => p.id !== bottom?.id) ?? players[1] ?? null;

  root.appendChild(renderSeatStrip(top, view, { side: "top", faceDown: true, isViewer: false, onCardClick: opts.onCardClick }));
  root.appendChild(renderArenaRow(view, top, bottom));
  root.appendChild(renderSeatStrip(bottom, view, { side: "bottom", faceDown: false, isViewer: true, onCardClick: opts.onCardClick }));

  return root;
}

interface SeatOptions {
  side: "top" | "bottom";
  faceDown: boolean;
  isViewer: boolean;
  onCardClick?: (entity: EntityDto) => void;
}

function renderSeatStrip(
  player: EntityDto | null,
  view: PlayView,
  opts: SeatOptions,
): HTMLElement {
  const strip = document.createElement("div");
  strip.className = `seat-strip seat-${opts.side}`;

  if (!player) {
    strip.textContent = "(waiting for player)";
    strip.classList.add("seat-empty");
    return strip;
  }

  const active = view.activePlayerId === player.id;
  if (active) strip.classList.add("seat-active");

  const header = document.createElement("div");
  header.className = "seat-header";
  header.innerHTML = `
    <span class="seat-name">${escapeHtml(player.displayName)}</span>
    <span class="seat-aether">⚡ ${playerAether(player)}</span>
    <span class="seat-zone">Hand ${zoneContents(player, "Hand").length}</span>
    <span class="seat-zone">Arsenal ${zoneContents(player, "Arsenal").length}</span>
    <span class="seat-zone">Cache ${zoneContents(player, "Cache").length}</span>
    <span class="seat-zone">Resonance ${zoneContents(player, "ResonanceField").length}</span>
  `;
  strip.appendChild(header);

  const hand = document.createElement("div");
  hand.className = "seat-hand";
  const handIds = zoneContents(player, "Hand");
  if (handIds.length === 0) {
    const empty = document.createElement("div");
    empty.className = "seat-hand-empty muted";
    empty.textContent = opts.faceDown ? "(no cards)" : "(empty hand)";
    hand.appendChild(empty);
  } else {
    for (const cardId of handIds) {
      const entity = view.cardsById.get(cardId);
      if (entity) {
        hand.appendChild(renderCardFromEntity(entity, {
          faceDown: opts.faceDown,
          size: "sm",
          onClick: opts.faceDown || !opts.onCardClick
            ? undefined
            : () => opts.onCardClick!(entity),
        }));
      }
    }
  }
  strip.appendChild(hand);

  return strip;
}

function renderArenaRow(view: PlayView, top: EntityDto | null, bottom: EntityDto | null): HTMLElement {
  const row = document.createElement("div");
  row.className = "arena-row";

  for (const arena of view.arenas) {
    const col = document.createElement("div");
    col.className = "arena-col";
    col.dataset.arenaPos = arenaPos(arena);

    const label = document.createElement("div");
    label.className = "arena-label";
    label.textContent = arenaPos(arena);
    col.appendChild(label);

    col.appendChild(renderConduit(view, top, arena, "top"));
    col.appendChild(renderUnitLane(view, top, arena, "top"));
    col.appendChild(renderUnitLane(view, bottom, arena, "bottom"));
    col.appendChild(renderConduit(view, bottom, arena, "bottom"));

    row.appendChild(col);
  }

  return row;
}

function renderConduit(
  view: PlayView,
  player: EntityDto | null,
  arena: EntityDto,
  side: "top" | "bottom",
): HTMLElement {
  const slot = document.createElement("div");
  slot.className = `conduit-slot conduit-${side}`;
  if (!player) {
    slot.classList.add("conduit-empty");
    return slot;
  }
  const integrity = playerIntegrity(view, player.id, arenaPos(arena));
  if (integrity === null) {
    slot.classList.add("conduit-collapsed");
    slot.innerHTML = `<span class="muted">—</span>`;
  } else {
    slot.innerHTML = `
      <span class="conduit-label">⟨${escapeHtml(player.displayName)}⟩</span>
      <span class="conduit-integrity">♥ ${integrity}</span>
    `;
  }
  return slot;
}

function renderUnitLane(
  _view: PlayView,
  player: EntityDto | null,
  _arena: EntityDto,
  side: "top" | "bottom",
): HTMLElement {
  // v1: no unit-in-arena data yet (interpreter stops before units
  // enter play). Render an empty lane so the board still looks like
  // a board.
  const lane = document.createElement("div");
  lane.className = `unit-lane unit-lane-${side}`;
  if (!player) return lane;
  const empty = document.createElement("span");
  empty.className = "muted unit-lane-empty";
  empty.textContent = "— empty —";
  lane.appendChild(empty);
  return lane;
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => {
    switch (c) {
      case "&": return "&amp;";
      case "<": return "&lt;";
      case ">": return "&gt;";
      case '"': return "&quot;";
      default: return "&#39;";
    }
  });
}
