#!/usr/bin/env python3
"""Modern extractor (protocols with server data reports, 1.13+).

Consumes a version's `--reports` output plus its decompiled tree and emits the
canonical dataset files for one protocol directory under data/java/<protocol>/.
Every emitted file carries a `_provenance` block naming the
source and mapping path so the dataset is auditable.

One driver emits one output shape and one provenance record for the modern dataset.

Note: codec keys (era member names) are assigned to packets. Codec-key
assignment is a review judgement, so this extractor emits a
candidate codec key (the era anchor passed on the command line) for every
packet; the human/dataset author retargets individual keys where a wire format
actually changed. The initially committed datasets were authored from these
candidates and then hand-checked against the decompiled StreamCodecs.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import decompile
import shape_extractor
import umpk_extract as ux


def _provenance(version: str, protocol: int, sources: list[str]) -> dict:
    return {
        "version": version,
        "protocol": protocol,
        "mapping_source": decompile.mapping_path_for(version),
        "sources": sources,
        "kind": "extracted",
    }


def extract_packets(reports: Path, version: str, protocol: int, codec_key: str) -> dict:
    raw = json.loads((reports / "packets.json").read_text(encoding="utf-8"))
    phases: dict[str, dict] = {}
    for phase, flows in raw.items():
        phases[phase] = {}
        for flow, packets in flows.items():
            ordered = sorted(packets.items(), key=lambda kv: kv[1]["protocol_id"])
            phases[phase][flow] = [
                {"id": name, "protocol_id": meta["protocol_id"], "codec": codec_key}
                for name, meta in ordered
            ]
    return {
        "_provenance": _provenance(version, protocol, ["reports/packets.json"]),
        "phases": phases,
    }


def extract_registry_list(reports: Path, name: str) -> list[dict]:
    reg = json.loads((reports / "registries.json").read_text(encoding="utf-8"))
    return ux.sorted_registry(reg[name]["entries"])


def extract_items(reports: Path, version: str, protocol: int) -> dict:
    entries = extract_registry_list(reports, "minecraft:item")
    return {
        "_provenance": _provenance(version, protocol, ["reports/registries.json#minecraft:item"]),
        "identity": "flat",
        "entries": entries,
    }


def extract_components(reports: Path, version: str, protocol: int) -> dict:
    # Data components arrived at 1.20.5 (protocol 766). Pre-766 versions have no minecraft:data_component_type registry; emit an empty component table so the dataset shape is uniform and DataGen sees "no components" explicitly.
    reg = json.loads((reports / "registries.json").read_text(encoding="utf-8"))
    entries = ux.sorted_registry(reg["minecraft:data_component_type"]["entries"]) \
        if "minecraft:data_component_type" in reg else []
    # Emit the era codec key alongside each component id; the committed datasets bind these to real codec members. Extraction leaves it null for review.
    return {
        "_provenance": _provenance(
            version, protocol, ["reports/registries.json#minecraft:data_component_type"]
        ),
        "entries": [{"id": e["name"], "id_num": e["id"], "codec": None} for e in entries],
    }


def extract_menus(reports: Path, version: str, protocol: int) -> dict:
    entries = extract_registry_list(reports, "minecraft:menu")
    return {
        "_provenance": _provenance(version, protocol, ["reports/registries.json#minecraft:menu"]),
        "note": "slot layouts/roles are curated; wire ids are extracted",
        "entries": [{"id": e["name"], "id_num": e["id"], "slots": None} for e in entries],
    }


def extract_argument_types(reports: Path, version: str, protocol: int) -> dict:
    # The minecraft:command_argument_type registry is only a synced registry in the reports from 1.19 (protocol 759) onward. For 1.16-1.18 the brigadier argument types live in a fixed, code-registered table (decompiled ArgumentTypes.java register() order); the dataset carries an empty list here and the codec's argument-type table supplies the id order for those protocols, mirroring how components.json is empty before 1.20.5.
    reg = json.loads((reports / "registries.json").read_text(encoding="utf-8"))
    if "minecraft:command_argument_type" not in reg:
        return {
            "_provenance": _provenance(
                version, protocol,
                ["fixed brigadier registry (no minecraft:command_argument_type report before 1.19)"],
            ),
            "entries": [],
        }
    entries = ux.sorted_registry(reg["minecraft:command_argument_type"]["entries"])
    return {
        "_provenance": _provenance(
            version, protocol, ["reports/registries.json#minecraft:command_argument_type"]
        ),
        "entries": entries,
    }


# Registries UMPK models per-version (static + default-synced). Kept explicit so review sees exactly which registries are captured.
STATIC_REGISTRIES = [
    "minecraft:mob_effect",
    "minecraft:sound_event",
    "minecraft:particle_type",
    "minecraft:block_entity_type",
    "minecraft:villager_profession",
    "minecraft:villager_type",
    "minecraft:attribute",
    "minecraft:entity_type",
    "minecraft:block",
    "minecraft:fluid",
    "minecraft:potion",
    "minecraft:enchantment",
]


def extract_registries(reports: Path, version: str, protocol: int) -> dict:
    reg = json.loads((reports / "registries.json").read_text(encoding="utf-8"))
    out: dict[str, list[dict]] = {}
    for name in STATIC_REGISTRIES:
        if name in reg:
            out[name] = ux.sorted_registry(reg[name]["entries"])
    return {
        "_provenance": _provenance(version, protocol, ["reports/registries.json"]),
        "registries": out,
    }


def extract_blocks(reports: Path, version: str, protocol: int) -> dict:
    raw = json.loads((reports / "blocks.json").read_text(encoding="utf-8"))
    reg = json.loads((reports / "registries.json").read_text(encoding="utf-8"))
    block_ids = {name: meta["protocol_id"] for name, meta in reg["minecraft:block"]["entries"].items()}
    blocks = []
    for name in sorted(block_ids, key=lambda n: block_ids[n]):
        info = raw[name]
        states = info["states"]
        default_id = next((s["id"] for s in states if s.get("default")), states[0]["id"])
        props = None
        # Property schema is the union across states, taken from the first state that declares properties (all states of a block share the schema).
        for st in states:
            if "properties" in st:
                props = sorted(st["properties"].keys())
                break
        blocks.append(
            {
                "name": name,
                "block_id": block_ids[name],
                "default_state": default_id,
                "min_state": min(s["id"] for s in states),
                "max_state": max(s["id"] for s in states),
                "num_states": len(states),
                "properties": props,
            }
        )
    return {
        "_provenance": _provenance(
            version, protocol, ["reports/blocks.json", "reports/registries.json#minecraft:block"]
        ),
        "identity": "flat",
        "blocks": blocks,
    }


def extract_entities(reports: Path, version: str, protocol: int) -> dict:
    entries = extract_registry_list(reports, "minecraft:entity_type")
    # Dimensions (width/height) are not in reports; UMPK curates them. Bootstrap leaves them null so the author fills the physics-relevant ones.
    return {
        "_provenance": _provenance(
            version, protocol, ["reports/registries.json#minecraft:entity_type"]
        ),
        "flat_id_space": True,
        "entries": [{"id": e["name"], "id_num": e["id"], "width": None, "height": None} for e in entries],
    }


# Java field name -> UMPK metadata serializer codec key.
SERIALIZER_CODEC = {
    "BYTE": "Byte",
    "INT": "VarInt",
    "LONG": "VarLong",
    "FLOAT": "Float",
    "STRING": "String",
    "COMPONENT": "Component",
    "OPTIONAL_COMPONENT": "OptionalComponent",
    "ITEM_STACK": "ItemStack",
    "BLOCK_STATE": "BlockState",
    "OPTIONAL_BLOCK_STATE": "OptionalBlockState",
    "BOOLEAN": "Boolean",
    "PARTICLE": "Particle",
    "PARTICLES": "Particles",
    "ROTATIONS": "Rotations",
    "BLOCK_POS": "BlockPos",
    "OPTIONAL_BLOCK_POS": "OptionalBlockPos",
    "DIRECTION": "Direction",
    "OPTIONAL_LIVING_ENTITY_REFERENCE": "OptionalLivingEntityReference",
    "OPTIONAL_UUID": "OptionalUuid",
    "OPTIONAL_GLOBAL_POS": "OptionalGlobalPos",
    "NBT_TAG": "CompoundTag",
    "COMPOUND_TAG": "CompoundTag",
    "VILLAGER_DATA": "VillagerData",
    "OPTIONAL_UNSIGNED_INT": "OptionalUnsignedInt",
    "POSE": "Pose",
    "CAT_VARIANT": "CatVariant",
    "CAT_SOUND_VARIANT": "CatSoundVariant",
    "CHICKEN_VARIANT": "ChickenVariant",
    "CHICKEN_SOUND_VARIANT": "ChickenSoundVariant",
    "COW_VARIANT": "CowVariant",
    "COW_SOUND_VARIANT": "CowSoundVariant",
    "WOLF_VARIANT": "WolfVariant",
    "WOLF_SOUND_VARIANT": "WolfSoundVariant",
    "FROG_VARIANT": "FrogVariant",
    "PIG_VARIANT": "PigVariant",
    "PIG_SOUND_VARIANT": "PigSoundVariant",
    "ZOMBIE_NAUTILUS_VARIANT": "ZombieNautilusVariant",
    "PAINTING_VARIANT": "PaintingVariant",
    "ARMADILLO_STATE": "ArmadilloState",
    "SNIFFER_STATE": "SnifferState",
    "WEATHERING_COPPER_STATE": "WeatheringCopperState",
    "COPPER_GOLEM_STATE": "CopperGolemState",
    "VECTOR3": "Vector3",
    "QUATERNION": "Quaternion",
    "RESOLVABLE_PROFILE": "ResolvableProfile",
    "HUMANOID_ARM": "HumanoidArm",
    "SNIFFER_DIGGING_STATE": "SnifferState",
}


def extract_metadata(version: str, protocol: int) -> dict:
    tree = ux.decompiled_tree(version)
    if tree is None:
        ux.die(f"no decompiled tree for {version}; cannot extract metadata order")
    src = ux.find_java_file(
        tree, "net/minecraft/network/syncher/EntityDataSerializers.java"
    )
    if src is None:
        ux.die("EntityDataSerializers.java not found in decompiled tree")
    fields = ux.extract_serializer_order(src)
    entries = []
    for idx, field in enumerate(fields):
        codec = SERIALIZER_CODEC.get(field)
        if codec is None:
            print(f"warning: no codec mapping for serializer field {field!r}")
            codec = field
        entries.append({"id": idx, "field": field, "codec": codec})
    return {
        "_provenance": {
            "version": version,
            "protocol": protocol,
            "mapping_source": decompile.mapping_path_for(version),
            "sources": ["net/minecraft/network/syncher/EntityDataSerializers.java"],
            "kind": "extracted",
        },
        "terminator": "0xff",
        "serializers": entries,
    }


def run(
    version: str,
    protocol: int,
    reports: Path,
    out_dir: Path,
    codec_key: str,
) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    ux.write_json(out_dir / "packets.json", extract_packets(reports, version, protocol, codec_key))
    ux.write_json(out_dir / "items.json", extract_items(reports, version, protocol))
    ux.write_json(out_dir / "components.json", extract_components(reports, version, protocol))
    ux.write_json(out_dir / "menus.json", extract_menus(reports, version, protocol))
    ux.write_json(out_dir / "argument_types.json", extract_argument_types(reports, version, protocol))
    ux.write_json(out_dir / "registries.json", extract_registries(reports, version, protocol))
    ux.write_json(out_dir / "blocks.json", extract_blocks(reports, version, protocol))
    ux.write_json(out_dir / "entities.json", extract_entities(reports, version, protocol))
    ux.write_json(out_dir / "metadata.json", extract_metadata(version, protocol))
    shapes, block_shape_refs = shape_extractor.extract_modern(reports, version, protocol)
    ux.write_json(out_dir / "shapes.json", shapes)
    # Attach shape refs into blocks.json is avoided (blocks.json is report-pure);
    # shapes.json carries the per-block-state ref table instead.
    ux.write_json(out_dir / "block-shape-refs.json", block_shape_refs)


def main() -> int:
    parser = argparse.ArgumentParser(description="UMPK modern extractor")
    parser.add_argument("version")
    parser.add_argument("protocol", type=int)
    parser.add_argument("--reports", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--codec-key", required=True, help="candidate era anchor, e.g. V1_21_5")
    args = parser.parse_args()
    run(args.version, args.protocol, Path(args.reports), Path(args.out), args.codec_key)
    print(f"extracted {args.version} (protocol {args.protocol}) -> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
