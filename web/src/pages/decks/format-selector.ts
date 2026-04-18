export interface Format {
  id: string;
  label: string;
  maxCopies: number;
  deckSize: number;
  needsMockPool: boolean;
}

export const FORMATS: Format[] = [
  { id: "constructed", label: "Constructed", maxCopies: 4, deckSize: 40, needsMockPool: false },
  { id: "draft", label: "Draft (mock)", maxCopies: 4, deckSize: 40, needsMockPool: true },
];

export function formatById(id: string): Format {
  return FORMATS.find((f) => f.id === id) ?? FORMATS[0];
}
