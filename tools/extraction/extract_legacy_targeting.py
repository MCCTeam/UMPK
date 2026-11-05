"""Fold native legacy selection geometry and measured neighbor lookups into targeting tables."""

import argparse
import copy
import hashlib
import json
from pathlib import Path

from expand_legacy_outlines import expand_metadata
from extract_outlines import fold_outlines
from verify_legacy_outlines import verify


def ranges(values: list[int]) -> list[list[int]]:
    result = []
    for index, value in enumerate(values):
        if result and result[-1][0] + result[-1][1] == index and result[-1][2] == value:
            result[-1][1] += 1
        else:
            result.append([index, 1, value])
    return result


def fold_legacy(expanded: dict, doors: dict, state_count: int) -> dict:
    empty = {"boxes": [], "offset": "none", "max_horizontal_offset": 0, "max_vertical_offset": 0}
    native = expanded["states"]
    raw = {}
    for state in range(state_count):
        row = copy.deepcopy(native.get(str(state), {"block": "umpk:unmeasured", "outline": empty, "interaction": empty}))
        if row["block"] == "minecraft:air":
            row["outline"] = copy.deepcopy(empty)
        if row.get("trace_selection") == "farthest_from_end":
            row["outline"]["clip_model"] = "aabb_list"
        raw[str(state)] = row
    table = fold_outlines(raw, {int(state): row["block"] for state, row in raw.items()}, set(), "aabb")
    pool = table["shape_pool"]
    identities = {json.dumps(shape, sort_keys=True): index for index, shape in enumerate(pool)}

    def intern(shape: dict) -> int:
        # Reuse the strict shape normalization used by flattened measurements.
        normalized = fold_outlines({"0": {"block": "umpk:context", "outline": shape, "interaction": empty}}, {0: "umpk:context"}, set(), "aabb")["shape_pool"][0]
        key = json.dumps(normalized, sort_keys=True)
        if key not in identities:
            identities[key] = len(pool)
            pool.append(normalized)
        return identities[key]

    empty_index = intern(empty)
    context_shapes = [intern(shape) for shape in doors["shape_pool"]]
    canonical = {str(row["canonical_state"]) for row in native.values()}
    nodes, roots = [], []
    node_indices = {}
    for wire, center in sorted(native.items(), key=lambda entry: int(entry[0])):
        rule = doors["doors"].get(str(center["canonical_state"]))
        if rule is None:
            continue
        if rule["block"] != center["block"]:
            raise ValueError(f"Neighbor context names a different center block at {wire}")
        if set(rule["by_state"]) != canonical:
            raise ValueError(f"Incomplete native neighbor coverage at {wire}")
        if rule["offset"] not in ([0, 1, 0], [0, -1, 0]):
            raise ValueError("Unexpected native door neighbor offset")
        if any(type(index) is not int or not 0 <= index < len(context_shapes) for index in rule["by_state"].values()):
            raise ValueError("Neighbor context refers to an unmeasured shape")
        # Unloaded neighbors are air. Explicitly rejected native metadata remains unselectable.
        values = [context_shapes[rule["by_state"]["0"]]] * state_count
        for neighbor_wire, neighbor in native.items():
            values[int(neighbor_wire)] = context_shapes[rule["by_state"][str(neighbor["canonical_state"])]]
        for rejected in expanded["rejected_metadata"]:
            values[int(rejected)] = empty_index
        node = {"offset": rule["offset"], "ranges": ranges(values)}
        identity = json.dumps(node, sort_keys=True)
        if identity not in node_indices:
            node_indices[identity] = len(nodes)
            nodes.append(node)
        index = node_indices[identity]
        state = int(wire)
        if roots and roots[-1][0] + roots[-1][1] == state and roots[-1][2] == index:
            roots[-1][1] += 1
        else:
            roots.append([state, 1, index])
    table["context_nodes"], table["context_roots"] = nodes, roots
    return table


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, type=Path)
    parser.add_argument("--out", type=Path, help="Output dataset root; defaults to --data")
    args = parser.parse_args()
    catalog = json.loads((args.data / "versions.json").read_text())["versions"]
    client_reports = {row["version"]: row for row in json.loads((args.measurements / "legacy-outline-native-client-results.json").read_text())}
    alias_reports = {row["version"]: row for row in json.loads((args.measurements / "legacy-state-alias-results.json").read_text())}
    door_reports = {row["version"]: row for row in json.loads((args.measurements / "legacy-door-outline-results.json").read_text())}
    grouped = {}
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        if protocol >= 393:
            continue
        directory = args.measurements / version
        client, aliases, doors = client_reports[version], alias_reports[version], door_reports[version]
        if any(row["status"] != "complete" for row in (client, aliases, doors)):
            raise ValueError(f"Incomplete native measurements for {version}")
        for row, names in ((client, ["outline-native-client.json"]), (doors, ["door-outline-native.json", "door-outline-symbols.json", "state-native-aliases.json"])):
            for name in names:
                if sha(directory / name) != row["sha256"][name]:
                    raise ValueError(f"Native measurement changed: {version}/{name}")
        if sha(directory / "state-native-aliases.json") != aliases["sha256"]:
            raise ValueError(f"Native aliases changed for {version}")
        metadata = json.loads((args.official / "downloads" / version / "version.json").read_text())
        for side in ("client", "server"):
            artifact = args.official / "downloads" / version / (side + ".jar")
            if hashlib.sha1(artifact.read_bytes()).hexdigest() != metadata["downloads"][side]["sha1"]:
                raise ValueError(f"Official {side} artifact changed for {version}")
        bindings = json.loads((directory / "door-outline-symbols.json").read_text())
        tree = args.official / (version + "-decompiled")
        relevant = {bindings["block"], bindings["state"], *bindings["outline_bindings"]["overrides"]}
        for role in ("world", "position", "getter", "vector_type", "hit_type", "door_class", "box_type"):
            if bindings["outline_bindings"].get(role):
                relevant.add(bindings["outline_bindings"][role])
        sources = {}
        for name in relevant:
            source = tree / (name + ".java")
            digest = sha(source)
            if digest != bindings["outline_source_sha256"][name + ".java"]:
                raise ValueError(f"Native source binding changed for {version}/{name}")
            sources[name + ".java"] = digest
        block_document = json.loads((args.data / str(protocol) / "blocks.json").read_text())
        expanded = expand_metadata(json.loads((directory / "outline-native-client.json").read_text()), json.loads((directory / "state-native-aliases.json").read_text()), block_document)
        door_document = json.loads((directory / "door-outline-native.json").read_text())
        ray_count = verify(door_document["witness_rows"], door_document["oracle"])
        count = (max(block["block_id"] for block in block_document["blocks"]) + 1) * 16
        table = fold_legacy(expanded, door_document, count)
        proof = {"version": version, "client_sha1": metadata["downloads"]["client"]["sha1"], "server_sha1": metadata["downloads"]["server"]["sha1"],
                 "measurement_sha256": {name: sha(directory / name) for name in ("outline-native-client.json", "state-native-aliases.json", "door-outline-native.json")},
                 "source_sha256": sources, "native_door_contexts": doors["native_contexts"], "native_door_ray_witnesses": ray_count}
        grouped.setdefault(protocol, []).append((version, table, proof))
    for protocol, releases in grouped.items():
        # Descriptor.Build already selects the final catalog release for a shared protocol. Preserve older client-profile differences as evidence.
        representative, table, _ = releases[-1]
        differences = [version for version, other, _ in releases if other != table]
        document = {"_provenance": {"kind": "extracted", "tool": "tools/extraction/extract_legacy_targeting.py", "tool_sha256": sha(Path(__file__)),
                    "client_profile": representative, "measurements": [proof for _, _, proof in releases], "different_earlier_client_profiles": differences,
                    "note": "Selection boxes use the canonical vanilla client profile selected by the generated descriptor. Static geometry and native metadata aliases are measured for every release. Door neighbor rules are exhaustive over decoded states; other neighbor and block-entity variants remain pending."}, **table}
        output = (args.out or args.data) / str(protocol) / "block-targeting.json"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(document, indent=2, allow_nan=False) + "\n")
        print(f"{protocol}: client {representative}, {table['state_count']} states, {len(table['shape_pool'])} shapes, {len(table['context_nodes'])} neighbor nodes; earlier profile differences: {differences}")


if __name__ == "__main__":
    main()
