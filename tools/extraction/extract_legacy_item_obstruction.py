"""Extract source-audited legacy item entities' nonblocking placement rules."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import zipfile

AUDITED = {
    210: (("1.10.2",), "yk", "rw", "aid", "i", {"aap": 1, "aaq": 1, "sf": 1, "xa": 1, "xx": 2, "yj": 1, "yl": 1}),
    315: (("1.11",), "zi", "sm", "ajq", "i", {"abw": 1, "abx": 1, "sv": 1, "xz": 1, "yw": 1, "zh": 1, "zj": 1}),
    316: (("1.11.1", "1.11.2"), "zj", "sn", "ajs", "i", {"abx": 1, "aby": 1, "sw": 1, "ya": 1, "yx": 1, "zi": 1, "zk": 1}),
    335: (("1.12",), "acj", "ve", "ams", "i", {"aba": 1, "abx": 1, "aci": 1, "ack": 1, "afb": 1, "afc": 1, "vn": 1}),
    338: (("1.12.1",), "acl", "vg", "amu", "i", {"abc": 1, "abz": 1, "ack": 1, "acm": 1, "afd": 1, "afe": 1, "vp": 1}),
    340: (("1.12.2",), "acl", "vg", "amu", "i", {"abc": 1, "abz": 1, "ack": 1, "acm": 1, "afd": 1, "afe": 1, "vp": 1}),
    393: (("1.13",), "ami", "aeo", "axs", "j", {"aex": 1, "aky": 1, "alv": 1, "amh": 1, "amj": 1, "apd": 1, "apf": 1}),
    401: (("1.13.1",), "aml", "aer", "axx", "j", {"afa": 1, "alb": 1, "aly": 1, "amk": 1, "amm": 1, "apg": 1, "api": 1}),
    404: (("1.13.2",), "amm", "aer", "axy", "j", {"afa": 1, "alc": 1, "alz": 1, "aml": 1, "amn": 1, "aph": 1, "apj": 1}),
}


def item_rules(raw: dict, entities: set[str]) -> dict[str, int]:
    binding = AUDITED.get(raw.get("protocol"))
    if type(raw.get("protocol")) is not int or binding is None:
        raise ValueError("Legacy item protocol has no audited binding")
    versions, item, owner, world, field, _ = binding
    if raw.get("version") not in versions or (raw.get("entity"), raw.get("class"), raw.get("field_owner"), raw.get("field_name")) != ("minecraft:item", item, owner, field) or "minecraft:item" not in entities:
        raise ValueError("Legacy item source binding differs from the audited contract")
    rows = raw.get("rows", [])
    if len(rows) != 2 or {row.get("constructor") for row in rows} != {"default", "network_xyz"} or any(row.get("blocks_building") is not False for row in rows):
        raise ValueError("Both original item constructors must report a false building flag")
    origins = raw.get("origins", {})
    if set(origins) != {item, owner, world} or len(set(origins.values())) != 1 or not all(isinstance(origin, str) and origin for origin in origins.values()):
        raise ValueError("Native item, entity and world origins differ")
    return {name: int(name == "minecraft:item") for name in sorted(entities)}


def item_alias_rules(measurements: list[dict], entities: set[str]) -> dict[str, int]:
    if not measurements or len({row["version"] for row in measurements}) != len(measurements) or len({row["protocol"] for row in measurements}) != 1:
        raise ValueError("Item aliases must name distinct releases of one protocol")
    rules = [item_rules(row, entities) for row in measurements]
    if any(row != rules[0] for row in rules):
        raise ValueError("Item obstruction differs between protocol aliases")
    return rules[0]


def sha(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_measurement(root: Path, official: Path, data: Path) -> dict:
    receipt = json.loads((root / "result.json").read_text(encoding="utf-8"))
    protocol = receipt.get("protocol", 210)
    if type(protocol) is not int or protocol not in AUDITED:
        raise ValueError("Legacy item protocol has no audited binding")
    versions, _, _, _, field, expected_writers = AUDITED[protocol]
    version = receipt.get("version", versions[-1])
    if version not in versions:
        raise ValueError("Legacy item release has no audited binding")
    native = root / "native.json"
    audit = root / "building-flag-writers.json"
    if receipt.get("status") != "complete" or sha(native) != receipt["measurement_sha256"] or sha(audit) != receipt["audit_sha256"]:
        raise ValueError("Legacy item measurement or audit receipt differs")
    for name, expected in receipt["sources_sha256"].items():
        if sha(official / f"{version}-client-decompiled" / name) != expected:
            raise ValueError(f"Audited native source changed: {name}")
    preserved = Path(receipt["unsigned_client"])
    if sha(preserved) != receipt["preservation"]["unsigned_jar_sha256"]:
        raise ValueError("Preserved native client changed")
    downloads = official / "downloads" / version
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
    if writers["jar_sha256"] != sha(preserved) or writers["classes_scanned"] != len(classes) or Counter(row["class"] for row in writers["root_flag_writers"]) != Counter(expected_writers) or any(row["fieldref"] != [row["class"], field, "Z"] for row in writers["root_flag_writers"]):
        raise ValueError("Unclassified native building-flag writer")
    registry = json.loads((data / str(protocol) / "entities.json").read_text(encoding="utf-8"))
    entities = {row["id"] for key in ("entries", "mob_entries", "object_entries") for row in registry.get(key, [])}
    raw = json.loads(native.read_text(encoding="utf-8"))
    if raw["version"] != version or raw["protocol"] != protocol or any(Path(origin).resolve() != preserved.resolve() for origin in raw["origins"].values()):
        raise ValueError("Native item identity or class origin differs from its receipt")
    rules = item_rules(raw, entities)
    return {"receipt": receipt, "version": version, "protocol": protocol, "raw": raw, "rules": rules, "field": field, "writers": sum(expected_writers.values())}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--alias", type=Path, action="append", default=[])
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=Path("data/java"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    records = [read_measurement(root, args.official, args.data) for root in [args.measurements, *args.alias]]
    primary = records[0]
    rules = item_alias_rules([row["raw"] for row in records], set(primary["rules"]))
    receipt, version, protocol, field = (primary[key] for key in ("receipt", "version", "protocol", "field"))
    provenance = {"kind": "extracted", "tool": "tools/extraction/extract_legacy_item_obstruction.py", "tool_sha256": sha(Path(__file__)), "version": version, "protocol": protocol, "client_sha256": receipt["client_sha256"], "server_sha256": receipt["server_sha256"], "measurement_sha256": receipt["measurement_sha256"], "audit_sha256": receipt["audit_sha256"], "preservation": receipt["preservation"], "building_flag_source_sha256": receipt["sources_sha256"], "note": f"Only the original item entity is proven permanently nonblocking. Both constructors leave Entity.{field} false; all original class files were audited, and its {primary['writers']} writes belong to unrelated entity classes. Other entries remain unknown (0). No entity or world tick was executed."}
    if len(records) > 1:
        provenance["aliases"] = [{"version": row["version"], "client_sha256": row["receipt"]["client_sha256"], "server_sha256": row["receipt"]["server_sha256"], "measurement_sha256": row["receipt"]["measurement_sha256"], "audit_sha256": row["receipt"]["audit_sha256"]} for row in sorted(records, key=lambda row: row["version"])]
    document = {"schema": 1, "entities": rules, "fixed_boxes": {}, "_provenance": provenance}
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
