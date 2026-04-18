import type { CardDto } from "../../api/dtos";

export interface CardFilters {
  factions: Set<string>;
  types: Set<string>;
  costs: Set<string>;
  rarities: Set<string>;
  keywordQuery: string;
}

export function emptyFilters(): CardFilters {
  return {
    factions: new Set(),
    types: new Set(),
    costs: new Set(),
    rarities: new Set(),
    keywordQuery: "",
  };
}

export function cardCostBucket(cost: number | null): string {
  if (cost === null || cost === undefined) return "?";
  if (cost >= 6) return "6+";
  return String(cost);
}

export function applyFilters(cards: CardDto[], filters: CardFilters): CardDto[] {
  const kw = filters.keywordQuery.trim().toLowerCase();
  return cards.filter((c) => {
    if (filters.factions.size > 0 && !c.factions.some((f) => filters.factions.has(f))) return false;
    if (filters.types.size > 0 && !filters.types.has(c.type)) return false;
    if (filters.costs.size > 0 && !filters.costs.has(cardCostBucket(c.cost))) return false;
    if (filters.rarities.size > 0 && !filters.rarities.has(c.rarity)) return false;
    if (kw && !c.keywords.some((k) => k.toLowerCase().includes(kw))) return false;
    return true;
  });
}

export function uniqueValues<T extends string | number>(
  cards: CardDto[],
  pick: (c: CardDto) => T | T[] | null,
): T[] {
  const seen = new Set<T>();
  for (const c of cards) {
    const v = pick(c);
    if (v === null || v === undefined) continue;
    if (Array.isArray(v)) v.forEach((x) => seen.add(x));
    else seen.add(v);
  }
  return [...seen].sort((a, b) => String(a).localeCompare(String(b)));
}

export function extractSourceBlock(content: string, startLine: number): string {
  if (startLine <= 0) return "";
  const lines = content.split("\n");
  if (startLine > lines.length) return "";
  let depth = 0;
  let seenOpen = false;
  const out: string[] = [];
  for (let i = startLine - 1; i < lines.length; i++) {
    const line = lines[i];
    out.push(line);
    for (const ch of line) {
      if (ch === "{") {
        depth++;
        seenOpen = true;
      } else if (ch === "}") {
        depth--;
      }
    }
    if (seenOpen && depth <= 0) break;
  }
  return out.join("\n");
}
