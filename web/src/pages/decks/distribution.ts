import type { CardDto, DistributionDto } from "../../api/dtos";
import { cardCostBucket } from "../cards/filter";

export interface DeckEntry {
  name: string;
  count: number;
}

export function summarize(deck: DeckEntry[], cards: CardDto[]): DistributionDto {
  const byName = new Map(cards.map((c) => [c.name, c]));
  const faction: Record<string, number> = {};
  const type: Record<string, number> = {};
  const cost: Record<string, number> = {};
  const rarity: Record<string, number> = {};

  for (const entry of deck) {
    const card = byName.get(entry.name);
    if (!card) continue;
    for (const f of card.factions) faction[f] = (faction[f] ?? 0) + entry.count;
    if (card.type) type[card.type] = (type[card.type] ?? 0) + entry.count;
    const bucket = cardCostBucket(card.cost);
    cost[bucket] = (cost[bucket] ?? 0) + entry.count;
    if (card.rarity) rarity[card.rarity] = (rarity[card.rarity] ?? 0) + entry.count;
  }
  return { faction, type, cost, rarity };
}

export function deckTotalCards(deck: DeckEntry[]): number {
  return deck.reduce((acc, e) => acc + e.count, 0);
}
