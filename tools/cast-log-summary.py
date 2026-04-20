#!/usr/bin/env python3
"""Alphabetical summary of a CCGNF cast log.

Reads the JSONL stream produced when the interpreter runs with
CCGNF_CAST_LOG=<path> and prints one row per card name, sorted
alphabetically, so you can eyeball whether each card's declared
triggered abilities actually fired during a tournament.

Two record kinds are read:
  - {"kind":"cast",    "card": ..., "keywords": [...], "abilities": [...]}
  - {"kind":"trigger", "owner_card": ..., "event": ...}

A card whose row reports `cast: N, triggers: <none>` despite declaring
an ability with `on: Event.X(...)` is a debugging signal -- the cast
landed but the dispatch did not fire the trigger.

Usage:
    python3 tools/cast-log-summary.py <path/to/cast-log.jsonl>
"""
from __future__ import annotations

import json
import sys
from collections import defaultdict


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: cast-log-summary.py <path/to/cast-log.jsonl>", file=sys.stderr)
        return 2

    path = argv[1]

    casts: dict[str, int] = defaultdict(int)
    triggers: dict[str, dict[str, int]] = defaultdict(lambda: defaultdict(int))
    keywords_seen: dict[str, set[str]] = defaultdict(set)
    declared_triggers: dict[str, set[str]] = defaultdict(set)
    declared_ability_kinds: dict[str, set[str]] = defaultdict(set)
    declared_types: dict[str, str] = {}
    all_cards: set[str] = set()

    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                continue
            kind = rec.get("kind")
            if kind == "cast":
                name = rec.get("card", "?")
                all_cards.add(name)
                casts[name] += 1
                for kw in rec.get("keywords", []):
                    keywords_seen[name].add(kw)
                declared_types[name] = rec.get("type", "?")
                for ab in rec.get("abilities", []):
                    ab_kind = ab.get("kind")
                    if ab_kind:
                        declared_ability_kinds[name].add(ab_kind)
                    on = ab.get("on")
                    if on:
                        declared_triggers[name].add(on)
            elif kind == "trigger":
                name = rec.get("owner_card", "?")
                all_cards.add(name)
                evt = rec.get("event", "?")
                triggers[name][evt] += 1

    if not all_cards:
        print(f"(no records in {path})")
        return 0

    # Column widths.
    name_w = max(12, max(len(c) for c in all_cards))
    print(f"{'card':<{name_w}}  type        cast  triggers                                    declared")
    print(f"{'-'*name_w}  ----------  ----  ------------------------------------------  --------")
    # Ability kinds that DON'T go through the Triggered dispatcher and so
    # naturally won't show up as trigger-fires in the log even when they
    # work. OnResolve runs inline from PlayManeuver; Static is consulted
    # by KeywordRuntime / continuous passes. Excluded from the silent flag.
    dispatch_kinds = {"Triggered", "OnEnter", "OnPlayed", "OnCardPlayed",
                      "OnArenaEnter", "StartOfYourTurn", "EndOfYourTurn",
                      "StartOfClash", "EndOfClash"}

    for name in sorted(all_cards, key=str.casefold):
        typ = declared_types.get(name, "?")
        cast = casts.get(name, 0)
        trig_map = triggers.get(name, {})
        trig_str = ", ".join(f"{k}={v}" for k, v in sorted(trig_map.items())) or "<none>"
        # Prefer the ability kinds (OnEnter / Triggered / Static / ...) so
        # unexpanded shorthand macros are visible; if we saw an explicit
        # `on:` pattern, append it too.
        ability_kinds = sorted(declared_ability_kinds.get(name, set()))
        triggered_pats = sorted(declared_triggers.get(name, set()))
        declared_parts: list[str] = []
        if ability_kinds:
            declared_parts.extend(ability_kinds)
        if triggered_pats:
            declared_parts.append("[" + "; ".join(triggered_pats) + "]")
        declared = ", ".join(declared_parts) or "-"
        flag = ""
        dispatching = {k for k in ability_kinds if k in dispatch_kinds}
        if dispatching and not trig_map:
            flag = "  !! declared-but-silent"
        print(f"{name:<{name_w}}  {typ:<10}  {cast:>4}  {trig_str:<42}  {declared}{flag}")

    # Summary footer.
    total_casts = sum(casts.values())
    total_triggers = sum(sum(tm.values()) for tm in triggers.values())
    silent = [
        c for c in all_cards
        if (declared_ability_kinds.get(c, set()) & dispatch_kinds)
        and not triggers.get(c)
    ]
    # Shorthand-triggers (OnEnter, EndOfClash, OnCardPlayed, StartOfYourTurn,
    # EndOfYourTurn, StartOfClash, OnArenaEnter, OnPlayed) show up in the
    # log as their pre-expansion identifier when the preprocessor didn't
    # expand them -- a reliable signal that the shorthand macro from
    # encoding/engine/02-trigger-shorthands.ccgnf wasn't in scope when the
    # card was processed. Worth surfacing because those cards' Triggered
    # dispatch is dead regardless of the interpreter wiring.
    shorthands = {
        "OnEnter", "OnPlayed", "OnCardPlayed", "OnArenaEnter",
        "StartOfYourTurn", "EndOfYourTurn", "StartOfClash", "EndOfClash",
    }
    unexpanded = sorted({
        f"{card}:{ab}"
        for card in all_cards
        for ab in declared_ability_kinds.get(card, set())
        if ab in shorthands
    }, key=str.casefold)
    print()
    print(f"Totals: {len(all_cards)} distinct cards, "
          f"{total_casts} casts, {total_triggers} trigger-fires.")
    if silent:
        print(f"Declared-but-silent cards ({len(silent)}): {', '.join(sorted(silent, key=str.casefold))}")
    if unexpanded:
        print(f"Unexpanded shorthand triggers ({len(unexpanded)}): "
              f"{', '.join(unexpanded[:8])}{'...' if len(unexpanded) > 8 else ''}")
        print("  (these mean the preprocessor didn't expand the shorthand "
              "macro -- see docs/plan/engine-completion-guide.md.)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
