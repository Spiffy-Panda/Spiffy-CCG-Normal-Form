#!/usr/bin/env python3
"""Slice an AIxDeck matrix tournament by AI and by deck.

Usage:
  tools/bench-matrix.py <results.json> [--baseline <baseline.json>]

The script auto-detects the set of decks and bots from the result file's
pairs (no hard-coded pairId parsing). For each (deck, bot) cell it reports
decisive WR; aggregates by deck and by bot are printed underneath.

Works on any tournament whose pairs cover a product of multiple decks and
multiple bots. The intended input is the AiDeckMatrix config
(4 AIs x 4 decks = 16 pairs), but it also handles 2x2, 3x3, etc. If the
pair list isn't a full product the missing cells render as '--'.

With --baseline, prints (cur -> prev) deltas so you can see how a deck /
weights / engine edit moved each cell relative to a prior bench. The
baseline must be a result from the same tournament shape — same set of
decks and bots — or the diff is meaningless.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


# Short labels for well-known deck / bot ids. Falls back to ids if unmapped.
DECK_LABELS = {
    "bulwark-control": "bulwark-ctl",
    "ember-aggro": "ember-aggro",
    "hollow-disruption": "hollow-disr",
    "tide-thorn-combo": "tide-thorn",
}
BOT_LABELS = {
    "experimental/2026-04-19-fortress": "fortress",
    "experimental/2026-04-19-hellfire": "hellfire",
    "experimental/2026-04-19-reaper": "reaper",
    "experimental/2026-04-19-wavebreaker": "wavebreaker",
    "utility": "utility",
    "fixed": "fixed",
}


def short_deck(deck_id: str) -> str:
    return DECK_LABELS.get(deck_id, deck_id[:12])


def short_bot(bot_id: str) -> str:
    return BOT_LABELS.get(bot_id, bot_id[:12])


def decisive_wr(p: dict) -> float:
    dec = p["wins"] + p["losses"]
    return 100 * p["wins"] / dec if dec else 0.0


def load(path: Path) -> dict:
    if not path.exists():
        print(f"error: {path} does not exist", file=sys.stderr)
        sys.exit(2)
    return json.loads(path.read_text(encoding="utf-8"))


def build_cell_lookup(data: dict) -> dict[tuple[str, str], dict]:
    """Return (deckId, botProfile) -> pair row from the result."""
    return {(p["deckId"], p["botProfile"]): p for p in data["pairs"]}


def print_matrix(
    data: dict,
    baseline: dict | None = None,
) -> None:
    name = data.get("config", {}).get("name") or data.get("name", "<unnamed>")
    matchups = data["matchups"]
    total_games = sum(m["games"] for m in matchups)
    total_draws = sum(m["draws"] for m in matchups)

    print(f"Tournament: {name}")
    print(
        f"Games:      {total_games}, draws {total_draws} "
        f"({100*total_draws/total_games if total_games else 0:.1f} %)"
    )

    pairs = data["pairs"]
    decks = sorted({p["deckId"] for p in pairs})
    bots = sorted({p["botProfile"] for p in pairs})
    cells = build_cell_lookup(data)
    base_cells = build_cell_lookup(baseline) if baseline else None

    # ---- Matrix ----
    print()
    print("Matrix — decisive WR (wins / (wins + losses)):")
    print()

    name_width = 16
    col_width = 14
    hdr = " " * name_width
    for b in bots:
        hdr += f"{short_bot(b):>{col_width}}"
    print(hdr)

    for deck in decks:
        row = f"{short_deck(deck):<{name_width}}"
        for bot in bots:
            cell = cells.get((deck, bot))
            if cell is None:
                row += f"{'--':>{col_width}}"
                continue
            wr = decisive_wr(cell)
            if baseline and base_cells and (deck, bot) in base_cells:
                bwr = decisive_wr(base_cells[(deck, bot)])
                delta = wr - bwr
                sign = "+" if delta >= 0 else "-"
                row += f"{wr:6.1f} ({sign}{abs(delta):4.1f})"
            else:
                row += f"{wr:>{col_width - 2}.1f} %"
        print(row)

    # ---- Per-deck aggregate ----
    print()
    print("Per-deck (aggregate across all bots):")
    for deck in decks:
        rows = [cells[(deck, bot)] for bot in bots if (deck, bot) in cells]
        _print_aggregate(short_deck(deck), rows, baseline, deck, bots, "deck", base_cells)

    # ---- Per-AI aggregate ----
    print()
    print("Per-AI (aggregate across all decks):")
    for bot in bots:
        rows = [cells[(deck, bot)] for deck in decks if (deck, bot) in cells]
        _print_aggregate(short_bot(bot), rows, baseline, bot, decks, "ai", base_cells)


def _print_aggregate(
    label: str,
    rows: list[dict],
    baseline: dict | None,
    group_key: str,
    others: list[str],
    mode: str,
    base_cells: dict | None,
) -> None:
    tw = sum(p["wins"] for p in rows)
    tl = sum(p["losses"] for p in rows)
    td = sum(p["draws"] for p in rows)
    dec = tw + tl
    dwr = 100 * tw / dec if dec else 0.0

    line = f"  {label:<14} {tw:>4}-{tl:>4}-{td:>4}   decWR={dwr:5.1f} %"

    if baseline and base_cells:
        if mode == "deck":
            brows = [base_cells[(group_key, b)] for b in others if (group_key, b) in base_cells]
        else:
            brows = [base_cells[(d, group_key)] for d in others if (d, group_key) in base_cells]
        btw = sum(p["wins"] for p in brows)
        btl = sum(p["losses"] for p in brows)
        bdec = btw + btl
        bdwr = 100 * btw / bdec if bdec else 0.0
        delta = dwr - bdwr
        sign = "+" if delta >= 0 else "-"
        line += f"   baseline {bdwr:5.1f} %  ({sign}{abs(delta):5.1f} pp)"

    print(line)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Slice an AIxDeck matrix tournament by AI and by deck.")
    ap.add_argument("results", type=Path, help="Path to matrix tournament results")
    ap.add_argument("--baseline", type=Path, help="Optional prior results for delta comparison")
    args = ap.parse_args(argv[1:])

    data = load(args.results)
    baseline = load(args.baseline) if args.baseline else None
    print_matrix(data, baseline=baseline)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
