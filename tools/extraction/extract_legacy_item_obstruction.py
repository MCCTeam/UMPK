"""Extract source-audited legacy item and XP nonblocking placement rules."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import zipfile

CLASSIC_AUDITED = (
    (("1.8",), "adw", "wv", "aqu", "k", {"ada": 1, "adu": 1, "adv": 1, "adx": 1, "aek": 1, "xm": 1}),
    (("1.8.1",), "adx", "wx", "aqr", "k", {"adb": 1, "adv": 1, "adw": 1, "ady": 1, "ael": 1, "xo": 1}),
    (("1.8.2", "1.8.3"), "va", "pl", "adn", "k", {"ps": 1, "ug": 1, "uy": 1, "uz": 1, "vb": 1, "vk": 1}),
    (("1.8.4", "1.8.5", "1.8.6", "1.8.7", "1.8.8", "1.8.9"), "uz", "pk", "adm", "k", {"pr": 1, "uf": 1, "ux": 1, "uy": 1, "va": 1, "vj": 1}),
)

AUDITED = {
    107: (("1.9",), "yd", "rr", "aht", "i", {"aag": 1, "aah": 1, "sa": 1, "wt": 1, "xq": 2, "yc": 1, "ye": 1}),
    108: (("1.9.1",), "yd", "rr", "aht", "i", {"aag": 1, "aah": 1, "sa": 1, "wt": 1, "xq": 2, "yc": 1, "ye": 1}),
    109: (("1.9.2",), "yd", "rr", "aht", "i", {"aag": 1, "aah": 1, "sa": 1, "wt": 1, "xq": 2, "yc": 1, "ye": 1}),
    110: (("1.9.3", "1.9.4"), "yd", "rr", "aht", "i", {"aag": 1, "aah": 1, "sa": 1, "wt": 1, "xq": 2, "yc": 1, "ye": 1}),
    210: (("1.10", "1.10.1", "1.10.2"), "yk", "rw", "aid", "i", {"aap": 1, "aaq": 1, "sf": 1, "xa": 1, "xx": 2, "yj": 1, "yl": 1}),
    315: (("1.11",), "zi", "sm", "ajq", "i", {"abw": 1, "abx": 1, "sv": 1, "xz": 1, "yw": 1, "zh": 1, "zj": 1}),
    316: (("1.11.1", "1.11.2"), "zj", "sn", "ajs", "i", {"abx": 1, "aby": 1, "sw": 1, "ya": 1, "yx": 1, "zi": 1, "zk": 1}),
    335: (("1.12",), "acj", "ve", "ams", "i", {"aba": 1, "abx": 1, "aci": 1, "ack": 1, "afb": 1, "afc": 1, "vn": 1}),
    338: (("1.12.1",), "acl", "vg", "amu", "i", {"abc": 1, "abz": 1, "ack": 1, "acm": 1, "afd": 1, "afe": 1, "vp": 1}),
    340: (("1.12.2",), "acl", "vg", "amu", "i", {"abc": 1, "abz": 1, "ack": 1, "acm": 1, "afd": 1, "afe": 1, "vp": 1}),
    393: (("1.13",), "ami", "aeo", "axs", "j", {"aex": 1, "aky": 1, "alv": 1, "amh": 1, "amj": 1, "apd": 1, "apf": 1}),
    401: (("1.13.1",), "aml", "aer", "axx", "j", {"afa": 1, "alb": 1, "aly": 1, "amk": 1, "amm": 1, "apg": 1, "api": 1}),
    404: (("1.13.2",), "amm", "aer", "axy", "j", {"afa": 1, "alc": 1, "alz": 1, "aml": 1, "amn": 1, "aph": 1, "apj": 1}),
    477: (("1.14",), "ato", "aif", "bhh", "i", {"aio": 1, "asf": 1, "atc": 1, "atn": 1, "atp": 1, "axl": 1, "axn": 1}),
    480: (("1.14.1",), "atp", "aii", "bhi", "i", {"air": 1, "asg": 1, "atd": 1, "ato": 1, "atq": 1, "axm": 1, "axo": 1}),
    485: (("1.14.2",), "atp", "aii", "bhi", "i", {"air": 1, "asg": 1, "atd": 1, "ato": 1, "atq": 1, "axm": 1, "axo": 1}),
    490: (("1.14.3",), "ats", "ail", "bhm", "i", {"aiu": 1, "asj": 1, "atg": 1, "atr": 1, "att": 1, "axp": 1, "axr": 1}),
}


def audited_binding(protocol: int, version: str | None) -> tuple:
    if type(protocol) is not int:
        raise ValueError("Legacy item protocol has no audited binding")
    if protocol == 47:
        for binding in CLASSIC_AUDITED:
            if version in binding[0]:
                return binding
    elif protocol in AUDITED:
        binding = AUDITED[protocol]
        if version is None or version in binding[0]:
            return binding
    raise ValueError("Legacy item release has no audited binding")


def registry_identities(registry: dict) -> dict[str, str]:
    identities = {}
    for key in ("entries", "mob_entries", "object_entries"):
        for row in registry.get(key, []):
            name = row["id"]
            canonical = name if ":" in name else "minecraft:" + name
            if canonical in identities and identities[canonical] != name:
                raise ValueError("Legacy registry aliases collide after qualification")
            identities[canonical] = name
    return identities


# Reviewed protocol-315 source binding; the report is retained separately from the compact fixture.
SOURCE_NONBLOCKING_PROOF_SHA256 = "dd426ee8101b9028efff5a75ddd56acf5b4cfdaf6da8a36248f30a93c87c8fcd"
SOURCE_NONBLOCKING_FAMILY_BUNDLE_SHA256 = "a07dd9fa81ffb549fee22f43ecf05b0ba59fa43a1912cc10efc3d412b89af7ce"
SOURCE_NONBLOCKING_FAMILY_PROOF_SHA256S = frozenset(
    [
        "279c650dcd1f649132f07417a8bbce913c2b2ae5b67e1c824e0e3a29358b74cc",
        "52a0b040440320cfb50a28b627767da92ac242cecaec0975036d44f620097e69",
        "0cff1030b2cc9be170aaaf515b217bca707a93131f833e0c15873f3519d49128",
        "28fe0b0ae1f0c44debb30e7d547f171e7d6c2adea820a4fd8cf635aa11777d75",
        "5cba9f71b6da9eaa1bcf9d569c86f9b071f299f49f9286958ca7156639e915c5",
        "e838877297c4a79ef594d606e6334b27914ffead2b7ca61f9ab15935d6b08c4e",
        "780628503a68fe22354a467d02cb261cc29dc9e3fd6ae9cc73c2cd166bcf506b",
        "2c3794d1d79fcac5900fbec5939b725612f092cbdba68da80eb6cfb4f4aec5be",
        "7b0acf0e7dc8f7ff721648ff42889e28afc04eddf575cce9fdc8853f5f58d438",
        "c53e9e5a8168aa94345681341b55f11e5c392be2ff2723d1e24603f59b1e0146",
        "d254ea63b60192fb8e12cf2846b5af6afbf112d06754b65863a3fbe2ad9ea8be",
        "80e437be66a9783e0bb342c3c2092fdeba355436a1942f77a811c929d0f22bb4",
        "38f387b62ec821ec264809a613f89d614a9fc1150553b3b42c27c8c245e44300",
        "1921433470c779aeb2ff777e202ce2f0cc5c103066e234d2371e36fff9b2e6b3",
        "501618d63d6aa28b2dd56c00b18da54b86a477360743cff533dedc5458581c98",
        "e5e492be712cd6d56c2bbb9324df137e06624b244af7e69f3a6c1b3722267522",
        "7c3e0f62809286caa1eb8ed1f846834dfda2ab2f612ab83387e496e3212d6901",
        "b6da9b2d75b916de8d97a3729c9ac38946f3080bb66958bd11145e88f6310a56",
        "9a4ab72175019d5596c5c9d8fe419b467c914c31e3404d999b8ad935d8c16cec",
        "bded018becd46e752af1992c7b6583699a1c19042b938d45b5dd7e24d4cb78e7",
        "c72e36ab3eca52fbc4ee2945502812ab40cfe6e96cae7eba7df745ed6a71aca0",
        "dbafb46899fb2e15fc56a9d83138f404750889082fc8ee3c2cfe40acf5cfe373",
        "69a519f8882fd9c52445bb150c58acce706f8dcf86f2a1f22d9f071ad21f5ead",
        "1c6aa2cd9145e6578ff81017d7e68b2332f070a69af5054b46182d460682ab25",
        "e7063bf9222d56cc74cfb011dad393d8f00e59c8953018f9dbb4e2095a75ee80",
        "78ac4f9b0925b73510a568c83e7b728a039672adee961e02ff45c819472c4028",
        "060ccb7aeb7b2e5898e340743eadbd540c40924cffadf5a8d997e8a26a042b7b",
        "cbc625075ce0df9b0078a40ca52fe16bfd604b32bced1eb94f86ceeda8f081d3",
        "126b32144c79ed148be7ec7aa7100322f0a83905d8524e93f9027e2cd3614bbc",
        "318d938ce3ab5c951b0f9d9d0abf5e309bf35001c34b07bca6f952b5e66fd551",
    ]
)


def source_nonblocking_type(raw: dict, entities: set[str]) -> str | None:
    proof = raw.get("source_nonblocking_proof")
    if proof is None:
        return None
    encoded = json.dumps(proof, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")
    if hashlib.sha256(encoded).hexdigest() not in SOURCE_NONBLOCKING_FAMILY_PROOF_SHA256S | {SOURCE_NONBLOCKING_PROOF_SHA256}:
        raise ValueError("Legacy source nonblocking proof differs from the reviewed binding")
    if (raw.get("protocol"), raw.get("version"), raw.get("field_owner"), raw.get("field_name")) != (proof["protocol"], proof["version"], proof["field_owner"], proof["field_name"]) or proof["entity"] not in entities:
        raise ValueError("Legacy source nonblocking proof belongs to another era or missing identity")
    return proof["entity"]


def select_source_nonblocking_proof(document: dict, protocol: int, version: str) -> dict:
    if document.get("schema") == 2:
        encoded = json.dumps(document, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")
        if hashlib.sha256(encoded).hexdigest() != SOURCE_NONBLOCKING_FAMILY_BUNDLE_SHA256:
            raise ValueError("Legacy source proof bundle differs from the reviewed native releases")
        matching = [row for row in document["bindings"] if (row["protocol"], row["version"]) == (protocol, version)]
        if len(matching) != 1:
            raise ValueError("Legacy source proof bundle has no exact protocol and release binding")
        document = matching[0]
    if (document.get("protocol"), document.get("version")) != (protocol, version):
        raise ValueError("Legacy source proof belongs to another protocol or named release")
    return document


def item_rules(raw: dict, entities: set[str]) -> dict[str, int]:
    binding = audited_binding(raw.get("protocol"), raw.get("version"))
    versions, item, owner, world, field, _ = binding
    if raw.get("version") not in versions or (raw.get("entity"), raw.get("class"), raw.get("field_owner"), raw.get("field_name")) != ("minecraft:item", item, owner, field) or "minecraft:item" not in entities:
        raise ValueError("Legacy item source binding differs from the audited contract")
    rows = raw.get("rows", [])
    if len(rows) != 2 or {row.get("constructor") for row in rows} != {"default", "network_xyz"} or any(row.get("blocks_building") is not False for row in rows):
        raise ValueError("Both original item constructors must report a false building flag")
    origins = raw.get("origins", {})
    if set(origins) != {item, owner, world} or len(set(origins.values())) != 1 or not all(isinstance(origin, str) and origin for origin in origins.values()):
        raise ValueError("Native item, entity and world origins differ")
    source_type = source_nonblocking_type(raw, entities)
    return {name: int(name == "minecraft:item" or name == source_type) for name in sorted(entities)}


def item_alias_rules(measurements: list[dict], entities: set[str]) -> dict[str, int]:
    if not measurements or len({row["version"] for row in measurements}) != len(measurements) or len({row["protocol"] for row in measurements}) != 1:
        raise ValueError("Item aliases must name distinct releases of one protocol")
    if measurements[0]["protocol"] == 47 and {row["version"] for row in measurements} != {version for binding in CLASSIC_AUDITED for version in binding[0]}:
        raise ValueError("Classic item evidence must cover all ten protocol aliases")
    rules = [item_rules(row, entities) for row in measurements]
    if any(row != rules[0] for row in rules):
        raise ValueError("Item obstruction differs between protocol aliases")
    return rules[0]


def sha(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_measurement(root: Path, official: Path, data: Path, source_proof: Path | None = None) -> dict:
    receipt = json.loads((root / "result.json").read_text(encoding="utf-8"))
    protocol = receipt.get("protocol", 210)
    versions, _, _, _, field, expected_writers = audited_binding(protocol, receipt.get("version"))
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
    identities = registry_identities(registry)
    raw = json.loads(native.read_text(encoding="utf-8"))
    if raw["version"] != version or raw["protocol"] != protocol or any(Path(origin).resolve() != preserved.resolve() for origin in raw["origins"].values()):
        raise ValueError("Native item identity or class origin differs from its receipt")
    if source_proof is not None:
        proof = select_source_nonblocking_proof(json.loads(source_proof.read_text(encoding="utf-8")), protocol, version)
        raw["source_nonblocking_proof"] = proof
        source_nonblocking_type(raw, set(identities))
        if proof["client_sha256"] != receipt["client_sha256"] or proof["writer_audit_sha256"] != receipt["audit_sha256"] or proof["classes_preserved"] != len(classes):
            raise ValueError("Legacy source nonblocking proof uses different original classes or flag audit")
        for name, expected in proof["source_sha256"].items():
            if sha(official / f"{version}-client-decompiled" / name) != expected:
                raise ValueError(f"Legacy source nonblocking proof source changed: {name}")
    rules = item_rules(raw, set(identities))
    return {"receipt": receipt, "version": version, "protocol": protocol, "raw": raw, "rules": rules, "identities": identities, "field": field, "writers": sum(expected_writers.values())}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measurements", type=Path, required=True)
    parser.add_argument("--alias", type=Path, action="append", default=[])
    parser.add_argument("--source-proof", type=Path, help="Optional reviewed source and original-bytecode nonparticipation binding")
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--data", type=Path, default=Path("data/java"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    records = [read_measurement(root, args.official, args.data, args.source_proof) for root in [args.measurements, *args.alias]]
    primary = records[0]
    rules = item_alias_rules([row["raw"] for row in records], set(primary["rules"]))
    receipt, version, protocol, field = (primary[key] for key in ("receipt", "version", "protocol", "field"))
    provenance = {"kind": "extracted", "tool": "tools/extraction/extract_legacy_item_obstruction.py", "tool_sha256": sha(Path(__file__)), "version": version, "protocol": protocol, "client_sha256": receipt["client_sha256"], "server_sha256": receipt["server_sha256"], "measurement_sha256": receipt["measurement_sha256"], "audit_sha256": receipt["audit_sha256"], "preservation": receipt["preservation"], "building_flag_source_sha256": receipt["sources_sha256"], "note": f"Only the original item entity is proven permanently nonblocking. Both constructors leave Entity.{field} false; all original class files were audited, and its {primary['writers']} writes belong to unrelated entity classes. Other entries remain unknown (0). No entity or world tick was executed."}
    if len(records) > 1:
        provenance["aliases"] = [{"version": row["version"], "client_sha256": row["receipt"]["client_sha256"], "server_sha256": row["receipt"]["server_sha256"], "measurement_sha256": row["receipt"]["measurement_sha256"], "audit_sha256": row["receipt"]["audit_sha256"]} for row in sorted(records, key=lambda row: row["version"])]
    if args.source_proof is not None:
        proof = primary["raw"]["source_nonblocking_proof"]
        provenance["source_nonblocking_proof"] = {"binding_sha256": sha(args.source_proof), "source_proof_sha256": proof["source_proof_sha256"], "entity": proof["entity"], "source_sha256": proof["source_sha256"], "native_xp_constructor_executed": False, "native_world_query_executed": False}
        provenance["note"] = "The original item and XP-orb entities are permanently nonblocking. Item constructor measurements are unchanged. The XP-orb rule follows the reviewed source and original-bytecode proof: its base flag stays false and all flag writers target unrelated entity classes. No XP constructor, entity/world tick or native world-query invocation was executed for this proof. Other entries remain unknown (0)."
    registry_rules = {primary["identities"][name]: rule for name, rule in rules.items()}
    document = {"schema": 1, "entities": registry_rules, "fixed_boxes": {}, "_provenance": provenance}
    output = json.dumps(document, indent=2) + "\n"
    destination = args.data / str(protocol) / "entity-obstruction.json"
    if args.check:
        if not destination.is_file() or destination.read_text(encoding="utf-8") != output:
            raise ValueError("Canonical legacy item obstruction data differs")
    else:
        destination.write_text(output, encoding="utf-8")
    print(f"{protocol}: {len(rules)} entity keys; {sum(rules.values())} proven permanently nonblocking")


if __name__ == "__main__":
    main()
