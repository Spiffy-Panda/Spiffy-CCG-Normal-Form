// Shared card inspector — a right-edge side panel that opens when any
// card-like element is clicked on the tabletop. v1 scope is deliberately
// thin: show whatever the caller can resolve about the card (a full
// CardDto when the entity's name matches a card in the catalog, or just
// the entity placeholder for anonymous runtime cards). The panel lives
// at document root so every page can reuse it; open/close hooks read
// and write the singleton.
//
// Escape closes.

import type { CardDto } from "../api/dtos";
import type { EntityDto } from "./play-state";

export interface InspectorCard {
  name: string;
  factions: string[];
  type: string;
  cost: number | null;
  rarity: string;
  abilitiesText: string[];
  sourcePath?: string | null;
  sourceLine?: number | null;
  // The original raw payload, for the Source tab fallback.
  raw?: EntityDto | CardDto | null;
}

let panelEl: HTMLElement | null = null;
let current: InspectorCard | null = null;
let escListener: ((e: KeyboardEvent) => void) | null = null;

/**
 * Open the inspector. The panel is moved into <c>container</c> if supplied
 * (embedded mode — callers get layout control, e.g. the tabletop stacks
 * the inspector above its event log); otherwise it floats off
 * <c>document.body</c> like a modal overlay.
 */
export function openInspector(card: InspectorCard, container?: HTMLElement): void {
  current = card;
  ensurePanel(container);
  render();
  if (!escListener) {
    escListener = (e) => {
      if (e.key === "Escape") closeInspector();
    };
    window.addEventListener("keydown", escListener);
  }
}

export function closeInspector(): void {
  current = null;
  if (panelEl) {
    panelEl.classList.remove("card-inspector-open");
  }
  if (escListener) {
    window.removeEventListener("keydown", escListener);
    escListener = null;
  }
}

export function fromCardDto(dto: CardDto): InspectorCard {
  return {
    name: dto.name,
    factions: dto.factions,
    type: dto.type,
    cost: dto.cost,
    rarity: dto.rarity,
    abilitiesText: dto.abilitiesText ?? [],
    sourcePath: dto.sourcePath,
    sourceLine: dto.sourceLine,
    raw: dto,
  };
}

export function fromEntity(entity: EntityDto, catalog?: readonly CardDto[]): InspectorCard {
  const resolved = catalog?.find((c) => c.name === entity.displayName);
  if (resolved) return fromCardDto(resolved);
  return {
    name: entity.displayName,
    factions: [],
    type: entity.kind,
    cost: null,
    rarity: "",
    abilitiesText: [],
    sourcePath: null,
    sourceLine: null,
    raw: entity,
  };
}

function ensurePanel(container?: HTMLElement): void {
  const target = container ?? document.body;
  if (!panelEl) {
    panelEl = document.createElement("aside");
    panelEl.className = "card-inspector";
  }
  if (panelEl.parentElement !== target) {
    // Insert at the top of the target container so embedded callers
    // (the tabletop's right column) get the inspector above their other
    // children. For body-mounted overlays this is irrelevant since the
    // panel is fixed-positioned.
    target.insertBefore(panelEl, target.firstChild);
  }
}

function render(): void {
  if (!panelEl || !current) return;
  panelEl.innerHTML = "";
  panelEl.classList.add("card-inspector-open");

  const head = document.createElement("div");
  head.className = "card-inspector-head";
  const title = document.createElement("div");
  title.className = "card-inspector-title";
  title.textContent = current.name;
  head.appendChild(title);
  const close = document.createElement("button");
  close.className = "card-inspector-close";
  close.textContent = "✕";
  close.title = "Close (Esc)";
  close.addEventListener("click", closeInspector);
  head.appendChild(close);
  panelEl.appendChild(head);

  const meta = document.createElement("div");
  meta.className = "card-inspector-meta muted";
  const parts: string[] = [];
  if (current.type) parts.push(current.type);
  if (current.cost !== null && current.cost !== undefined) parts.push(`${current.cost}⚡`);
  if (current.rarity) parts.push(current.rarity);
  if (current.factions.length > 0) parts.push(current.factions.join(" / "));
  meta.textContent = parts.join(" · ");
  panelEl.appendChild(meta);

  if (current.abilitiesText.length > 0) {
    const rules = document.createElement("div");
    rules.className = "card-inspector-rules";
    for (const line of current.abilitiesText) {
      const p = document.createElement("p");
      p.textContent = line;
      rules.appendChild(p);
    }
    panelEl.appendChild(rules);
  } else {
    const empty = document.createElement("div");
    empty.className = "muted";
    empty.style.fontSize = "12px";
    empty.style.marginTop = "6px";
    empty.textContent = "No humanized rules. Runtime entity — see Source below.";
    panelEl.appendChild(empty);
  }

  if (current.sourcePath) {
    const src = document.createElement("div");
    src.className = "card-inspector-source muted";
    src.textContent = current.sourceLine
      ? `${current.sourcePath}:${current.sourceLine}`
      : current.sourcePath;
    panelEl.appendChild(src);
  } else if (current.raw) {
    const pre = document.createElement("pre");
    pre.className = "card-inspector-raw";
    pre.textContent = JSON.stringify(current.raw, null, 2);
    panelEl.appendChild(pre);
  }
}
