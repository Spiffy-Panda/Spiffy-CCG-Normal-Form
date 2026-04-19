// Low-fi card face used by the tabletop and (eventually) the card
// inspector. Renders from two sources:
//
//   * CardView — a small struct with explicit fields (name, factions,
//     cost, type, abilitiesText). Used when we have a resolved card
//     from /api/cards.
//   * EntityDto with kind="Card" — a runtime card entity from the
//     GameState. At this stage of the engine these carry only a
//     displayName placeholder ("Deck_Player1_7"), so they render as
//     generic card chits.
//
// Face-down cards (opponent hand) render as a card-back with no name.

import type { CardDto } from "../api/dtos";
import type { EntityDto } from "./play-state";

export interface CardView {
  name: string;
  factions: string[];
  type: string;
  cost: number | null;
  rarity: string;
  abilitiesText: string[];
  entityId?: number;
}

export interface CardRenderOptions {
  faceDown?: boolean;
  size?: "sm" | "md" | "lg";
  onClick?: (view: CardView) => void;
  subtle?: boolean;
}

export function renderCard(view: CardView, opts: CardRenderOptions = {}): HTMLElement {
  const el = document.createElement("div");
  el.className = `play-card play-card-${opts.size ?? "md"}`;
  if (opts.faceDown) el.classList.add("play-card-back");
  if (opts.subtle) el.classList.add("play-card-subtle");

  if (opts.faceDown) {
    const back = document.createElement("div");
    back.className = "play-card-back-pattern";
    el.appendChild(back);
    return el;
  }

  const faction = view.factions[0] ?? "NEUTRAL";
  el.dataset.faction = faction;
  el.style.setProperty("--card-faction-color", factionColor(faction));

  const stripe = document.createElement("div");
  stripe.className = "play-card-stripe";
  el.appendChild(stripe);

  const header = document.createElement("div");
  header.className = "play-card-header";
  const name = document.createElement("div");
  name.className = "play-card-name";
  name.textContent = view.name;
  header.appendChild(name);
  if (view.cost !== null && view.cost !== undefined) {
    const cost = document.createElement("div");
    cost.className = "play-card-cost";
    cost.textContent = String(view.cost);
    header.appendChild(cost);
  }
  el.appendChild(header);

  if (view.type) {
    const typeLine = document.createElement("div");
    typeLine.className = "play-card-type";
    typeLine.textContent = view.type;
    el.appendChild(typeLine);
  }

  if (view.abilitiesText.length > 0) {
    const body = document.createElement("div");
    body.className = "play-card-body";
    body.textContent = view.abilitiesText.join(" • ");
    el.appendChild(body);
  }

  if (opts.onClick) {
    el.classList.add("play-card-clickable");
    el.addEventListener("click", () => opts.onClick!(view));
  }

  return el;
}

export function renderCardFromDto(card: CardDto, opts: CardRenderOptions = {}): HTMLElement {
  return renderCard({
    name: card.name,
    factions: card.factions,
    type: card.type,
    cost: card.cost,
    rarity: card.rarity,
    abilitiesText: card.abilitiesText ?? [],
  }, opts);
}

// Renders a runtime card entity. Until decks are wired to rooms (7c),
// the entity only carries a placeholder displayName — produce a
// minimal chit with the entity id visible for debugging.
export function renderCardFromEntity(entity: EntityDto, opts: CardRenderOptions = {}): HTMLElement {
  return renderCard({
    name: friendlyName(entity),
    factions: [],
    type: "Card",
    cost: null,
    rarity: "",
    abilitiesText: [],
    entityId: entity.id,
  }, opts);
}

function friendlyName(entity: EntityDto): string {
  // "Deck_Player1_7" → "#7"
  const m = /_(\d+)$/.exec(entity.displayName);
  return m ? `#${m[1]}` : entity.displayName;
}

const FACTION_COLORS: Record<string, string> = {
  EMBER: "#d86a3a",
  BULWARK: "#c7b077",
  TIDE: "#4f98c7",
  THORN: "#5fa360",
  HOLLOW: "#7a6aa8",
  NEUTRAL: "#888888",
};

function factionColor(faction: string): string {
  return FACTION_COLORS[faction] ?? FACTION_COLORS.NEUTRAL;
}
