import "./style.css";
import { api } from "../../api/client";
import type { CardDto } from "../../api/dtos";
import type { RouteMatch } from "../../router";
import { chip } from "../../shared/chip";
import { cardCostBucket } from "../cards/filter";
import { FORMATS, formatById, type Format } from "./format-selector";
import { deckTotalCards, summarize, type DeckEntry } from "./distribution";
import { deleteDeck, listSavedDecks, loadDeck, saveDeck } from "./persistence";

interface PageState {
  allCards: CardDto[];
  cardsByName: Map<string, CardDto>;
  format: Format;
  pool: CardDto[];
  deck: Map<string, number>;       // card name -> count
  poolSearch: string;
  loading: boolean;
  error: string | null;
  draftSeed: number;
}

const state: PageState = {
  allCards: [],
  cardsByName: new Map(),
  format: FORMATS[0],
  pool: [],
  deck: new Map(),
  poolSearch: "",
  loading: false,
  error: null,
  draftSeed: 1234,
};

let container: HTMLElement | null = null;

export async function renderDecks(root: HTMLElement, _match: RouteMatch): Promise<void> {
  container = root;
  if (state.allCards.length === 0 && !state.loading && !state.error) {
    state.loading = true;
    renderShell();
    try {
      const { ok, body } = await api.cards();
      if (!ok) throw new Error("GET /api/cards failed");
      state.allCards = body;
      state.cardsByName = new Map(body.map((c) => [c.name, c]));
      state.pool = body;
      state.error = null;
    } catch (err) {
      state.error = String(err);
    } finally {
      state.loading = false;
    }
  }
  renderShell();
}

async function setFormat(format: Format): Promise<void> {
  state.format = format;
  state.deck = new Map();
  if (format.needsMockPool) {
    const { ok, body } = await api.mockPool({
      format: format.id,
      seed: state.draftSeed,
      size: Math.max(40, format.deckSize),
    });
    if (ok) {
      const names = new Set(body.cards);
      state.pool = state.allCards.filter((c) => names.has(c.name));
    } else {
      state.pool = state.allCards;
    }
  } else {
    state.pool = state.allCards;
  }
  renderShell();
}

function renderShell(): void {
  if (!container) return;
  container.innerHTML = "";

  if (state.error) {
    const err = document.createElement("div");
    err.className = "status-err";
    err.style.padding = "24px";
    err.textContent = `Error: ${state.error}`;
    container.appendChild(err);
    return;
  }

  const page = document.createElement("div");
  page.className = "decks-page";
  container.appendChild(page);

  const left = document.createElement("div");
  left.className = "decks-col left";
  page.appendChild(left);

  const mid = document.createElement("div");
  mid.className = "decks-col";
  page.appendChild(mid);

  const right = document.createElement("div");
  right.className = "decks-col right";
  page.appendChild(right);

  renderLeft(left);
  renderPool(mid);
  renderDeck(right);
}

function renderLeft(col: HTMLElement): void {
  const fmtSection = document.createElement("div");
  fmtSection.className = "decks-section";
  fmtSection.innerHTML = `<h3>Format</h3>`;
  const select = document.createElement("select");
  select.className = "decks-format-select";
  for (const f of FORMATS) {
    const opt = document.createElement("option");
    opt.value = f.id;
    opt.textContent = f.label;
    if (f.id === state.format.id) opt.selected = true;
    select.appendChild(opt);
  }
  select.addEventListener("change", () => void setFormat(formatById(select.value)));
  fmtSection.appendChild(select);

  const info = document.createElement("p");
  info.className = "muted";
  info.style.margin = "6px 0 0 0";
  info.style.fontSize = "11.5px";
  info.textContent =
    `Max ${state.format.maxCopies} copies · deck size ${state.format.deckSize}` +
    (state.format.needsMockPool ? ` · mock pool seed ${state.draftSeed}` : "");
  fmtSection.appendChild(info);

  if (state.format.needsMockPool) {
    const warn = document.createElement("div");
    warn.className = "decks-warning";
    warn.textContent = "Mock draft pool — real drafting pending.";
    fmtSection.appendChild(warn);
  }
  col.appendChild(fmtSection);

  // Save / Load section.
  const saveSection = document.createElement("div");
  saveSection.className = "decks-section";
  saveSection.innerHTML = `<h3>Decks</h3>`;

  const actionRow = document.createElement("div");
  actionRow.className = "decks-row";
  const saveBtn = document.createElement("button");
  saveBtn.className = "decks-btn primary";
  saveBtn.textContent = "Save";
  saveBtn.addEventListener("click", () => onSave());
  actionRow.appendChild(saveBtn);

  const newBtn = document.createElement("button");
  newBtn.className = "decks-btn";
  newBtn.textContent = "New";
  newBtn.addEventListener("click", () => {
    state.deck = new Map();
    renderShell();
  });
  actionRow.appendChild(newBtn);
  saveSection.appendChild(actionRow);

  const saved = listSavedDecks(state.format.id);
  if (saved.length > 0) {
    const loadRow = document.createElement("div");
    loadRow.className = "decks-row";
    loadRow.style.marginTop = "6px";
    const loadSelect = document.createElement("select");
    loadSelect.className = "decks-format-select";
    loadSelect.innerHTML = `<option value="">Load…</option>` +
      saved.map((n) => `<option value="${escapeHtml(n)}">${escapeHtml(n)}</option>`).join("");
    loadSelect.addEventListener("change", () => {
      if (!loadSelect.value) return;
      const d = loadDeck(state.format.id, loadSelect.value);
      if (d) {
        state.deck = new Map(d.cards.map((c) => [c.name, c.count]));
        renderShell();
      }
    });
    loadRow.appendChild(loadSelect);

    const delBtn = document.createElement("button");
    delBtn.className = "decks-btn";
    delBtn.textContent = "Delete";
    delBtn.title = "Delete selected saved deck";
    delBtn.addEventListener("click", () => {
      if (!loadSelect.value) return;
      deleteDeck(state.format.id, loadSelect.value);
      renderShell();
    });
    loadRow.appendChild(delBtn);
    saveSection.appendChild(loadRow);
  }
  col.appendChild(saveSection);

  if (state.format.needsMockPool) {
    const seedSection = document.createElement("div");
    seedSection.className = "decks-section";
    seedSection.innerHTML = `<h3>Draft seed</h3>`;
    const row = document.createElement("div");
    row.className = "decks-row";
    const seedInput = document.createElement("input");
    seedInput.type = "number";
    seedInput.value = String(state.draftSeed);
    seedInput.style.flex = "1";
    seedInput.style.padding = "5px 8px";
    seedInput.style.background = "transparent";
    seedInput.style.color = "inherit";
    seedInput.style.border = "1px solid var(--control-border)";
    seedInput.style.borderRadius = "4px";
    row.appendChild(seedInput);
    const reseed = document.createElement("button");
    reseed.className = "decks-btn";
    reseed.textContent = "Reseed";
    reseed.addEventListener("click", () => {
      state.draftSeed = parseInt(seedInput.value, 10) || 0;
      void setFormat(state.format);
    });
    row.appendChild(reseed);
    seedSection.appendChild(row);
    col.appendChild(seedSection);
  }
}

function renderPool(col: HTMLElement): void {
  const head = document.createElement("div");
  head.className = "decks-section";
  head.innerHTML = `<h3>Card pool — ${state.pool.length}</h3>`;
  const search = document.createElement("input");
  search.className = "decks-pool-search";
  search.placeholder = "Search name or keyword…";
  search.value = state.poolSearch;
  let debounce: number | undefined;
  search.addEventListener("input", () => {
    state.poolSearch = search.value;
    window.clearTimeout(debounce);
    debounce = window.setTimeout(() => renderPool(col), 120);
  });
  head.appendChild(search);
  col.innerHTML = "";
  col.appendChild(head);

  const filtered = filterPool(state.pool, state.poolSearch);
  const list = document.createElement("ul");
  list.className = "decks-pool-list";
  for (const card of filtered) {
    list.appendChild(cardRow(card, true));
  }
  if (filtered.length === 0) {
    const empty = document.createElement("div");
    empty.className = "decks-empty";
    empty.textContent = "No cards in pool match this search.";
    list.appendChild(empty);
  }
  col.appendChild(list);
}

function renderDeck(col: HTMLElement): void {
  const total = deckTotalCards([...state.deck.entries()].map(([name, count]) => ({ name, count })));
  const deckHead = document.createElement("div");
  deckHead.className = "decks-section";
  const status = total === state.format.deckSize
    ? `<span class="status-ok">${total} / ${state.format.deckSize}</span>`
    : `${total} / ${state.format.deckSize}`;
  deckHead.innerHTML = `<h3>Deck — ${status}</h3>`;
  col.appendChild(deckHead);

  if (state.deck.size === 0) {
    const empty = document.createElement("div");
    empty.className = "decks-empty";
    empty.textContent = "No cards yet. Click `+` in the pool.";
    col.appendChild(empty);
  } else {
    const list = document.createElement("ul");
    list.className = "decks-list";
    const sorted = [...state.deck.entries()].sort((a, b) => a[0].localeCompare(b[0]));
    for (const [name] of sorted) {
      const card = state.cardsByName.get(name);
      if (card) list.appendChild(cardRow(card, false));
    }
    col.appendChild(list);
  }

  // Distribution panel.
  const dist = summarize(
    [...state.deck.entries()].map(([name, count]) => ({ name, count })),
    state.allCards,
  );
  const panel = document.createElement("div");
  panel.className = "decks-section";
  panel.innerHTML = `<h3>Distribution</h3>`;
  panel.appendChild(barGroup("Faction", dist.faction, total));
  panel.appendChild(barGroup("Type", dist.type, total));
  panel.appendChild(barGroup("Cost", dist.cost, total));
  panel.appendChild(barGroup("Rarity", dist.rarity, total));
  col.appendChild(panel);
}

function filterPool(cards: CardDto[], query: string): CardDto[] {
  const q = query.trim().toLowerCase();
  if (!q) return cards;
  return cards.filter((c) => {
    if (c.name.toLowerCase().includes(q)) return true;
    if (c.keywords.some((k) => k.toLowerCase().includes(q))) return true;
    return false;
  });
}

function cardRow(card: CardDto, isPool: boolean): HTMLElement {
  const li = document.createElement("li");
  li.className = "decks-card-row";

  const count = document.createElement("span");
  count.className = "decks-card-row-count";
  count.textContent = String(state.deck.get(card.name) ?? 0);
  li.appendChild(count);

  const main = document.createElement("div");
  main.style.minWidth = "0";
  main.style.overflow = "hidden";
  main.style.textOverflow = "ellipsis";

  const nameLine = document.createElement("div");
  nameLine.style.display = "flex";
  nameLine.style.alignItems = "baseline";
  nameLine.style.gap = "6px";
  const nameSpan = document.createElement("span");
  nameSpan.style.fontWeight = "600";
  nameSpan.style.whiteSpace = "nowrap";
  nameSpan.textContent = card.name;
  nameLine.appendChild(nameSpan);

  const costEl = document.createElement("span");
  costEl.className = "muted";
  costEl.style.fontSize = "11px";
  costEl.textContent = card.cost === null ? "" : cardCostBucket(card.cost);
  nameLine.appendChild(costEl);
  main.appendChild(nameLine);

  const meta = document.createElement("div");
  meta.className = "decks-card-row-meta";
  for (const f of card.factions) meta.appendChild(chip({ label: f, variant: "faction" }));
  if (card.type) meta.appendChild(chip({ label: card.type, variant: "type" }));
  if (card.rarity) meta.appendChild(chip({ label: card.rarity, variant: "rarity" }));
  main.appendChild(meta);

  li.appendChild(main);

  const controls = document.createElement("span");
  controls.className = "decks-count-controls";
  const minusBtn = document.createElement("button");
  minusBtn.textContent = "−";
  minusBtn.disabled = !state.deck.has(card.name);
  minusBtn.addEventListener("click", (ev) => {
    ev.stopPropagation();
    adjustCount(card.name, -1);
  });
  controls.appendChild(minusBtn);

  const countEl = document.createElement("span");
  countEl.className = "count";
  countEl.textContent = String(state.deck.get(card.name) ?? 0);
  controls.appendChild(countEl);

  const plusBtn = document.createElement("button");
  plusBtn.textContent = "+";
  plusBtn.disabled = (state.deck.get(card.name) ?? 0) >= state.format.maxCopies;
  plusBtn.addEventListener("click", (ev) => {
    ev.stopPropagation();
    adjustCount(card.name, 1);
  });
  controls.appendChild(plusBtn);
  li.appendChild(controls);

  if (isPool) li.addEventListener("click", () => adjustCount(card.name, 1));
  return li;
}

function adjustCount(name: string, delta: number): void {
  const current = state.deck.get(name) ?? 0;
  const next = current + delta;
  if (next <= 0) state.deck.delete(name);
  else if (next <= state.format.maxCopies) state.deck.set(name, next);
  renderShell();
}

function barGroup(title: string, counts: Record<string, number>, total: number): HTMLElement {
  const group = document.createElement("div");
  group.className = "dist-group";
  const head = document.createElement("h4");
  head.textContent = title;
  group.appendChild(head);

  const entries = Object.entries(counts).sort((a, b) => b[1] - a[1]);
  if (entries.length === 0) {
    const empty = document.createElement("div");
    empty.className = "muted";
    empty.style.fontSize = "11px";
    empty.style.marginLeft = "4px";
    empty.textContent = "—";
    group.appendChild(empty);
    return group;
  }
  const maxValue = Math.max(1, total);
  for (const [key, value] of entries) {
    const row = document.createElement("div");
    row.className = "dist-bar";
    const label = document.createElement("span");
    label.textContent = key;
    row.appendChild(label);
    const track = document.createElement("div");
    track.className = "dist-bar-track";
    const fill = document.createElement("div");
    fill.className = "dist-bar-fill";
    fill.style.width = `${Math.min(100, (value / maxValue) * 100)}%`;
    track.appendChild(fill);
    row.appendChild(track);
    const count = document.createElement("span");
    count.className = "dist-bar-count";
    count.textContent = String(value);
    row.appendChild(count);
    group.appendChild(row);
  }
  return group;
}

function onSave(): void {
  const name = window.prompt("Save deck as:");
  if (!name) return;
  const cards: DeckEntry[] = [...state.deck.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => a.name.localeCompare(b.name));
  saveDeck({
    name,
    format: state.format.id,
    createdAt: new Date().toISOString(),
    cards,
  });
  renderShell();
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
