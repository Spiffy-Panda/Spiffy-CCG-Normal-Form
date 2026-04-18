import type { CardDto } from "../../api/dtos";
import { api } from "../../api/client";
import { chip } from "../../shared/chip";
import { cardCostBucket, extractSourceBlock } from "./filter";

export async function renderCardDetail(
  container: HTMLElement,
  card: CardDto,
  rawCache: Map<string, string>,
): Promise<void> {
  container.innerHTML = "";

  const wrap = document.createElement("div");
  wrap.className = "card-detail";

  const title = document.createElement("h2");
  title.className = "card-detail-title";
  title.textContent = card.name;
  wrap.appendChild(title);

  const chips = document.createElement("div");
  chips.className = "card-detail-chips";
  for (const f of card.factions) chips.appendChild(chip({ label: f, variant: "faction" }));
  if (card.type) chips.appendChild(chip({ label: card.type, variant: "type" }));
  if (card.cost !== null) {
    chips.appendChild(chip({ label: `cost ${cardCostBucket(card.cost)}`, variant: "cost" }));
  }
  if (card.rarity) chips.appendChild(chip({ label: card.rarity, variant: "rarity" }));
  for (const k of card.keywords) chips.appendChild(chip({ label: k, variant: "keyword" }));
  wrap.appendChild(chips);

  if (card.text) {
    const text = document.createElement("p");
    text.className = "card-detail-text";
    text.textContent = card.text;
    wrap.appendChild(text);
  }

  const srcLabel = document.createElement("h3");
  srcLabel.className = "card-detail-section";
  srcLabel.textContent = card.sourcePath
    ? `source — ${card.sourcePath}:${card.sourceLine}`
    : "source";
  wrap.appendChild(srcLabel);

  const pre = document.createElement("pre");
  pre.className = "card-detail-source";
  pre.textContent = card.sourcePath ? "(loading…)" : "(no source path)";
  wrap.appendChild(pre);

  container.appendChild(wrap);

  if (card.sourcePath) {
    try {
      let content = rawCache.get(card.sourcePath);
      if (content === undefined) {
        const { ok, body, status } = await api.projectFile(card.sourcePath);
        if (!ok) {
          pre.textContent = `(failed to load: HTTP ${status})`;
          return;
        }
        content = body;
        rawCache.set(card.sourcePath, content);
      }
      const block = extractSourceBlock(content, card.sourceLine);
      pre.textContent = block || "(card block not found in source)";
    } catch (err) {
      pre.textContent = `(error: ${String(err)})`;
    }
  }
}
