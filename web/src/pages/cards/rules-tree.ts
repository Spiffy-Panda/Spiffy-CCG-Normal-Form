import type { CardDto, ProjectDto } from "../../api/dtos";
import { api } from "../../api/client";
import { extractSourceBlock } from "./filter";

type Kind = "Entity" | "Card" | "Token" | "Augment";

interface RulesGroup {
  kind: Kind;
  label: string;
  count: number;
  items: RulesItem[];
}

interface RulesItem {
  kind: Kind;
  label: string;   // e.g., "Card Spark", "Augment Game.abilities"
  path: string;    // source path
  line: number;    // 1-based line of the declaration's opening token
}

export function renderRulesTree(
  container: HTMLElement,
  project: ProjectDto,
  cards: CardDto[],
  rawCache: Map<string, string>,
): void {
  container.innerHTML = "";

  const wrap = document.createElement("div");
  wrap.className = "rules-tree";

  const intro = document.createElement("p");
  intro.className = "muted rules-tree-intro";
  intro.textContent =
    "Declaration tree. Each leaf expands to its raw .ccgnf source; a human-readable render will land in a later step.";
  wrap.appendChild(intro);

  const groups = buildGroups(project);
  for (const group of groups) {
    const section = document.createElement("details");
    section.className = "rules-group";
    if (group.kind === "Entity") section.open = true;

    const summary = document.createElement("summary");
    summary.className = "rules-group-summary";
    summary.textContent = `${group.label} (${group.count})`;
    section.appendChild(summary);

    if (group.kind === "Card") {
      const byFaction = new Map<string, RulesItem[]>();
      for (const item of group.items) {
        const name = item.label.replace(/^Card\s+/, "");
        const card = cards.find((c) => c.name === name);
        const faction = card?.factions[0] ?? "NEUTRAL";
        if (!byFaction.has(faction)) byFaction.set(faction, []);
        byFaction.get(faction)!.push(item);
      }
      for (const faction of [...byFaction.keys()].sort()) {
        const items = byFaction.get(faction)!;
        const sub = document.createElement("details");
        sub.className = "rules-subgroup";
        const subSummary = document.createElement("summary");
        subSummary.textContent = `${faction} (${items.length})`;
        sub.appendChild(subSummary);
        sub.appendChild(renderItemList(items, rawCache));
        section.appendChild(sub);
      }
    } else {
      section.appendChild(renderItemList(group.items, rawCache));
    }

    wrap.appendChild(section);
  }

  if (project.macros.length > 0) {
    const macros = document.createElement("details");
    macros.className = "rules-group";
    const summary = document.createElement("summary");
    summary.className = "rules-group-summary";
    summary.textContent = `Macros (${project.macros.length})`;
    macros.appendChild(summary);
    const list = document.createElement("ul");
    list.className = "rules-macros";
    for (const name of project.macros) {
      const li = document.createElement("li");
      li.textContent = name;
      list.appendChild(li);
    }
    macros.appendChild(list);
    wrap.appendChild(macros);
  }

  container.appendChild(wrap);
}

function renderItemList(items: RulesItem[], rawCache: Map<string, string>): HTMLElement {
  const ul = document.createElement("ul");
  ul.className = "rules-items";
  for (const item of items) {
    const li = document.createElement("li");
    const details = document.createElement("details");
    details.className = "rules-item";
    const summary = document.createElement("summary");
    summary.className = "rules-item-summary";
    const label = document.createElement("span");
    label.textContent = item.label;
    summary.appendChild(label);
    if (item.path) {
      const loc = document.createElement("span");
      loc.className = "muted rules-item-loc";
      loc.textContent = ` ${item.path}:${item.line}`;
      summary.appendChild(loc);
    }
    details.appendChild(summary);

    const pre = document.createElement("pre");
    pre.className = "rules-item-source";
    pre.textContent = item.path ? "(click to load)" : "(no source path)";
    details.appendChild(pre);

    details.addEventListener("toggle", () => {
      if (!details.open || pre.dataset.loaded === "1" || !item.path) return;
      pre.textContent = "(loading…)";
      void loadSource(item, rawCache).then((src) => {
        pre.textContent = src;
        pre.dataset.loaded = "1";
      });
    });

    li.appendChild(details);
    ul.appendChild(li);
  }
  return ul;
}

async function loadSource(item: RulesItem, rawCache: Map<string, string>): Promise<string> {
  try {
    let content = rawCache.get(item.path);
    if (content === undefined) {
      const { ok, body, status } = await api.projectFile(item.path);
      if (!ok) return `(failed to load: HTTP ${status})`;
      content = body;
      rawCache.set(item.path, content);
    }
    const block = extractSourceBlock(content, item.line);
    return block || "(declaration block not found in source)";
  } catch (err) {
    return `(error: ${String(err)})`;
  }
}

function buildGroups(project: ProjectDto): RulesGroup[] {
  const kinds: Array<[Kind, string]> = [
    ["Entity", "Entities"],
    ["Card", "Cards"],
    ["Token", "Tokens"],
    ["Augment", "Augmentations"],
  ];
  const groups: RulesGroup[] = [];
  for (const [kind, label] of kinds) {
    const count = project.declarations.counts[kind] ?? 0;
    const items: RulesItem[] = [];
    for (const [path, entries] of Object.entries(project.declarations.byFile)) {
      for (const entry of entries) {
        if (!entry.label.startsWith(`${kind} `)) continue;
        items.push({ kind, label: entry.label, path, line: entry.line });
      }
    }
    if (kind !== "Card") items.sort((a, b) => a.label.localeCompare(b.label));
    groups.push({ kind, label, count, items });
  }
  return groups;
}
