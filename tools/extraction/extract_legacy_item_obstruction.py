"""Extract source-audited legacy item entities' nonblocking placement rules."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import zipfile

AUDITED = {
    210: ("1.10.2", "yk", "rw", "aid", {"aap": 1, "aaq": 1, "sf": 1, "xa": 1, "xx": 2, "yj": 1, "yl": 1}),
    315: ("1.11", "zi", "sm", "ajq", {"abw": 1, "abx": 1, "sv": 1, "xz": 1, "yw": 1, "zh": 1, "zj": 1}),
}


def item_rules(raw: dict, entities: set[str]) -> dict[str, int]:
    binding = AUDITED.get(raw.get("protocol"))
    if binding is None:
        raise ValueError("Legacy item protocol has no audited binding")
    version, item, owner, world, _ = binding
    if (raw.get("version"), raw.get("entity"), raw.get("class"), raw.get("field_owner"), raw.get("field_name")) != (version, "minecraft:item", item, owner, "i") or "minecraft:item" not in entities:
        raise ValueError("Legacy item source binding differs from the audited contract")
    rows = raw.get("rows", [])
    if len(rows) != 2 or {row.get("constructor") for row in rows} != {"default", "network_xyz"} or any(row.get("blocks_building") is not False for row in rows):
        raise ValueError("Both original item constructors must report a false building flag")
    origins = raw.get("origins", {})
    if set(origins) != {item, owner, world} or len(set(origins.values())) != 1 or not all(isinstance(origin, str) and origin for origin in origins.values()):
        raise ValueError("Native item, entity and world origins differ")
    return {name: int(name == "minecraft:item") for name in sorted(entities)}


def sha(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=Path("data/java"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = args.measurements
    receipt = json.loads((root / "result.json").read_text(encoding="utf-8"))
    protocol = receipt.get("protocol", 210)
    if protocol not in AUDITED:
        raise ValueError("Legacy item protocol has no audited binding")
    version, _, _, _, expected_writers = AUDITED[protocol]
    native = root / "native.json"
    audit = root / "building-flag-writers.json"
    if receipt.get("status") != "complete" or sha(native) != receipt["measurement_sha256"] or sha(audit) != receipt["audit_sha256"]:
        raise ValueError("Legacy item measurement or audit receipt differs")
    for name, expected in receipt["sources_sha256"].items():
        if sha(args.official / f"{version}-client-decompiled" / name) != expected:
            raise ValueError(f"Audited native source changed: {name}")
    preserved = Path(receipt["unsigned_client"])
    if sha(preserved) != receipt["preservation"]["unsigned_jar_sha256"]:
        raise ValueError("Preserved native client changed")
    downloads = args.official / "downloads" / version
    metadata = json.loads((downloads / "version.json").read_text(encoding="utf-8"))
    for side in ("client", "server"):
        path = downloads / (side + ".jar")
        if sha(path) != receipt[side + "_sha256"] or hashlib.sha1(path.read_bytes()).hexdigest() != metadata["downloads"][side]["sha1"]:
            raise ValueError("Official artifact changed: " + side)
    with zipfile.ZipFile(downloads / "client.jar") as original, zipfile.ZipFile(preserved) as measured:
        classes = {name for name in original.namelist() if name.endswith(".class")}
        if classes != {name for name in measured.namelist() if name.endswith(".class")} or len(classes) != receipt["preservation"]["unchanged_class_files"] or any(original.read(name) != measured.read(name) for name in classes):
            raise ValueError("Original client class preservation failed")
    writers = json.loads(audit.read_text(encoding="utf-8"))
    if writers["jar_sha256"] != sha(preserved) or writers["classes_scanned"] != len(classes) or Counter(row["class"] for row in writers["root_flag_writers"]) != Counter(expected_writers) or any(row["fieldref"] != [row["class"], "i", "Z"] for row in writers["root_flag_writers"]):
        raise ValueError("Unclassified native building-flag writer")
    registry = json.loads((args.data / str(protocol) / "entities.json").read_text(encoding="utf-8"))
    entities = {row["id"] for key in ("mob_entries", "object_entries") for row in registry[key]}
    rules = item_rules(json.loads(native.read_text(encoding="utf-8")), entities)
    document = {"schema": 1, "entities": rules, "fixed_boxes": {}, "_provenance": {"kind": "extracted", "tool": "tools/extraction/extract_legacy_item_obstruction.py", "tool_sha256": sha(Path(__file__)), "version": version, "protocol": protocol, "client_sha256": receipt["client_sha256"], "server_sha256": receipt["server_sha256"], "measurement_sha256": sha(native), "audit_sha256": sha(audit), "preservation": receipt["preservation"], "building_flag_source_sha256": receipt["sources_sha256"], "note": f"Only the original item entity is proven permanently nonblocking. Both constructors leave Entity.i false; all original class files were audited, and its {sum(expected_writers.values())} writers affect this in unrelated entity classes. Other entries remain unknown (0). No entity or world tick was executed."}}
    output = json.dumps(document, indent=2) + "\n"
    destination = args.data / str(protocol) / "entity-obstruction.json"
    if args.check:
        if not destination.is_file() or destination.read_text(encoding="utf-8") != output:
            raise ValueError("Canonical legacy item obstruction data differs")
    else:
        destination.write_text(output, encoding="utf-8")
    print(f"{protocol}: {len(rules)} entity keys; only minecraft:item promoted to nonblocking")


if __name__ == "__main__":
    main()
