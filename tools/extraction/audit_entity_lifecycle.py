"""Audit native client world retention and remove-before-insert ordering across official source trees."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

from measure_block_interactions import interaction_catalog

NAME = r"[A-Za-z_$][\w$]*"


def masked(source: str) -> str:
    pattern = r'/\*.*?\*/|//[^\n]*|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    return re.sub(pattern, lambda match: re.sub(r"[^\n]", " ", match[0]), source, flags=re.S)


def closing(source: str, start: int, left: str, right: str) -> int:
    depth = 1
    for index in range(start + 1, len(source)):
        depth += (source[index] == left) - (source[index] == right)
        if depth == 0:
            return index
    raise ValueError("Unbalanced native source boundary")


def methods(source: str) -> list[dict]:
    code = masked(source)
    result = []
    pattern = r"(?:public|private|protected)\s+(?:final\s+)?void\s+(" + NAME + r")\s*\(([^()]*)\)\s*\{"
    for match in re.finditer(pattern, code):
        end = closing(code, match.end() - 1, "{", "}")
        parameters = [re.sub(r"\bfinal\s+", "", part).strip().split() for part in match[2].split(",") if part.strip()]
        result.append({"name": match[1], "parameters": parameters, "line": source[:match.start()].count("\n") + 1, "body": code[match.end():end], "source": source[match.start():end + 1]})
    return result


def respawn_guard(source: str, world: str) -> dict:
    found = []
    for method in methods(source):
        if len(method["parameters"]) != 1:
            continue
        body = method["body"]
        assignment = re.search(r"this\." + NAME + r"\s*=\s*new\s+" + re.escape(world) + r"\s*\(", body)
        if assignment is None:
            continue
        guards = []
        for match in re.finditer(r"\bif\s*\(", body):
            end = closing(body, match.end() - 1, "(", ")")
            start = end + 1
            while start < len(body) and body[start].isspace():
                start += 1
            if start < len(body) and body[start] == "{" and start < assignment.start() < closing(body, start, "{", "}"):
                guards.append((start, body[match.end():end].strip(), match.start()))
        if not guards:
            continue
        _, condition, before = max(guards)
        if re.fullmatch(NAME, condition):
            declaration = re.search(r"\bboolean\s+" + re.escape(condition) + r"\s*=\s*([^;]+);", body[:before])
            if declaration is None:
                continue
            condition = declaration[1].strip()
        if "!=" not in condition or "||" in condition or "&&" in condition:
            continue
        found.append({"method": method["name"], "line": method["line"], "condition": re.sub(r"\s+", " ", condition), "source": method["source"]})
    if len(found) != 1:
        raise ValueError(f"Expected one guarded native respawn world replacement, found {len(found)}")
    return found[0]


def replacement(source: str) -> dict:
    found = []
    all_methods = methods(source)
    for method in all_methods:
        parameters, body = method["parameters"], method["body"]
        kind, storage = None, None
        if len(parameters) == 1 and len(parameters[0]) == 2:
            entity = parameters[0][1]
            insert = re.search(r"this\.(" + NAME + r")\.addEntity\(\s*" + re.escape(entity) + r"\s*\)", body)
            if insert and re.search(r"this\.removeEntity\(\s*" + re.escape(entity) + r"\.getId\(\)", body[:insert.start()]):
                kind, storage = "remove_by_id", insert[1]
        if len(parameters) == 2 and parameters[0][0] == "int" and all(len(part) == 2 for part in parameters):
            identity, entity_type, entity = parameters[0][1], parameters[1][0], parameters[1][1]
            managed_insert = re.search(r"this\.(" + NAME + r")\.addEntity\(\s*" + re.escape(entity) + r"\s*\)", body)
            if managed_insert and re.search(r"this\.removeEntity\(\s*" + re.escape(identity) + r"\s*,", body[:managed_insert.start()]):
                kind, storage = "remove_by_id", managed_insert[1]
            insert = re.search(r"this\.(" + NAME + r")\." + NAME + r"\(\s*" + re.escape(identity) + r"\s*,\s*" + re.escape(entity) + r"\s*\)", body)
            if insert:
                prefix = body[:insert.start()]
                old = re.search(r"\b" + re.escape(entity_type) + r"\s+(" + NAME + r")\s*=\s*this\." + NAME + r"\(\s*" + re.escape(identity) + r"\s*\)", prefix)
                if old and re.search(r"if\s*\(\s*" + re.escape(old[1]) + r"\s*!=\s*null\s*\)\s*\{\s*this\." + NAME + r"\(\s*" + re.escape(old[1]) + r"\s*\)", prefix):
                    kind, storage = "lookup_then_remove", insert[1]
                calls = list(re.finditer(r"this\.(" + NAME + r")\(\s*" + re.escape(identity) + r"\s*\)\s*;", prefix))
                for call in calls:
                    removers = [candidate for candidate in all_methods if candidate["name"] == call[1] and len(candidate["parameters"]) == 1 and candidate["parameters"][0][0] == "int"]
                    if call[1] == "removeEntity" or any(entity_type in candidate["body"] and "null" in candidate["body"] for candidate in removers):
                        kind, storage = "remove_by_id", insert[1]
        if kind is not None:
            found.append({"method": method["name"], "line": method["line"], "kind": kind, "storage": storage, "source": method["source"]})
    if len(found) != 1:
        raise ValueError(f"Expected one native remove-before-insert method, found {len(found)}")
    return found[0]


def unique_hit(root: Path, literal: str) -> Path:
    result = subprocess.run(["rg", "-l", "-F", literal, str(root), "-g", "*.java"], capture_output=True, text=True, check=False)
    paths = result.stdout.splitlines()
    if result.returncode != 0 or len(paths) != 1:
        raise ValueError(f"Cannot uniquely bind native source marker {literal}")
    return Path(paths[0])


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    rows = []
    versions = json.loads((repo / "data/java/versions.json").read_text())["versions"]
    catalog = {row["name"]: row for row in versions}
    catalog.update({row["name"]: row for row in interaction_catalog(versions, args.official)})
    for version in sorted(catalog.values(), key=lambda row: tuple(int(part) for part in row["name"].split("."))):
        name, protocol = version["name"], version["protocol"]
        root = args.official / (name + "-client-decompiled")
        row = {"version": name, "protocol": protocol, "status": "failed"}
        try:
            if protocol < 498:
                world = unique_hit(root, '"MpServer"')
                listener = unique_hit(root, '"disconnect.lost"')
            else:
                listener = root / "net/minecraft/client/multiplayer/ClientPacketListener.java"
                world = next(path for stem in ("ClientLevel", "MultiPlayerLevel") if (path := root / ("net/minecraft/client/multiplayer/" + stem + ".java")).exists())
            guard = respawn_guard(listener.read_text(), world.stem)
            insertion = replacement(world.read_text())
            jar = args.official / "downloads" / name / "client.jar"
            metadata = json.loads((jar.parent / "version.json").read_text())
            sha1, sha256 = hashlib.sha1(), hashlib.sha256()
            with jar.open("rb") as stream:
                while chunk := stream.read(4 * 1024 * 1024):
                    sha1.update(chunk)
                    sha256.update(chunk)
            if sha1.hexdigest() != metadata["downloads"]["client"]["sha1"]:
                raise ValueError("Official client artifact hash differs")
            row.update(status="complete", identity="numeric_dimension" if protocol < 735 else "dimension_resource_key", respawn=guard, insertion=insertion, client_sha256=sha256.hexdigest(), source_sha256={str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in (world, listener)})
        except (OSError, ValueError, StopIteration) as error:
            row["error"] = str(error)
        rows.append(row)
        print(name, row["status"], row.get("error", ""), flush=True)
    args.out.write_text(json.dumps({"kind": "source audit; not native world execution", "tool_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "releases": rows}, indent=2) + "\n")
    if any(row["status"] != "complete" for row in rows):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
