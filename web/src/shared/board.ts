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
import type { CardDto } from "../api/dtos";
import {
  arenaPos,
  playerAether,
  playerIntegrity,
  zoneContents,
  type EntityDto,
  type PlayView,
} from "./play-state";

export interface BoardRenderOptions {
  // Roster PlayerId of the viewing seat (1 = first joiner, 2 = second),
  // null when the viewer isn't seated. This is NOT an entity id — the
  // interpreter's Player entities have their own ids (usually 2/3 after
  // Game=1), and we map roster id → state.Players order positionally.
  viewerPlayerId: number | null;
  onCardClick?: (entity: EntityDto) => void;
  // Optional: secondary action wired up to the "ⓘ" corner badge on
  // playable hand cards. When omitted, primary click handles inspection.
  onCardInfo?: (entity: EntityDto) => void;
  catalog?: readonly CardDto[];
  // Entity ids that are the subject of a pending legal action (e.g.
  // play_card from hand). Rendered with a bright outline to signal
  // "this card is a button right now."
  relevantCardIds?: ReadonlySet<number>;
}

export function renderBoard(view: PlayView, opts: BoardRenderOptions): HTMLElement {
  const root = document.createElement("div");
  root.className = "board";

  const players = view.players;
  // Roster PlayerId N ↔ state.Players[N-1]. view.players is sorted by
  // entity id ascending, which matches the Setup allocation order of
  // `Entity Player[i] for i ∈ {1,2}`. Spectators (viewerPlayerId == null)
  // default to players[0] as the bottom seat.
  const viewerIdx = opts.viewerPlayerId !== null ? opts.viewerPlayerId - 1 : -1;
  const bottom = (viewerIdx >= 0 ? players[viewerIdx] : null) ?? players[0] ?? null;
  const top = players.find((p) => p.id !== bottom?.id) ?? players[1] ?? null;

  root.appendChild(renderSeatStrip(top, view, {
    side: "top", faceDown: true, isViewer: false,
    onCardClick: opts.onCardClick, onCardInfo: opts.onCardInfo,
    catalog: opts.catalog, relevantCardIds: opts.relevantCardIds,
  }));
  root.appendChild(renderArenaRow(view, top, bottom, opts.onCardClick, opts.catalog));
  root.appendChild(renderSeatStrip(bottom, view, {
    side: "bottom", faceDown: false, isViewer: true,
    onCardClick: opts.onCardClick, onCardInfo: opts.onCardInfo,
    catalog: opts.catalog, relevantCardIds: opts.relevantCardIds,
  }));

  return root;
}

interface SeatOptions {
  side: "top" | "bottom";
  faceDown: boolean;
  isViewer: boolean;
  onCardClick?: (entity: EntityDto) => void;
  onCardInfo?: (entity: EntityDto) => void;
  catalog?: readonly CardDto[];
  relevantCardIds?: ReadonlySet<number>;
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
        const relevant = opts.relevantCardIds?.has(entity.id) === true;
        hand.appendChild(renderCardFromEntity(entity, {
          faceDown: opts.faceDown,
          size: "sm",
          relevant,
          onClick: opts.faceDown || !opts.onCardClick
            ? undefined
            : () => opts.onCardClick!(entity),
          onInfo: opts.faceDown || !relevant || !opts.onCardInfo
            ? undefined
            : () => opts.onCardInfo!(entity),
        }, opts.catalog));
      }
    }
  }
  strip.appendChild(hand);

  return strip;
}

function renderArenaRow(
  view: PlayView,
  top: EntityDto | null,
  bottom: EntityDto | null,
  onCardClick?: (entity: EntityDto) => void,
  catalog?: readonly CardDto[],
): HTMLElement {
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
    col.appendChild(renderUnitLane(view, top, arena, "top", onCardClick, catalog));
    col.appendChild(renderUnitLane(view, bottom, arena, "bottom", onCardClick, catalog));
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
  view: PlayView,
  player: EntityDto | null,
  arena: EntityDto,
  side: "top" | "bottom",
  onCardClick?: (entity: EntityDto) => void,
  catalog?: readonly CardDto[],
): HTMLElement {
  const lane = document.createElement("div");
  lane.className = `unit-lane unit-lane-${side}`;
  if (!player) return lane;

  // Units in play for this (player, arena) pair. 8e's PlayUnit tags each
  // card entity with parameters.arena = "Left" / "Center" / "Right" and
  // characteristics.in_play = "true" (serialized as strings).
  const arenaPosName = arenaPos(arena);
  const units: EntityDto[] = [];
  for (const entity of view.cardsById.values()) {
    if (entity.ownerId !== player.id) continue;
    if (entity.characteristics?.["in_play"] !== "true") continue;
    if (entity.parameters?.["arena"] !== arenaPosName) continue;
    units.push(entity);
  }

  if (units.length === 0) {
    const empty = document.createElement("span");
    empty.className = "muted unit-lane-empty";
    empty.textContent = "— empty —";
    lane.appendChild(empty);
    return lane;
  }

  for (const unit of units) {
    lane.appendChild(renderCardFromEntity(unit, {
      size: "sm",
      onClick: onCardClick ? () => onCardClick(unit) : undefined,
    }, catalog));
  }
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
