"""Fold native outline measurements into pooled shapes and contiguous state ranges."""

import argparse
import hashlib
import json
import math
from pathlib import Path


def fold_outlines(raw: dict, states: dict[int, str], items: set[str], clip_model: str = "voxel") -> dict:
    if clip_model not in ("voxel", "voxel_forward", "aabb"):
        raise ValueError("Unsupported source ray clip model")
    if set(raw) != {str(state) for state in states}:
        raise ValueError("Outline measurements do not cover the exact native state IDs")
    pool: list[dict] = []
    indices: dict[str, int] = {}

    def intern(shape: dict) -> int:
        if not isinstance(shape, dict) or shape.get("offset") not in ("none", "xz", "xyz"):
            raise ValueError("Unsupported outline offset model")
        boxes = shape.get("boxes")
        if not isinstance(boxes, list):
            raise ValueError("Outline boxes must be a list")
        for box in boxes:
            if not isinstance(box, list) or len(box) != 6 or any(type(value) not in (int, float) or not math.isfinite(value) for value in box):
                raise ValueError("Invalid outline box coordinates")
            if any(box[axis] > box[axis + 3] for axis in range(3)):
                raise ValueError("Inverted outline box")
        limits = [shape.get("max_horizontal_offset"), shape.get("max_vertical_offset")]
        if any(type(value) not in (int, float) or not math.isfinite(value) or value < 0 for value in limits):
            raise ValueError("Invalid outline offset limits")
        normalized = {"boxes": [[float(value) for value in box] for box in boxes], "offset": shape["offset"],
                      "max_horizontal_offset": float(limits[0]) if shape["offset"] != "none" else 0.0,
                      "max_vertical_offset": float(limits[1]) if shape["offset"] == "xyz" else 0.0}
        if "clip_model" in shape:
            if shape["clip_model"] != "aabb_list":
                raise ValueError("Unsupported per-shape clip model")
            normalized["clip_model"] = shape["clip_model"]
        key = json.dumps(normalized, sort_keys=True, allow_nan=False)
        if key not in indices:
            indices[key] = len(pool)
            pool.append(normalized)
        return indices[key]

    refs: list[list] = []
    for state, name in sorted(states.items()):
        measured = raw[str(state)]
        if measured.get("block") != name:
            raise ValueError(f"Outline state {state} names a different block")
        outline, interaction = intern(measured["outline"]), intern(measured["interaction"])
        item = measured.get("held_item", "")
        if item and (item not in items or "held_outline" not in measured):
            raise ValueError(f"Invalid held-item outline selector at state {state}")
        held = intern(measured["held_outline"]) if item else outline
        row = [state, 1, outline, interaction, item, held]
        if refs and refs[-1][0] + refs[-1][1] == state and refs[-1][2:] == row[2:]:
            refs[-1][1] += 1
        else:
            refs.append(row)
    return {"schema": 1, "clip_model": clip_model, "state_count": len(states), "shape_pool": pool, "state_refs": refs}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, type=Path)
    args = parser.parse_args()
    catalog = json.loads((args.data / "versions.json").read_text())["versions"]
    grouped: dict[int, dict] = {}
    provenance: dict[int, list] = {}
    for entry in catalog:
        version, protocol = entry["name"], entry["protocol"]
        if protocol < 393:
            continue
        directory = args.data / str(protocol)
        block_document = json.loads((directory / "blocks.json").read_text())
        if block_document["identity"] != "flat":
            raise ValueError("This extractor requires a flattened native state registry")
        states = {state: block["name"] for block in block_document["blocks"] for state in range(block["min_state"], block["max_state"] + 1)}
        items = {item["name"] for item in json.loads((directory / "items.json").read_text())["entries"]}
        measurement = args.measurements / version / "outline-context.json"
        raw_bytes = measurement.read_bytes()
        measured = fold_outlines(json.loads(raw_bytes), states, items, "voxel_forward" if protocol < 477 else "voxel")
        if protocol in grouped and grouped[protocol] != measured:
            raise ValueError(f"Outline measurements differ between releases sharing protocol {protocol}: {version}")
        grouped[protocol] = measured
        downloads = args.official / "downloads" / version
        metadata = json.loads((downloads / "version.json").read_text())
        artifact = (downloads / "server.jar").read_bytes()
        if hashlib.sha1(artifact).hexdigest() != metadata["downloads"]["server"]["sha1"]:
            raise ValueError(f"Official server hash mismatch for {version}")
        mapping = downloads / "server_mappings.txt"
        if mapping.exists() and hashlib.sha1(mapping.read_bytes()).hexdigest() != metadata["downloads"]["server_mappings"]["sha1"]:
            raise ValueError(f"Official mappings hash mismatch for {version}")
        sources = {}
        for relative in ("net/minecraft/util/Mth.java", "net/minecraft/world/level/BlockGetter.java", "net/minecraft/world/level/block/Block.java", "net/minecraft/world/level/block/state/BlockBehaviour.java", "net/minecraft/world/level/block/ScaffoldingBlock.java", "net/minecraft/world/level/block/LightBlock.java"):
            source = args.official / (version + "-decompiled") / relative
            if source.exists():
                sources[relative] = hashlib.sha256(source.read_bytes()).hexdigest()
        row = {"version": version, "server_sha256": hashlib.sha256(artifact).hexdigest(),
               "measurement_sha256": hashlib.sha256(raw_bytes).hexdigest(), "source_sha256": sources}
        binding_path = args.measurements / version / "outline-symbols.json"
        if not mapping.exists() and protocol < 498:
            bindings = json.loads(binding_path.read_text())
            if bindings["version"] != version:
                raise ValueError("Outline bindings name a different version")
            for name, expected in bindings["sources"].items():
                source = args.official / (version + "-decompiled") / (name + ".java")
                if hashlib.sha256(source.read_bytes()).hexdigest() != expected:
                    raise ValueError(f"Outline source binding changed: {name}")
            row["source_bindings"] = bindings
            row["adapter_tool"] = "tools/extraction/shape-extractor/LegacyFlatOutlineDump.java"
            row["adapter_tool_sha256"] = hashlib.sha256((Path(__file__).parent / "shape-extractor/LegacyFlatOutlineDump.java").read_bytes()).hexdigest()
        provenance.setdefault(protocol, []).append(row)
    for protocol, measured in grouped.items():
        document = {"_provenance": {"kind": "extracted", "tool": "tools/extraction/shape-extractor/OutlineDump.java",
            "tool_sha256": hashlib.sha256((Path(__file__).parent / "shape-extractor/OutlineDump.java").read_bytes()).hexdigest(),
            "measurements": provenance[protocol], "positions": [[0, 0, 0], [17, 63, -31], [-30000000, -64, 29999999], [0, 100, 0]],
            "note": "Native getShape and getInteractionShape with EmptyBlockGetter. Empty and queried-item-holding contexts are measured at every position. Coordinate dependence must be exactly a native offset translation; otherwise extraction fails. Legacy neighbor geometry is outside this flattened table."}, **measured}
        path = args.data / str(protocol) / "block-targeting.json"
        # Keep each reference range on one line so a state-to-shape change remains reviewable.
        ranges = document.pop("state_refs")
        prefix = json.dumps(document, indent=2, allow_nan=False)
        rows = ",\n".join("    " + json.dumps(row, separators=(",", ":"), allow_nan=False) for row in ranges)
        path.write_text(prefix[:-2] + ",\n  \"state_refs\": [\n" + rows + "\n  ]\n}\n")
        print(f"{protocol}: {len(provenance[protocol])} releases, {measured['state_count']} states, {len(measured['shape_pool'])} shapes, {len(measured['state_refs'])} ranges")


if __name__ == "__main__":
    main()
