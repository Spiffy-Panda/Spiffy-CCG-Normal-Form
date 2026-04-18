import type { CardDto } from "../../api/dtos";
import { chip } from "../../shared/chip";
import { cardCostBucket } from "./filter";

export function renderCardList(
  container: HTMLElement,
  cards: CardDto[],
  selected: string | null,
  onSelect: (name: string) => void,
): void {
  container.innerHTML = "";
  const header = document.createElement("div");
  header.className = "card-list-header muted";
  header.textContent = `${cards.length} card${cards.length === 1 ? "" : "s"}`;
  container.appendChild(header);

  if (cards.length === 0) {
    const empty = document.createElement("div");
    empty.className = "muted";
    empty.style.padding = "12px";
    empty.textContent = "No cards match these filters.";
    container.appendChild(empty);
    return;
  }

  const ul = document.createElement("ul");
  ul.className = "card-list";
  for (const card of cards) {
    const li = document.createElement("li");
    li.className = "card-list-item";
    if (card.name === selected) li.classList.add("selected");
    li.dataset.name = card.name;

    const main = document.createElement("div");
    main.className = "card-list-item-main";

    const name = document.createElement("span");
    name.className = "card-list-item-name";
    name.textContent = card.name;
    main.appendChild(name);

    const meta = document.createElement("span");
    meta.className = "card-list-item-meta muted";
    const costDisplay = card.cost === null ? "" : ` · ${cardCostBucket(card.cost)}`;
    const type = card.type || "?";
    meta.textContent = `${type}${costDisplay}`;
    main.appendChild(meta);

    li.appendChild(main);

    const chips = document.createElement("div");
    chips.className = "card-list-item-chips";
    for (const f of card.factions) {
      chips.appendChild(chip({ label: f, variant: "faction" }));
    }
    if (card.rarity) chips.appendChild(chip({ label: card.rarity, variant: "rarity" }));
    li.appendChild(chips);

    li.addEventListener("click", () => onSelect(card.name));
    ul.appendChild(li);
  }
  container.appendChild(ul);
}
