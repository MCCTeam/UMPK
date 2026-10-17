"""Extract the source-audited 1.10.2 item entity's nonblocking placement rule."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import zipfile


def item_rules(raw: dict, entities: set[str]) -> dict[str, int]:
    if (raw.get("protocol"), raw.get("version"), raw.get("entity"), raw.get("class"), raw.get("field_owner"), raw.get("field_name")) != (210, "1.10.2", "minecraft:item", "yk", "rw", "i") or "minecraft:item" not in entities:
        raise ValueError("Legacy item source binding differs from the audited contract")
    rows = raw.get("rows", [])
    if len(rows) != 2 or {row.get("constructor") for row in rows} != {"default", "network_xyz"} or any(row.get("blocks_building") is not False for row in rows):
        raise ValueError("Both original item constructors must report a false building flag")
    origins = raw.get("origins", {})
    if set(origins) != {"yk", "rw", "aid"} or len(set(origins.values())) != 1 or not all(isinstance(origin, str) and origin for origin in origins.values()):
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
    native = root / "native.json"
    audit = root / "building-flag-writers.json"
    if receipt.get("status") != "complete" or sha(native) != receipt["measurement_sha256"] or sha(audit) != receipt["audit_sha256"]:
        raise ValueError("Legacy item measurement or audit receipt differs")
    for name, expected in receipt["sources_sha256"].items():
        if sha(args.official / "1.10.2-client-decompiled" / name) != expected:
            raise ValueError(f"Audited native source changed: {name}")
    preserved = Path(receipt["unsigned_client"])
    if sha(preserved) != receipt["preservation"]["unsigned_jar_sha256"]:
        raise ValueError("Preserved native client changed")
    metadata = json.loads((args.official / "downloads/1.10.2/version.json").read_text(encoding="utf-8"))
    for side in ("client", "server"):
        path = args.official / "downloads/1.10.2" / (side + ".jar")
        if sha(path) != receipt[side + "_sha256"] or hashlib.sha1(path.read_bytes()).hexdigest() != metadata["downloads"][side]["sha1"]:
            raise ValueError("Official artifact changed: " + side)
    with zipfile.ZipFile(args.official / "downloads/1.10.2/client.jar") as original, zipfile.ZipFile(preserved) as measured:
        classes = {name for name in original.namelist() if name.endswith(".class")}
        if classes != {name for name in measured.namelist() if name.endswith(".class")} or len(classes) != receipt["preservation"]["unchanged_class_files"] or any(original.read(name) != measured.read(name) for name in classes):
            raise ValueError("Original client class preservation failed")
    writers = json.loads(audit.read_text(encoding="utf-8"))
    expected_writers = Counter({"aap": 1, "aaq": 1, "sf": 1, "xa": 1, "xx": 2, "yj": 1, "yl": 1})
    if writers["jar_sha256"] != sha(preserved) or writers["classes_scanned"] != len(classes) or Counter(row["class"] for row in writers["root_flag_writers"]) != expected_writers or any(row["fieldref"] != [row["class"], "i", "Z"] for row in writers["root_flag_writers"]):
        raise ValueError("Unclassified native building-flag writer")
    registry = json.loads((args.data / "210/entities.json").read_text(encoding="utf-8"))
    entities = {row["id"] for key in ("mob_entries", "object_entries") for row in registry[key]}
    rules = item_rules(json.loads(native.read_text(encoding="utf-8")), entities)
    document = {"schema": 1, "entities": rules, "fixed_boxes": {}, "_provenance": {"kind": "extracted", "tool": "tools/extraction/extract_legacy_item_obstruction.py", "tool_sha256": sha(Path(__file__)), "version": "1.10.2", "protocol": 210, "client_sha256": receipt["client_sha256"], "server_sha256": receipt["server_sha256"], "measurement_sha256": sha(native), "audit_sha256": sha(audit), "preservation": receipt["preservation"], "building_flag_source_sha256": receipt["sources_sha256"], "note": "Only the original item entity is proven permanently nonblocking. Both constructors leave Entity.i false; all original class files were audited, and its eight writers affect this in unrelated entity classes. Other entries remain unknown (0). No entity or world tick was executed."}}
    output = json.dumps(document, indent=2) + "\n"
    destination = args.data / "210/entity-obstruction.json"
    if args.check:
        if not destination.is_file() or destination.read_text(encoding="utf-8") != output:
            raise ValueError("Canonical legacy item obstruction data differs")
    else:
        destination.write_text(output, encoding="utf-8")
    print(f"210: {len(rules)} entity keys; only minecraft:item promoted to nonblocking")


if __name__ == "__main__":
    main()
