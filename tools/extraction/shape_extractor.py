#!/usr/bin/env python3
"""Block shape extractor.

The target design is a Java tool that drives the official server jar to dump
collision/support shapes plus friction, speed factor, jump factor,
blocks-motion, and fluid state per block state. That jar-driven path requires
remapping each obfuscated server jar through its published Proguard mappings and
compiling a program against the remapped Mojang API surface. A Java skeleton for
that path lives at tools/extraction/shape-extractor/ (see its README).

When jar-driving is infeasible, the documented fallback uses the repository's
curated shape-bootstrap asset with provenance flags.
This module implements that fallback by mapping server-report block-state ordering onto the curated bootstrap table, producing a content-addressed and deduplicated per-state AABB reference table with `curated-shape-bootstrap` provenance.

The desired friction, speed, jump, and fluid physics fields still live in `features.json`; full per-state physics data remains a jar-driven extractor deliverable.
"""

from __future__ import annotations

import json
from pathlib import Path

import decompile
import umpk_extract as ux


def _round_box(box: list[float]) -> list[float]:
    return [round(float(v), 6) for v in box]


class ShapePool:
    """Content-addressed dedup of AABB lists. Index 0 is always the empty shape."""

    def __init__(self) -> None:
        self._by_key: dict[str, int] = {}
        self._shapes: list[list[list[float]]] = []
        self.intern([])  # empty shape at index 0

    def intern(self, boxes: list[list[float]]) -> int:
        norm = [_round_box(b) for b in boxes]
        key = json.dumps(norm, separators=(",", ":"))
        if key in self._by_key:
            return self._by_key[key]
        idx = len(self._shapes)
        self._shapes.append(norm)
        self._by_key[key] = idx
        return idx

    def as_list(self) -> list[list[list[float]]]:
        return self._shapes


def _load_shape_bootstrap() -> tuple[dict[str, list], dict]:
    payload = json.loads(ux.SHAPE_BOOTSTRAP.read_text(encoding="utf-8"))
    return payload["shapes"], payload["blocks"]


# The bootstrap table is a single modern snapshot (26.x era: it carries iron_chain and short_grass, and predates golden_dandelion). A block the target version names differently, or that did not exist when the snapshot was taken, does not resolve by name, and the unresolved case used to be answered with a solid unit cube. That is how `minecraft:grass` became a full collision block on every protocol from 477 (1.14) to 763 (1.20.1): the most common non-solid block in an overworld, standing in the physics engine as a wall.
#
# Each entry below is a block the target version has and the snapshot does not, mapped to the snapshot block whose collision geometry is provably the same. The mapping is name-only; the per-state list is still consumed positionally, and every pair here has an identical state schema.
SHAPE_SOURCE_ALIASES = {
    # Renames. The old and the new name never coexist in one version, so the alias can only fire on the side of the boundary that needs it. 1.20.3 renamed grass -> short_grass (Blocks.java: "grass" in 1.20.1, "short_grass" in 1.20.4). One state, no collision either side.
    "grass": "short_grass",
    # 1.17 renamed grass_path -> dirt_path (Blocks.java: "grass_path" in 1.16.5, "dirt_path" in 1.17.1). One state, Block.box(0,0,0,16,15,16) either side.
    "grass_path": "dirt_path",
    # 1.21.9 renamed chain -> iron_chain when the copper chains arrived (Blocks.java: "chain" in 1.21.4, "iron_chain" in 1.21.9). Six states, axis x/y/z crossed with waterlogged, identical order either side.
    "chain": "iron_chain",
    # Not renames: 26.1 additions the snapshot predates. Blocks.java registers GOLDEN_DANDELION with the same `new FlowerBlock(MobEffects.SATURATION, 0.35F, p)` and the same `Properties.of().mapColor(PLANT).noCollision()...` as DANDELION, and POTTED_GOLDEN_DANDELION as `new FlowerPotBlock( GOLDEN_DANDELION, flowerPotProperties())` exactly like POTTED_DANDELION, so the collision shapes are the same block for block.
    "golden_dandelion": "dandelion",
    "potted_golden_dandelion": "potted_dandelion",
}

# An aliased list is still read POSITIONALLY, so the alias is only sound while the two blocks share a state schema. Where they do not, the target-state -> source- state index mapping has to be stated rather than assumed, keyed by (target block, target state count). Anything unmapped is refused, not mis-indexed: reading the wrong index is exactly the defect this table exists to stop.
SHAPE_SOURCE_ALIAS_STATE_MAPS = {
    # 1.16 and 1.16.1 chain carries only `waterlogged` (two states) and answers every state with one vertical box. Disassembling the shipped 1.16 and 1.16.1 server jars, class bwg (chain), gives an identical static initialiser: Block.box(6.5, 0.0, 6.5, 9.5, 16.0, 9.5) stored in the single VoxelShape field, and getShape returns that field unconditionally; the sole property is BlockStateProperties.WATERLOGGED. The `axis` property arrives in 1.16.2 (protocol 751), where the six-state schema lines up on its own. iron_chain's list is [x, x, y, y, z, z], so both states take the y entry, Block.box(6.5, 0, 6.5, 9.5, 16, 9.5), the same box the jars name.
    ("chain", 2): [2, 2],
}


def _aliased_ref(short: str, state_count: int, bootstrap_blocks: dict):
    """The bootstrap shape reference for a block the snapshot names differently, or None.

    Returns None rather than a best guess whenever the alias cannot be read
    positionally, so an unsound mapping shows up as an uncovered block instead of
    a plausible wrong box.
    """
    alias = SHAPE_SOURCE_ALIASES.get(short)
    if alias is None:
        return None
    source = bootstrap_blocks.get(alias)
    if source is None:
        return None
    if not isinstance(source, list) or len(source) == state_count:
        return source
    mapping = SHAPE_SOURCE_ALIAS_STATE_MAPS.get((short, state_count))
    if mapping is None or len(mapping) != state_count:
        return None
    return [source[i] for i in mapping]


def extract_modern(reports: Path, version: str, protocol: int) -> tuple[dict, dict]:
    """Return (shapes.json payload, block-shape-refs.json payload) for a modern
    version using the curated bootstrap table mapped onto report state order.
    """
    bootstrap_shapes, bootstrap_blocks = _load_shape_bootstrap()
    blocks_raw = json.loads((reports / "blocks.json").read_text(encoding="utf-8"))

    pool = ShapePool()
    refs: dict[str, list[int]] = {}  # block name -> per-state shape index (into pool)
    missing: list[str] = []

    for name, info in blocks_raw.items():
        short = name.split(":", 1)[-1]
        states = info["states"]
        bootstrap_ref = bootstrap_blocks.get(short)
        if bootstrap_ref is None:
            bootstrap_ref = _aliased_ref(short, len(states), bootstrap_blocks)
        if bootstrap_ref is None:
            # Nothing measured this block. Leave it OUT of the reference table so JavaBlockShapes degrades to its documented flag-derived fallback, rather than asserting a solid unit cube nobody checked. The gap is then visible: BlockShapeTableTests requires full state coverage on the flattened bands, so the next unmapped block fails a test instead of silently walling off a flower.
            missing.append(short)
            continue
        state_indices: list[int] = []
        for i, _state in enumerate(states):
            if isinstance(bootstrap_ref, list):
                shape_id = bootstrap_ref[i] if i < len(bootstrap_ref) else bootstrap_ref[-1]
            else:
                shape_id = bootstrap_ref
            boxes = bootstrap_shapes.get(str(shape_id), [])
            state_indices.append(pool.intern(boxes))
        refs[name] = state_indices

    shapes_payload = {
        "_provenance": {
            "version": version,
            "protocol": protocol,
            "mapping_source": decompile.mapping_path_for(version),
            "sources": [
                "tools/extraction/assets/block-shape-bootstrap.json",
                "reports/blocks.json",
            ],
            "kind": "curated-shape-bootstrap",
            "note": (
                "AABB shapes mapped from the curated bootstrap table onto report state "
                "order. Jar-driven Mojang extraction is the intended future source; see "
                "shape-extractor/README. friction/speed/fluid per-state fields "
                "are not yet populated (tracked deviation)."
            ),
        },
        "shapes": pool.as_list(),
    }
    refs_payload = {
        "_provenance": {
            "version": version,
            "protocol": protocol,
            "kind": "curated-shape-bootstrap",
            "unmatched_blocks": sorted(missing),
        },
        "collision": refs,
    }
    return shapes_payload, refs_payload
