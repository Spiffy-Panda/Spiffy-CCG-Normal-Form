import "./style.css";
import { api } from "../../api/client";
import type { CardDto, ProjectDto } from "../../api/dtos";
import type { RouteMatch } from "../../router";
import { applyFilters, cardCostBucket, emptyFilters, uniqueValues, type CardFilters } from "./filter";
import { renderCardList } from "./card-list";
import { renderCardDetail } from "./card-detail";
import { renderRulesTree } from "./rules-tree";

type Tab = "cards" | "rules";

interface PageState {
  tab: Tab;
  selected: string | null;
  filters: CardFilters;
  cards: CardDto[];
  project: ProjectDto | null;
  rawCache: Map<string, string>;
  loading: boolean;
  error: string | null;
}

const state: PageState = {
  tab: "cards",
  selected: null,
  filters: emptyFilters(),
  cards: [],
  project: null,
  rawCache: new Map(),
  loading: false,
  error: null,
};

let currentContainer: HTMLElement | null = null;

export async function renderCards(container: HTMLElement, match: RouteMatch): Promise<void> {
  currentContainer = container;
  applyQueryToState(match.query);

  if (!state.loading && state.cards.length === 0 && !state.error) {
    state.loading = true;
    renderShell(container);
    try {
      const [cardsRes, projectRes] = await Promise.all([api.cards(), api.project()]);
      if (!cardsRes.ok) throw new Error(`/api/cards HTTP error`);
      if (!projectRes.ok) throw new Error(`/api/project HTTP error`);
      state.cards = cardsRes.body;
      state.project = projectRes.body;
      state.error = null;
    } catch (err) {
      state.error = String(err);
    } finally {
      state.loading = false;
    }
  }

  renderShell(container);
}

function applyQueryToState(query: URLSearchParams): void {
  const tab = query.get("tab");
  state.tab = tab === "rules" ? "rules" : "cards";
  state.selected = query.get("card");
}

function updateUrl(patch: Partial<{ tab: Tab; card: string | null }>): void {
  const params = new URLSearchParams();
  const tab = patch.tab ?? state.tab;
  if (tab !== "cards") params.set("tab", tab);
  const card = patch.card === undefined ? state.selected : patch.card;
  if (card) params.set("card", card);
  const hash = params.toString() ? `#/cards?${params.toString()}` : `#/cards`;
  if (window.location.hash !== hash) window.location.hash = hash;
}

function renderShell(container: HTMLElement): void {
  container.innerHTML = "";

  if (state.error) {
    const err = document.createElement("div");
    err.className = "status-err";
    err.style.padding = "24px";
    err.textContent = `Error loading cards: ${state.error}`;
    container.appendChild(err);
    return;
  }

  const page = document.createElement("div");
  page.className = "cards-page";

  const tabs = document.createElement("div");
  tabs.className = "cards-tabs";
  for (const [tab, label] of [["cards", "Cards"], ["rules", "Rules"]] as const) {
    const btn = document.createElement("button");
    btn.className = `cards-tab${state.tab === tab ? " active" : ""}`;
    btn.textContent = label;
    btn.addEventListener("click", () => {
      state.tab = tab;
      updateUrl({ tab });
      renderShell(container);
    });
    tabs.appendChild(btn);
  }
  page.appendChild(tabs);

  const panel = document.createElement("div");
  panel.className = `cards-tab-panel ${state.tab}`;
  page.appendChild(panel);

  if (state.loading) {
    panel.classList.add("rules");
    panel.innerHTML = `<p class="muted">Loading cards and project…</p>`;
  } else if (state.tab === "cards") {
    renderCardsTab(panel);
  } else {
    renderRulesTab(panel);
  }

  container.appendChild(page);
}

function renderCardsTab(panel: HTMLElement): void {
  panel.classList.remove("rules");

  const facets = document.createElement("div");
  facets.className = "cards-facets";
  renderFacets(facets);
  panel.appendChild(facets);

  const listPane = document.createElement("div");
  listPane.className = "cards-list-pane";
  panel.appendChild(listPane);

  const detailPane = document.createElement("div");
  detailPane.className = "cards-detail-pane";
  panel.appendChild(detailPane);

  const filtered = applyFilters(state.cards, state.filters);
  renderCardList(listPane, filtered, state.selected, (name) => {
    state.selected = name;
    updateUrl({ card: name });
    renderDetail(detailPane);
    renderCardList(listPane, filtered, state.selected, onSelectReRender);
  });
  renderDetail(detailPane);
}

function onSelectReRender(name: string): void {
  state.selected = name;
  updateUrl({ card: name });
  if (currentContainer) renderShell(currentContainer);
}

function renderDetail(pane: HTMLElement): void {
  pane.innerHTML = "";
  if (!state.selected) {
    const empty = document.createElement("div");
    empty.className = "muted-empty";
    empty.textContent = "Select a card to see its details.";
    pane.appendChild(empty);
    return;
  }
  const card = state.cards.find((c) => c.name === state.selected);
  if (!card) {
    const empty = document.createElement("div");
    empty.className = "muted-empty";
    empty.textContent = `Card "${state.selected}" not found.`;
    pane.appendChild(empty);
    return;
  }
  void renderCardDetail(pane, card, state.rawCache);
}

function renderRulesTab(panel: HTMLElement): void {
  panel.classList.add("rules");
  if (!state.project) {
    panel.textContent = "Project not loaded.";
    return;
  }
  renderRulesTree(panel, state.project, state.cards, state.rawCache);
}

function renderFacets(facets: HTMLElement): void {
  const factions = uniqueValues(state.cards, (c) => c.factions);
  const types = uniqueValues(state.cards, (c) => c.type).filter(Boolean);
  const costs = ["1", "2", "3", "4", "5", "6+", "?"];
  const availableCosts = new Set(state.cards.map((c) => cardCostBucket(c.cost)));
  const rarities = uniqueValues(state.cards, (c) => c.rarity).filter(Boolean);

  facets.appendChild(
    facetGroup("Faction", factions, state.filters.factions, state.cards.length, (v) =>
      state.cards.filter((c) => c.factions.includes(v)).length,
    ),
  );
  facets.appendChild(
    facetGroup("Type", types, state.filters.types, state.cards.length, (v) =>
      state.cards.filter((c) => c.type === v).length,
    ),
  );
  facets.appendChild(
    facetGroup(
      "Cost",
      costs.filter((c) => availableCosts.has(c)),
      state.filters.costs,
      state.cards.length,
      (v) => state.cards.filter((c) => cardCostBucket(c.cost) === v).length,
    ),
  );
  facets.appendChild(
    facetGroup("Rarity", rarities, state.filters.rarities, state.cards.length, (v) =>
      state.cards.filter((c) => c.rarity === v).length,
    ),
  );

  const kwGroup = document.createElement("div");
  kwGroup.className = "facet-group";
  const kwHead = document.createElement("h3");
  kwHead.textContent = "Keyword";
  kwGroup.appendChild(kwHead);
  const kwInput = document.createElement("input");
  kwInput.type = "text";
  kwInput.className = "facet-search";
  kwInput.placeholder = "substring…";
  kwInput.value = state.filters.keywordQuery;
  let debounce: number | undefined;
  kwInput.addEventListener("input", () => {
    state.filters.keywordQuery = kwInput.value;
    window.clearTimeout(debounce);
    debounce = window.setTimeout(() => {
      if (currentContainer) renderShell(currentContainer);
    }, 120);
  });
  kwGroup.appendChild(kwInput);
  facets.appendChild(kwGroup);

  const clear = document.createElement("button");
  clear.className = "facet-clear";
  clear.textContent = "Clear all";
  clear.addEventListener("click", () => {
    state.filters = emptyFilters();
    if (currentContainer) renderShell(currentContainer);
  });
  facets.appendChild(clear);
}

function facetGroup(
  title: string,
  values: string[],
  selected: Set<string>,
  _total: number,
  count: (v: string) => number,
): HTMLElement {
  const group = document.createElement("div");
  group.className = "facet-group";
  const head = document.createElement("h3");
  head.textContent = title;
  group.appendChild(head);
  for (const v of values) {
    const label = document.createElement("label");
    label.className = "facet-option";
    const cb = document.createElement("input");
    cb.type = "checkbox";
    cb.checked = selected.has(v);
    cb.addEventListener("change", () => {
      if (cb.checked) selected.add(v);
      else selected.delete(v);
      if (currentContainer) renderShell(currentContainer);
    });
    label.appendChild(cb);
    const text = document.createElement("span");
    text.textContent = v;
    label.appendChild(text);
    const countEl = document.createElement("span");
    countEl.className = "facet-count";
    countEl.textContent = String(count(v));
    label.appendChild(countEl);
    group.appendChild(label);
  }
  return group;
}
