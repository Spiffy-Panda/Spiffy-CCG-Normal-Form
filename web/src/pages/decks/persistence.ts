import type { DeckEntry } from "./distribution";

export interface SavedDeck {
  name: string;
  format: string;
  createdAt: string;
  cards: DeckEntry[];
}

const PREFIX = "deck:";

export function keyFor(format: string, name: string): string {
  return `${PREFIX}${format}:${name}`;
}

export function listSavedDecks(format: string): string[] {
  const out: string[] = [];
  const prefix = `${PREFIX}${format}:`;
  for (let i = 0; i < window.localStorage.length; i++) {
    const key = window.localStorage.key(i);
    if (key?.startsWith(prefix)) out.push(key.slice(prefix.length));
  }
  return out.sort();
}

export function loadDeck(format: string, name: string): SavedDeck | null {
  const raw = window.localStorage.getItem(keyFor(format, name));
  if (!raw) return null;
  try {
    return JSON.parse(raw) as SavedDeck;
  } catch {
    return null;
  }
}

export function saveDeck(deck: SavedDeck): void {
  window.localStorage.setItem(keyFor(deck.format, deck.name), JSON.stringify(deck));
}

export function deleteDeck(format: string, name: string): void {
  window.localStorage.removeItem(keyFor(format, name));
}
