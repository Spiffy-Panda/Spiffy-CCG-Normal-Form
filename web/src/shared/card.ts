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
  onInfo?: (view: CardView) => void;
  subtle?: boolean;
  // When true, mark the card as relevant to the current pending decision
  // (e.g. playable from hand under a play_card legal action). Rendered as
  // a bright outline + pointer affordance; typically combined with an
  // onClick handler that submits the action.
  relevant?: boolean;
}

export function renderCard(view: CardView, opts: CardRenderOptions = {}): HTMLElement {
  const el = document.createElement("div");
  el.className = `play-card play-card-${opts.size ?? "md"}`;
  if (opts.faceDown) el.classList.add("play-card-back");
  if (opts.subtle) el.classList.add("play-card-subtle");
  if (opts.relevant) el.classList.add("play-card-relevant");

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
  el.appendChild(header);

  // Three compact glyph rows — cost / echoes pushed / card type — when
  // the card carries resolved catalog data. Runtime entity-only chits
  // (no factions, no type) skip this and fall back to just the name.
  const hasMeta = (view.cost !== null && view.cost !== undefined) || view.factions.length > 0 || !!view.type;
  if (hasMeta) {
    const glyphs = document.createElement("div");
    glyphs.className = "play-card-glyphs";

    const costRow = document.createElement("div");
    costRow.className = "play-card-glyph-row play-card-glyph-cost";
    if (view.cost !== null && view.cost !== undefined) {
      costRow.textContent = `⚡${view.cost}`;
    } else {
      costRow.textContent = "";
    }
    glyphs.appendChild(costRow);

    const echoRow = document.createElement("div");
    echoRow.className = "play-card-glyph-row play-card-glyph-echo";
    if (view.factions.length > 0) {
      echoRow.textContent = view.factions.map(factionEchoGlyph).join(" ");
      echoRow.title = `Pushes ${view.factions.join(" + ")} echo${view.factions.length > 1 ? "es" : ""}`;
    } else {
      echoRow.textContent = "·";
    }
    glyphs.appendChild(echoRow);

    const typeRow = document.createElement("div");
    typeRow.className = "play-card-glyph-row play-card-glyph-type";
    if (view.type) {
      typeRow.textContent = `${typeGlyph(view.type)} ${view.type}`;
    } else {
      typeRow.textContent = "";
    }
    glyphs.appendChild(typeRow);

    el.appendChild(glyphs);
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

  if (opts.onInfo) {
    const info = document.createElement("button");
    info.className = "play-card-info-btn";
    info.type = "button";
    info.textContent = "ⓘ";
    info.title = "Inspect card";
    info.addEventListener("click", (e) => {
      e.stopPropagation();
      opts.onInfo!(view);
    });
    el.appendChild(info);
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

// Renders a runtime card entity. When the entity's displayName matches a
// CardDto in the supplied catalog, we promote to a fully-resolved face
// (cost / faction / type glyphs). Otherwise we fall back to a minimal
// chit with just the entity name — this still happens for anonymous
// tokens created by the engine at runtime, or when the catalog hasn't
// loaded yet.
export function renderCardFromEntity(
  entity: EntityDto,
  opts: CardRenderOptions = {},
  catalog?: readonly CardDto[],
): HTMLElement {
  const resolved = catalog?.find((c) => c.name === entity.displayName);
  if (resolved) {
    return renderCard({
      name: resolved.name,
      factions: resolved.factions,
      type: resolved.type,
      cost: resolved.cost,
      rarity: resolved.rarity,
      abilitiesText: resolved.abilitiesText ?? [],
      entityId: entity.id,
    }, opts);
  }
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

const FACTION_ECHO_GLYPHS: Record<string, string> = {
  EMBER: "🔥",
  BULWARK: "🛡",
  TIDE: "🌊",
  THORN: "🌿",
  HOLLOW: "🌌",
  NEUTRAL: "⚪",
};

function factionEchoGlyph(faction: string): string {
  return FACTION_ECHO_GLYPHS[faction] ?? "◆";
}

const TYPE_GLYPHS: Record<string, string> = {
  Unit: "⚔",
  Maneuver: "✦",
  Standard: "◈",
  Gambit: "◇",
  Card: "▫",
};

function typeGlyph(type: string): string {
  return TYPE_GLYPHS[type] ?? "▫";
}
