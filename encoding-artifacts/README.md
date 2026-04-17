# encoding-artifacts/

Holds the output of the `card-cluster` skill (see `.claude/skills/card-cluster/SKILL.md`).

Two kinds of files live here:

- **`working-<UTC-timestamp>.ccgnf`** — in-flight clusters, ignored by git. These exist only while the skill is iterating with the user. Once the cluster is approved and copied into `encoding/cards/`, the file is renamed (below).

- **`cluster-<UTC-timestamp>.ccgnf`** — finalized cluster archives. Tracked by git as the historical record of what was added, including the seed used. Useful for reproducing a cluster or auditing which cards came from which generation run.

## Why keep finalized artifacts?

The canonical card source is under `encoding/cards/`. The archive here is redundant for *execution* but useful for *provenance*: each `cluster-*.ccgnf` file records the seed and the original block-comment descriptions from the cluster run, which is harder to reconstruct from the merged-into-faction form.

## Regenerating a cluster

To reproduce a cluster from its archive: read the header of the archive file, note the seed, invoke the skill with that seed. The PRNG streams are deterministic so structural choices are identical; narrative content (names, flavor, prose) will vary because the LLM is not seeded.
