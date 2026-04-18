#!/usr/bin/env python3
"""
Compute deficit-tilted rarity weights for the next card cluster.

Reads encoding/cards/DISTRIBUTION.md, derives current % per rarity vs. the
target (44/32/18/6), and prints a `random.choices`-ready weight list. The
tilt is `target * clamp(target/current, 0.5, 2.0)` so over-represented
rarities get downweighted and under-represented ones get upweighted, with
a cap to prevent extreme swings on small sets.

Usage:
    python3 tools/cluster-rarity-weights.py
    # -> rarities: C,U,R,M
    # -> weights:  21,42,32,3
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

TARGETS = {"C": 44.0, "U": 32.0, "R": 18.0, "M": 6.0}
RARITIES = ["C", "U", "R", "M"]
CLAMP_LO, CLAMP_HI = 0.5, 2.0


def read_actuals(dist_path: Path) -> dict[str, float]:
    """Parse the % column from DISTRIBUTION.md's Totals table."""
    text = dist_path.read_text(encoding="utf-8")
    actuals: dict[str, float] = {}
    # Matches rows like: "| C      |   23 |    1 |       3 |    27 | 44%    | 50.0%  |"
    for r in RARITIES:
        m = re.search(
            rf"^\|\s*{r}\s*\|[^|]*\|[^|]*\|[^|]*\|[^|]*\|[^|]*\|\s*([0-9.]+)%\s*\|",
            text,
            flags=re.MULTILINE,
        )
        if not m:
            print(f"warning: could not find % for rarity {r}", file=sys.stderr)
            actuals[r] = TARGETS[r]
        else:
            actuals[r] = float(m.group(1))
    return actuals


def compute_weights(actuals: dict[str, float]) -> dict[str, int]:
    out: dict[str, int] = {}
    for r in RARITIES:
        target = TARGETS[r]
        current = max(actuals[r], 1.0)
        factor = max(CLAMP_LO, min(CLAMP_HI, target / current))
        out[r] = max(1, round(target * factor))
    return out


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent
    dist_path = repo_root / "encoding" / "cards" / "DISTRIBUTION.md"
    if not dist_path.exists():
        print(f"error: {dist_path} not found", file=sys.stderr)
        return 1
    actuals = read_actuals(dist_path)
    weights = compute_weights(actuals)
    print("rarities:", ",".join(RARITIES))
    print("weights: ", ",".join(str(weights[r]) for r in RARITIES))
    print("# tilt:   target * clamp(target/current, 0.5, 2.0)")
    for r in RARITIES:
        print(
            f"#   {r}: target {TARGETS[r]:>4.1f}% / current {actuals[r]:>5.2f}% -> weight {weights[r]}"
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
