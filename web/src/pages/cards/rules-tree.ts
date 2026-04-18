import type { CardDto, ProjectDto } from "../../api/dtos";
import { api } from "../../api/client";
import { extractSourceBlock } from "./filter";

interface RulesGroup {
  kind: "Entity" | "Card" | "Token" | "Augment" | "Other";
  label: string;
  count: number;
  items: RulesItem[];
}

interface RulesItem {
  label: string;          // e.g., "Card Spark"
  name: string;           // e.g., "Spark" — identifier used for lookups
  path: string;           // source path when known
  line: number;           // source line when known
  group: RulesGroup["kind"];
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

  const groups = buildGroups(project, cards);
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
        const card = cards.find((c) => c.name === item.name);
        const faction = card?.factions[0] ?? "NEUTRAL";
        if (!byFaction.has(faction)) byFaction.set(faction, []);
        byFaction.get(faction)!.push(item);
      }
      const factionList = [...byFaction.keys()].sort();
      for (const faction of factionList) {
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

    details.addEventListener(
      "toggle",
      () => {
        if (!details.open || pre.dataset.loaded === "1" || !item.path) return;
        pre.textContent = "(loading…)";
        void loadSource(item, rawCache).then((src) => {
          pre.textContent = src;
          pre.dataset.loaded = "1";
        });
      },
      { once: false },
    );

    li.appendChild(details);
    ul.appendChild(li);
  }
  return ul;
}

async function loadSource(
  item: RulesItem,
  rawCache: Map<string, string>,
): Promise<string> {
  try {
    let content = rawCache.get(item.path);
    if (content === undefined) {
      const { ok, body, status } = await api.projectFile(item.path);
      if (!ok) return `(failed to load: HTTP ${status})`;
      content = body;
      rawCache.set(item.path, content);
    }
    const line = item.line > 0 ? item.line : locateDeclaration(content, item);
    if (line <= 0) return "(declaration block not found in source)";
    const block = extractSourceBlock(content, line);
    return block || "(declaration block not found in source)";
  } catch (err) {
    return `(error: ${String(err)})`;
  }
}

function locateDeclaration(content: string, item: RulesItem): number {
  if (item.group === "Augment") {
    const pattern = new RegExp(
      `^\\s*${escapeRegex(item.name)}\\s*(\\+?=)`,
      "m",
    );
    const m = pattern.exec(content);
    if (!m) return 0;
    return lineOf(content, m.index);
  }
  const pattern = new RegExp(
    `^\\s*${item.group}\\s+${escapeRegex(item.name)}\\b`,
    "m",
  );
  const m = pattern.exec(content);
  if (!m) return 0;
  return lineOf(content, m.index);
}

function lineOf(content: string, offset: number): number {
  let line = 1;
  for (let i = 0; i < offset; i++) if (content[i] === "\n") line++;
  return line;
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function buildGroups(project: ProjectDto, cards: CardDto[]): RulesGroup[] {
  const kinds: Array<[RulesGroup["kind"], string]> = [
    ["Entity", "Entities"],
    ["Card", "Cards"],
    ["Token", "Tokens"],
    ["Augment", "Augmentations"],
  ];
  const groups: RulesGroup[] = [];
  for (const [kind, label] of kinds) {
    const count = project.declarations.counts[kind] ?? 0;
    const items = itemsFor(kind, project, cards);
    groups.push({ kind, label, count, items });
  }
  return groups;
}

function itemsFor(
  kind: RulesGroup["kind"],
  project: ProjectDto,
  cards: CardDto[],
): RulesItem[] {
  if (kind === "Card") {
    return cards.map((c) => ({
      group: kind,
      label: `Card ${c.name}`,
      name: c.name,
      path: c.sourcePath,
      line: c.sourceLine,
    }));
  }
  const prefix = `${kind} `;
  const items: RulesItem[] = [];
  for (const [path, labels] of Object.entries(project.declarations.byFile)) {
    for (const raw of labels) {
      if (!raw.startsWith(prefix)) continue;
      if (raw.startsWith("Card ")) continue;
      if (kind === "Entity" && raw.startsWith("Augment ")) continue;
      // Extract a canonical name for lookups: strip the kind prefix; if the
      // label has a [param] trailer (parametric entity), drop it.
      const body = raw.slice(prefix.length);
      const name = body.split("[")[0];
      items.push({ group: kind, label: raw, name, path, line: 0 });
    }
  }
  if (kind === "Augment") {
    items.length = 0;
    for (const [path, labels] of Object.entries(project.declarations.byFile)) {
      for (const raw of labels) {
        if (!raw.startsWith("Augment ")) continue;
        items.push({
          group: kind,
          label: raw,
          name: raw.slice("Augment ".length),
          path,
          line: 0,
        });
      }
    }
  }
  items.sort((a, b) => a.label.localeCompare(b.label));
  return items;
}
