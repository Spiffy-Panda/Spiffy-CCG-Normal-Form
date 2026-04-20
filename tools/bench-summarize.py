#!/usr/bin/env python3
"""Summarise a tournament results JSON.

Usage:
  tools/bench-summarize.py <results.json> [--baseline <baseline.json>] [--matchups]

Reads a result file produced by POST /api/ai/tournament/run (or equivalently
stored under ai-testing-data/) and prints:

  - Overall draw rate.
  - Per-pair records, sorted by decisive WR (wins / (wins+losses)).
  - Optionally, per-matchup cells (--matchups).
  - Optionally, deltas vs a baseline result (--baseline).

Written during the step-12 balance arc after several sessions of repeating
the same Python one-liners in the REPL. Saves ~60 seconds per bench review.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


# Force stdout to UTF-8 so unicode arrows / symbols don't crash on cp1252.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def decisive_wr(p: dict) -> float:
    dec = p["wins"] + p["losses"]
    return 100 * p["wins"] / dec if dec else 0.0


def fmt_delta(cur: float, base: float) -> str:
    d = cur - base
    sign = "+" if d >= 0 else "-"
    return f"({sign}{abs(d):5.1f} pp)"


def load(path: Path) -> dict:
    if not path.exists():
        print(f"error: {path} does not exist", file=sys.stderr)
        sys.exit(2)
    return json.loads(path.read_text(encoding="utf-8"))


def summarize(
    data: dict,
    *,
    baseline: dict | None = None,
    show_matchups: bool = False,
) -> None:
    name = data.get("config", {}).get("name") or data.get("name", "<unnamed>")
    matchups = data["matchups"]
    total_games = sum(m["games"] for m in matchups)
    total_draws = sum(m["draws"] for m in matchups)
    draw_pct = 100 * total_draws / total_games if total_games else 0.0

    print(f"Tournament: {name}")
    print(f"Games:      {total_games}")
    print(f"Draws:      {total_draws} ({draw_pct:.1f} %)")
    if baseline is not None:
        b_matchups = baseline["matchups"]
        b_games = sum(m["games"] for m in b_matchups)
        b_draws = sum(m["draws"] for m in b_matchups)
        b_pct = 100 * b_draws / b_games if b_games else 0.0
        print(f"            baseline {b_draws}/{b_games} ({b_pct:.1f} %) {fmt_delta(draw_pct, b_pct)}")
    print()

    print("Per-pair (sorted by decisive WR):")
    pairs = sorted(data["pairs"], key=decisive_wr, reverse=True)
    baseline_pairs = {p["pairId"]: p for p in baseline["pairs"]} if baseline else {}

    header = f"  {'pairId':<10} {'W':>4} {'L':>4} {'D':>4}  rawWR    decWR"
    if baseline:
        header += "   (vs baseline)"
    print(header)
    for p in pairs:
        raw = p["winRate"] * 100
        dec = decisive_wr(p)
        row = f"  {p['pairId']:<10} {p['wins']:>4} {p['losses']:>4} {p['draws']:>4}  {raw:5.1f} %  {dec:5.1f} %"
        if baseline and p["pairId"] in baseline_pairs:
            b = baseline_pairs[p["pairId"]]
            row += f"   dec {fmt_delta(dec, decisive_wr(b))}"
        print(row)

    if show_matchups:
        print()
        print("Per-matchup:")
        for m in matchups:
            a, bp = m["aPairId"], m["bPairId"]
            aw, bw, dr = m["aWins"], m["bWins"], m["draws"]
            print(f"  {a:<10} vs {bp:<10}  {aw:>3}/{bw:>3}/{dr:>3}  aWR={m['aWinRate']*100:5.1f} %  steps={m['avgSteps']:6.1f}")


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Summarise a tournament results JSON.")
    ap.add_argument("results", type=Path, help="Path to <name>.results.json")
    ap.add_argument("--baseline", type=Path, help="Optional baseline results for delta comparison")
    ap.add_argument("--matchups", action="store_true", help="Also print per-matchup cells")
    args = ap.parse_args(argv[1:])

    data = load(args.results)
    baseline = load(args.baseline) if args.baseline else None
    summarize(data, baseline=baseline, show_matchups=args.matchups)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
