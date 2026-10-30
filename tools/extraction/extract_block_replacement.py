"""Extract ordinary-cube replacement rules from verified vanilla measurements."""

import argparse
from collections.abc import Mapping
import copy
import hashlib
import json
from pathlib import Path
from urllib.parse import unquote, urlsplit
import zipfile


def replacement_flags(mask: int) -> int:
    if type(mask) is not int or not 0 <= mask < 4096 or mask & 63 not in (0, 63) or mask >> 6 not in (0, 63):
        raise ValueError("A replacement mask must have uniform clicked and destination faces")
    return int(bool(mask & 63)) | (int(bool(mask & 4032)) << 1)


def _ranges(values: Mapping[int, int]) -> list[list[int]]:
    result: list[list[int]] = []
    for state, flags in sorted(values.items()):
        if result and state == result[-1][1] + 1 and flags == result[-1][2]:
            result[-1][1] = state
        else:
            result.append([state, state, flags])
    return result


def _state_id(value: str) -> int:
    if type(value) is not str:
        raise ValueError("A native state identifier must be a decimal string")
    state = int(value)
    if state < 0 or str(state) != value:
        raise ValueError(f"Invalid native state identifier: {value}")
    return state


def compact_states(raw: Mapping[str, list], expected: Mapping[int, str], *, legacy: bool = False) -> dict:
    values: dict[int, int] = {}
    seen: set[int] = set()
    overrides: dict[str, list[list[int]]] = {}
    for key, row in raw.items():
        state = _state_id(key)
        identity = state >> 4 if legacy else state
        if not isinstance(row, list) or len(row) != 2 or identity not in expected or row[0] != expected[identity]:
            raise ValueError(f"Native replacement state differs from its registry: {key}")
        rule = row[1]
        if isinstance(rule, dict):
            if not legacy or set(rule) != {"default", "below", "below_states_tested"} or not isinstance(rule["below"], dict):
                raise ValueError(f"Unsupported replacement dependency at {key}")
            if type(rule["below_states_tested"]) is not int or rule["below_states_tested"] < len(raw):
                raise ValueError(f"Incomplete native below-state probe at {key}")
            values[state] = replacement_flags(rule["default"])
            neighbors: dict[int, int] = {}
            for below, mask in rule["below"].items():
                flags = replacement_flags(mask)
                if below not in raw or flags == values[state]:
                    raise ValueError(f"Unmeasured or redundant below-state rule at {key}: {below}")
                neighbors[_state_id(below)] = flags
            if neighbors:
                overrides[key] = _ranges(neighbors)
        else:
            values[state] = replacement_flags(rule)
        seen.add(identity)
    if seen != set(expected):
        raise ValueError("Native replacement states do not cover the registry")
    return {"state_count": len(values), "ranges": _ranges(values), "below_overrides": overrides}


def fold(native: Mapping, blocks: Mapping, interactions: Mapping) -> dict:
    def qualified(name: str) -> str:
        return name if ":" in name else "minecraft:" + name

    cubes = {qualified(name) for name, rule in interactions["items"].items() if rule[0] == "simple_cube"}
    if not cubes or not isinstance(native["items"], list) or len(native["items"]) != len(cubes) or set(native["items"]) != cubes:
        raise ValueError("Native replacement measurements must cover every ordinary cube item exactly once")
    identity = blocks["identity"]
    if identity == "legacy":
        expected = {block["block_id"]: qualified(block.get("name", block.get("material"))) for block in blocks["blocks"]}
    elif identity == "flat":
        expected = {state: block["name"] for block in blocks["blocks"] for state in range(block["min_state"], block["max_state"] + 1)}
    else:
        raise ValueError(f"Unsupported replacement state identity: {identity}")
    return {"schema": 1, "identity": identity, **compact_states(native["states"], expected, legacy=identity == "legacy")}


def select_profile(protocol: int, releases: list[tuple[str, dict]]) -> tuple[dict, list[dict]]:
    latest, selected = releases[-1]
    differences = []
    for version, profile in releases:
        if profile == selected:
            continue
        expected = copy.deepcopy(selected)
        flags = {state: value for first, last, value in expected["ranges"] for state in range(first, last + 1)}
        if protocol != 47 or version != "1.8" or latest != "1.8.9" or expected["identity"] != "legacy" or flags.get(0) != 3 or any(flags.get(state) != 2 or expected["below_overrides"].get(str(state)) != [[2802, 2803, 3]] for state in range(2809, 2812)):
            raise ValueError(f"Unreviewed native replacement difference for protocol {protocol}: {version}")
        flags[0] = 2
        for state in range(2809, 2812):
            del flags[state]
            del expected["below_overrides"][str(state)]
        expected["state_count"] -= 3
        expected["ranges"] = _ranges(flags)
        if profile != expected:
            raise ValueError(f"Unreviewed native replacement difference for protocol {protocol}: {version}")
        differences.append({"version": version, "profile": copy.deepcopy(profile)})
    return copy.deepcopy(selected), differences


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--measurements", required=True, action="append", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()

    def digest(path: Path, algorithm: str = "sha256") -> str:
        hasher = hashlib.new(algorithm)
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                hasher.update(chunk)
        return hasher.hexdigest()

    def verified(path: Path, expected: str) -> None:
        if digest(path) != expected:
            raise ValueError(f"Replacement measurement input changed: {path}")

    def read(path: Path) -> dict:
        return json.loads(path.read_text(encoding="utf-8"))

    catalog = read(args.data / "versions.json")["versions"]
    protocols = {row["name"]: row["protocol"] for row in catalog}
    grouped: dict[int, list] = {}
    evidence: dict[str, dict] = {}
    for root in args.measurements:
        tools = read(root / "tools.json")
        sources = tools.get("sources", tools.get("source_sha256", {}))
        if not sources:
            raise ValueError(f"Missing replacement measurement tools: {root}")
        for field in (sources, tools.get("helper_classes", {}), tools.get("source_receipt_sha256", {})):
            for name, expected in field.items():
                verified(Path(name), expected)
        preserved_clients = {}
        for name in tools.get("source_receipt_sha256", {}):
            for row in read(Path(name)):
                if row.get("status") == "complete" and row.get("preservation"):
                    preserved_clients[row["version"]] = row
        if "helper_receipt_sha256" in tools:
            command = read(root / "commands.json")[0]["command"]
            helper = Path(command[command.index("-cp") + 1]).parent / "results.json"
            verified(helper, tools["helper_receipt_sha256"])
        for report in read(root / "results.json"):
            version, protocol = report["version"], report["protocol"]
            if version in evidence or report.get("status") != "complete":
                raise ValueError(f"Duplicate or incomplete native replacement measurement: {version}")
            directory = root / version
            measurement = directory / "replacement.json"
            verified(measurement, report["measurement_sha256"])
            native = read(measurement)
            contracts = native["contracts"]
            if report["states"] != len(contracts["states"]) or report["scenarios"] != len(native["rows"]) or not native["rows"] or report["predicate_calls"] != contracts["predicate_calls"] or report["predicate_calls"] <= 0 or report["predicate_origins"] != contracts["predicate_origins"]:
                raise ValueError(f"Native replacement receipt differs from its measurements: {version}")
            downloads = args.official / "downloads" / version
            metadata = read(downloads / "version.json")
            artifacts = {}
            for key, leaf in (("client", "client.jar"), ("server", "server.jar"), ("client_mappings", "client_mappings.txt")):
                if key in metadata["downloads"]:
                    path = downloads / leaf
                    actual = digest(path, "sha1")
                    if actual != metadata["downloads"][key]["sha1"]:
                        raise ValueError(f"Official replacement artifact hash differs: {version}: {leaf}")
                    artifacts[leaf] = digest(path)
            if artifacts["client.jar"] != report["client_sha256"]:
                raise ValueError(f"Native replacement used a different client jar: {version}")
            preserved = report if "preservation" in report else preserved_clients.get(version, {})
            preservation = preserved.get("preservation", {})
            if preserved.get("client_sha256") != report["client_sha256"] or preservation.get("unchanged_class_files", 0) <= 0:
                raise ValueError(f"Missing original client-class preservation evidence: {version}")
            with zipfile.ZipFile(downloads / "client.jar") as archive:
                classes = {name: hashlib.sha256(archive.read(name)).hexdigest() for name in archive.namelist() if name.endswith(".class")}
            if len(classes) != preservation["unchanged_class_files"] or hashlib.sha256(json.dumps(classes, sort_keys=True).encode()).hexdigest() != preservation["classes_sha256"]:
                raise ValueError(f"Preserved replacement class bytes differ from the official jar: {version}")
            if version not in protocols:
                with zipfile.ZipFile(downloads / "client.jar") as archive:
                    embedded = json.loads(archive.read("version.json"))
                if embedded["id"] != version or embedded["protocol_version"] != protocol or protocol not in protocols.values():
                    raise ValueError(f"Unverified replacement release alias: {version}")
            elif protocols[version] != protocol:
                raise ValueError(f"Replacement protocol differs from the version catalog: {version}")
            source = args.official / (version + "-client-decompiled")
            verified(source / ".complete.json", report["completion_sha256"])
            if read(source / ".complete.json")["artifact_sha1"] != metadata["downloads"]["client"]["sha1"]:
                raise ValueError(f"Replacement source tree belongs to another client artifact: {version}")
            mapping = {}
            if (downloads / "client_mappings.txt").exists():
                for line in (downloads / "client_mappings.txt").read_text(encoding="utf-8").splitlines():
                    if line and not line.startswith((" ", "#")) and line.endswith(":") and " -> " in line:
                        name, raw = line[:-1].split(" -> ")
                        mapping[raw] = name
            predicate_sources = {}
            origins = set(contracts["predicate_origins"].values())
            if len(origins) != 1 or not contracts["predicate_origins"] or set(contracts["predicate_origins"]) != set(contracts["predicate_owner_counts"]):
                raise ValueError(f"Missing native replacement predicate origins: {version}")
            origin = next(iter(origins))
            if urlsplit(origin).scheme != "file" or Path(unquote(urlsplit(origin).path)).name != "client-unsigned.jar":
                raise ValueError(f"Replacement predicate did not originate from the client jar: {version}")
            unsigned = Path(unquote(urlsplit(origin).path))
            if unsigned.exists():
                verified(unsigned, preservation["unsigned_jar_sha256"])
            classes = (directory / "classes.log").read_text(encoding="utf-8")
            native_classes = set(report["native_origins"].values()) if "preservation" in report else set(report["native_origins"])
            for raw in set(contracts["predicate_origins"]) | native_classes:
                prefix = "[class,load] " + raw + " source: "
                found = [line.split(prefix, 1)[1] for line in classes.splitlines() if prefix in line]
                if found != [origin]:
                    raise ValueError(f"Native replacement class origin differs: {version}: {raw}")
            for raw in contracts["predicate_origins"]:
                leaf = mapping.get(raw, raw).split("$")[0].replace(".", "/") + ".java"
                predicate_sources[leaf] = digest(source / leaf)
            for name, key in (("interaction-symbols.json", "bindings_sha256"), ("remote-symbols.json", "remote_bindings_sha256")):
                if key in report:
                    verified(directory / name, report[key])
                    for leaf, expected in read(directory / name).get("client_sources", {}).items():
                        verified(source / leaf, expected)
            adapter = directory / "adapters" / ("EntityWorldAdapter.java" if "preservation" in report else "LegacyDimensionWorldAdapter.java")
            verified(adapter, report.get("owned_adapter_sha256", report.get("adapter_sha256")))
            data = args.data / str(protocol)
            profile = fold(contracts, read(data / "blocks.json"), read(data / "block-interactions.json"))
            grouped.setdefault(protocol, []).append((version, profile))
            evidence[version] = {"version": version, "measurement_sha256": report["measurement_sha256"], "measurement_receipt_sha256": digest(root / "results.json"), "client_class_preservation": preservation, "artifacts_sha256": artifacts, "completion_sha256": report["completion_sha256"], "measurement_tools_sha256": digest(root / "tools.json"), "measurement_sources_sha256": {Path(name).name: value for name, value in sources.items()}, "predicate_sources_sha256": predicate_sources, "predicate_calls": contracts["predicate_calls"], "state_count": len(contracts["states"]), "cube_item_count": len(contracts["items"]), "full_use_samples": len(native["rows"]), "scope": contracts["scope"]}
    if not set(protocols).issubset(evidence) or set(grouped) != set(protocols.values()):
        raise ValueError("Replacement measurements do not cover every catalog release and protocol")
    documents = {}
    for protocol, releases in grouped.items():
        releases.sort(key=lambda row: tuple(map(int, row[0].split("."))))
        document, differences = select_profile(protocol, releases)
        document["_provenance"] = {"kind": "extracted", "tool": "tools/extraction/extract_block_replacement.py", "tool_sha256": digest(Path(__file__)), "client_profile": releases[-1][0], "different_earlier_client_profiles": differences, "measurements": [evidence[version] for version, _ in releases], "note": "Ordinary cube items only. Flags 1=replace clicked block, 2=allow placement into the resolved destination. Ranges contain native wire state identifiers; absent identifiers remain unknown. Below-state overrides preserve measured legacy dependencies. Protocol aliases use the latest measured vanilla client profile. Stack, player, world, obstruction and use-result rules remain runtime conditions."}
        documents[protocol] = document
    for protocol, document in sorted(documents.items()):
        target = args.out / str(protocol) / "block-replacement.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(document, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        print(f"{protocol}: {document['state_count']} states, {len(document['ranges'])} ranges")


if __name__ == "__main__":
    main()
