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


# Finite extraction cohort: these 26.3 additions inherit the original SlabBlock shape rule. The runtime consumes ordinary generated references and has no release-specific repair list.
_NATIVE_SLAB_COLORS = ('white', 'orange', 'magenta', 'light_blue', 'yellow', 'lime', 'pink', 'gray', 'light_gray', 'cyan', 'purple', 'blue', 'brown', 'green', 'red', 'black')
_NATIVE_SLAB_REFS = tuple('minecraft:' + color + '_' + material + '_slab' for material in ('wool', 'concrete') for color in _NATIVE_SLAB_COLORS) + ('minecraft:poplar_slab',)
_NATIVE_SLAB_MEASUREMENT_SHA256 = '0a3a3a697422b36c8d06d02a7815798d4baadb5b6cc8dcf746dea59d272aec62'
_NATIVE_SLAB_SOURCES = {
    'net/minecraft/world/level/block/SlabBlock.java': '3b4b4adf4792ab6417aedf151387abc9946dbe42c50dfacb8ddf4836e48a0d7e',
    'net/minecraft/world/level/block/Blocks.java': '9c0112d10541541760227ab0dd22f96cef49540de6ade2a4b9c74b02ef8ffb8b',
}


def _fold_native_slab_refs(pool_document: dict, refs_document: dict, blocks_document: dict, native: dict, selected: tuple[str, ...] | list[str]) -> dict:
    """Reuse the existing pool for verified native slab states without changing any other reference."""
    from copy import deepcopy
    from verify_collision_grids import verify

    verify(native)
    if not selected or len(selected) != len(set(selected)):
        raise ValueError('Expected unique slab identities')
    definitions = {block['name']: block for block in blocks_document['blocks']}
    if len(definitions) != len(blocks_document['blocks']):
        raise ValueError('Duplicate canonical block identity')
    pool = ShapePool()
    for boxes in pool_document['shapes']:
        pool.intern(boxes)
    if pool.as_list() != pool_document['shapes']:
        raise ValueError('The existing shape pool must retain its exact order and values')
    result = deepcopy(refs_document)
    for name in selected:
        block = definitions.get(name)
        if block is None:
            raise ValueError('Missing slab identity: ' + name)
        first = block.get('min_state')
        if (type(first) is not int or block.get('max_state') != first + 5 or block.get('num_states') != 6
                or block.get('default_state') != first + 3 or block.get('properties') != ['type', 'waterlogged']):
            raise ValueError('Unexpected native slab state schema: ' + name)
        indices = []
        for offset in range(6):
            key = str(first + offset)
            if key not in native['states']:
                raise ValueError('Native slab state ids are incomplete: ' + name)
            group = native['groups'][native['states'][key]]
            expected_box = [0, .5, 0, 1, 1, 1] if offset < 2 else [0, 0, 0, 1, .5, 1] if offset < 4 else [0, 0, 0, 1, 1, 1]
            expected_planes = [[0, 1], [0, .5, 1], [0, 1]] if offset < 4 else [[0, 1], [0, 1], [0, 1]]
            if (group['boxes'] != [expected_box] or group['planes'] != expected_planes
                    or group['cells'] != ([1] if offset < 2 else [0]) or group['canonical_full_block'] != (offset >= 4)):
                raise ValueError('Unexpected native slab geometry: ' + name + '/' + str(offset))
            index = pool.intern(group['boxes'])
            if index >= len(pool_document['shapes']):
                raise ValueError('Native slab must reuse the existing shape pool')
            indices.append(index)
        if name in result['collision'] and result['collision'][name] != indices:
            raise ValueError('A changed existing slab reference cannot be overwritten: ' + name)
        result['collision'][name] = indices
    selected_short = {name.removeprefix('minecraft:') for name in selected}
    result['_provenance']['unmatched_blocks'] = [name for name in refs_document['_provenance']['unmatched_blocks'] if name not in selected_short]
    return result


def supplement_native_slab_refs(data: Path, official: Path, measurements: Path, *, check: bool) -> None:
    """Fold the retained 26.3 original collision grids into only the 33 missing slab reference rows."""
    import hashlib

    from verify_collision_grids import verify

    def sha(path: Path, algorithm: str = 'sha256') -> str:
        with path.open('rb') as stream:
            return hashlib.file_digest(stream, algorithm).hexdigest()

    version, protocol = '26.3', 777
    catalog = json.loads((data / 'versions.json').read_text(encoding='utf-8'))['versions']
    if not any(row['name'] == version and row['protocol'] == protocol for row in catalog):
        raise ValueError('The native slab reference version binding differs')
    root = data / str(protocol)
    raw_path = measurements / version / 'collision-grids.json'
    if sha(raw_path) != _NATIVE_SLAB_MEASUREMENT_SHA256:
        raise ValueError('The original native slab collision measurement changed')
    rows = [row for row in json.loads((measurements / 'results.json').read_text(encoding='utf-8')) if row.get('version') == version]
    if len(rows) != 1 or rows[0].get('status') != 'complete' or rows[0].get('protocol') != protocol or rows[0].get('measurement_sha256') != sha(raw_path):
        raise ValueError('The original native slab collision receipt is missing or mismatched')
    receipt = rows[0]
    raw = json.loads(raw_path.read_text(encoding='utf-8'))
    verify(raw)
    if receipt.get('states') != len(raw['states']) or receipt.get('groups') != len(raw['groups']):
        raise ValueError('The original native slab collision counts differ')
    for name in ('CollisionGridDump.java', 'MiningDump.java', 'PushDump.java'):
        if sha(measurements / 'harness' / name) != sha(Path(__file__).parent / 'shape-extractor' / name):
            raise ValueError('The original native collision probe source changed: ' + name)
    downloads = official / 'downloads' / version
    metadata = json.loads((downloads / 'version.json').read_text(encoding='utf-8'))
    for side in ('client', 'server'):
        artifact = downloads / (side + '.jar')
        if sha(artifact, 'sha1') != metadata['downloads'][side]['sha1'] or sha(artifact) != receipt[side + '_sha256']:
            raise ValueError('The original native slab artifact changed: ' + side)
    tree = official / (version + '-client-decompiled')
    complete = json.loads((tree / '.complete.json').read_text(encoding='utf-8'))
    if complete.get('version') != version or complete.get('side') != 'client' or complete.get('artifact_sha1') != metadata['downloads']['client']['sha1']:
        raise ValueError('The completed native slab source identity differs')
    for name, digest in _NATIVE_SLAB_SOURCES.items():
        if sha(tree / name) != digest:
            raise ValueError('The native slab registration or shape source changed: ' + name)
    grid_document = json.loads((root / 'block-collision-grids.json').read_text(encoding='utf-8'))
    provenance = grid_document['_provenance']
    if (provenance.get('version') != version or provenance.get('protocol') != protocol
            or provenance.get('measurement_sha256') != sha(raw_path) or provenance.get('client_sha256') != receipt['client_sha256']
            or grid_document.get('state_count') != len(raw['states'])
            or any(grid_document.get(key) != raw[key] for key in ('schema', 'cell_order', 'states', 'groups'))):
        raise ValueError('Canonical native collision-grid provenance or content differs')
    blocks = json.loads((root / 'blocks.json').read_text(encoding='utf-8'))
    if max(block['max_state'] for block in blocks['blocks']) + 1 != len(raw['states']):
        raise ValueError('Canonical block-state registry does not match native slab measurement')
    pool = json.loads((root / 'shapes.json').read_text(encoding='utf-8'))
    path = root / 'block-shape-refs.json'
    refs = json.loads(path.read_text(encoding='utf-8'))
    if refs['_provenance'].get('version') != version or refs['_provenance'].get('protocol') != protocol or refs['_provenance'].get('kind') != 'curated-shape-bootstrap':
        raise ValueError('The base collision-reference provenance differs')
    result = _fold_native_slab_refs(pool, refs, blocks, raw, _NATIVE_SLAB_REFS)
    supplement = {'version': version, 'protocol': protocol, 'families': len(_NATIVE_SLAB_REFS), 'states': len(_NATIVE_SLAB_REFS) * 6,
                  'measurement_sha256': sha(raw_path), 'client_sha256': receipt['client_sha256'], 'server_sha256': receipt['server_sha256'],
                  'tool': 'tools/extraction/shape_extractor.py --native-slab-refs', 'native_tool': 'tools/extraction/shape-extractor/CollisionGridDump.java',
                  'native_sources_sha256': _NATIVE_SLAB_SOURCES,
                  'note': 'Original empty-context collision grids; only the 33 missing 26.3 SlabBlock families. Existing shape-pool indices and all other reference rows are preserved.'}
    old = refs['_provenance'].get('native_slab_refs')
    if old is not None and old != supplement:
        raise ValueError('The existing native slab reference provenance differs')
    result['_provenance']['native_slab_refs'] = supplement
    contents = (json.dumps(result, indent=2) + '\n').encode()
    if check:
        if path.read_bytes() != contents:
            raise ValueError('Generated native slab collision references differ: ' + str(path))
    else:
        path.write_bytes(contents)
    print(('Checked' if check else 'Extracted'), len(_NATIVE_SLAB_REFS), 'native slab collision-reference families and', len(_NATIVE_SLAB_REFS) * 6, 'states')


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description='Maintain canonical collision references from retained original native measurements')
    parser.add_argument('--native-slab-refs', action='store_true', required=True, help='fold the finite 26.3 native slab reference supplement')
    parser.add_argument('--data', type=Path, required=True)
    parser.add_argument('--official', type=Path, required=True)
    parser.add_argument('--measurements', type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    supplement_native_slab_refs(args.data, args.official, args.measurements, check=args.check)


if __name__ == '__main__':
    main()
