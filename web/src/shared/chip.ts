export interface ChipOptions {
  label: string;
  variant?: "faction" | "type" | "keyword" | "rarity" | "cost" | "neutral";
  title?: string;
}

export function chip(opts: ChipOptions): HTMLSpanElement {
  const span = document.createElement("span");
  span.className = `chip chip-${opts.variant ?? "neutral"}`;
  span.textContent = opts.label;
  if (opts.title) span.title = opts.title;
  return span;
}
