"""Bind metadata-era selection geometry to each release's own native source."""

import argparse
import hashlib
import json
import re
from pathlib import Path

from resolve_legacy_mining import methods, one, params

METHOD = r"(?:public|protected|private|default)\s+(?:(?:static|final)\s+)*(\w+(?:<[^>]+>)?)\s+(\w+)\(([^)]*)\)\s*\{"
PRIMITIVES = {"boolean", "byte", "short", "char", "int", "long", "float", "double", "void"}


def resolve_sources(symbols: dict, sources: dict[str, str]) -> dict:
    block, state = symbols["block"], symbols["state"]
    world, position = symbols["hardness_args"][-2:]
    mutable = len(symbols["hardness_args"]) == 2
    prefix = [world, position] if mutable else [state, world, position]
    block_methods = list(methods(sources[block], METHOD))
    ray, body = one([(m, b) for m, b in block_methods if params(m[3])[:len(prefix)] == prefix
                     and len(params(m[3])) == len(prefix) + 2 and params(m[3])[-1] == params(m[3])[-2]
                     and params(m[3])[-1] not in PRIMITIVES], "native block trace")
    vector, hit = params(ray[3])[-1], ray[1]
    vector_fields = re.findall(r"public\s+(?:final\s+)?double\s+(\w+)\s*;", sources[vector])
    if len(vector_fields) != 3:
        raise ValueError("Incomplete native vector coordinates")
    point = one(re.findall(r"public\s+" + vector + r"\s+(\w+)\s*;", sources[hit]), "hit point")
    direction, face = one([(m[1], m[2]) for m in re.finditer(r"public\s+(\w+)\s+(\w+)\s*;", sources[hit])
                           if m[1] in sources and re.search(r"enum\s+" + m[1] + r"\b", sources[m[1]])
                           and re.search(r'"north"|\bNORTH\b', sources[m[1]])], "hit direction")
    getter = one(re.findall(r"\bclass\s+" + world + r"\b[^\{]*?\bimplements\s+(\w+)", sources[world]), "world view")
    result = {"world": world, "position": position, "getter": getter, "ray": ray[2], "ray_args": params(ray[3]),
              "vector_type": vector, "vector_fields": vector_fields, "hit_type": hit, "hit_point": point,
              "direction_type": direction, "hit_face": face, "mutable_bounds": mutable, "overrides": {}}
    offsets = [m[2] for m, b in block_methods if m[1] == vector and params(m[3]) == [state, getter, position] and "15L" in b]
    result["offset"] = one(offsets, "native block offset") if offsets else None
    doors = [name for name, source in sources.items() if all('"' + property + '"' in source for property in ("hinge", "half", "open", "powered"))]
    result["door_class"] = one(doors, "native door class") if doors else None
    if doors:
        _, decode = one([(m, b) for m, b in methods(sources[doors[0]], METHOD) if m[1] == state and params(m[3]) == ["int"]], "door metadata decoder")
        if not re.search(r"&\s*8\b", decode):
            raise ValueError("Unsupported native door half encoding")
        result["door_upper_bit"] = 8
    if mutable:
        setter, assignments = one([(m, b) for m, b in block_methods if m[1] == "void" and params(m[3]) == ["float"] * 6], "native bounds setter")
        fields = []
        for argument in setter[3].split(","):
            name = argument.split()[-1]
            fields.append(one(re.findall(r"this\.(\w+)\s*=\s*" + name + r"\s*;", assignments), "bounds assignment"))
        result["bounds_fields"] = fields
        result["bounds_update"] = one(re.findall(r"this\.(\w+)\((?:\(" + getter + r"\))?\w+\s*,\s*\w+\);", body), "bounds update")
    else:
        helper, _ = one([(m, b) for m, b in block_methods if m[1] == hit and len(params(m[3])) == 4
                         and params(m[3])[:3] == [position, vector, vector]], "native box trace helper")
        box = params(helper[3])[-1]
        coordinates = re.findall(r"public\s+final\s+double\s+(\w+)\s*;", sources[box])
        if len(coordinates) != 6:
            raise ValueError("Incomplete native box coordinates")
        result["box_type"], result["box_fields"] = box, coordinates
        state_var = ray[3].split(",")[0].split()[-1]
        result["state_outline"] = one(re.findall(r"\b" + state_var + r"\.(\w+)\(\w+,\s*\w+\)", body), "native state outline")

    for name, source in sources.items():
        if name == block:
            continue
        overrides = [(m, b) for m, b in methods(source, METHOD) if m[1] == hit and m[2] == ray[2] and params(m[3]) == params(ray[3])]
        for _, override in overrides:
            if re.fullmatch(r"\s*return\s+null\s*;\s*", override):
                row = {"kind": "empty"}
            elif mutable and "Arrays.binarySearch" in override and "new " + hit + "[8]" in override:
                facing = one(re.findall(r"int\s+\w+\s*=\s*(?:\(\(\w+\))?\w+\.(\w+)\((\w+)\)\)?\.(\w+)\(\);", override), "stair facing")
                half = one(re.findall(r"boolean\s+\w+\s*=\s*\w+\." + facing[0] + r"\((\w+)\)\s*==\s*(\w+\.\w+(?:\.\w+)?);", override), "stair half")
                row = {"kind": "octants", "property_get": facing[0], "facing_property": facing[1], "facing_index": facing[2],
                       "half_property": half[0], "top_value": half[1],
                       "exclusions": one(re.findall(r"int\[\]\s+\w+\s*=\s*(\w+)\[", override), "stair excluded octants"),
                       "trace_flag": one(re.findall(r"this\.(\w+)\s*=\s*true;", override), "stair trace flag"),
                       "trace_index": one([m[1] for m in re.finditer(r"this\.(\w+)\s*=\s*(\w+);", override)
                                           if m[2] not in ("true", "false")], "stair octant index")}
            elif not mutable and re.search(r"for\s*\(" + result["box_type"] + r"\s+\w+\s*:", override):
                boxes, actual = one(re.findall(r":\s*(\w+)\(this\.(\w+)\(\w+,\s*\w+,\s*\w+\)\)\)", override), "stair boxes and actual state")
                row = {"kind": "boxes", "boxes": boxes, "actual_state": actual}
            elif mutable and re.search(r"return\s+super\." + ray[2] + r"\(", override):
                row = {"kind": "mutable"}
            else:
                raise ValueError(f"Unclassified native trace override: {name}")
            result["overrides"][name] = row
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--symbols", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    symbols = json.loads(args.symbols.read_text())
    tree = args.official / (symbols["version"] + "-decompiled")
    for name, expected in symbols["sources"].items():
        if hashlib.sha256((tree / (name + ".java")).read_bytes()).hexdigest() != expected:
            raise ValueError(f"Mining source binding changed: {name}")
    sources = {p.stem: re.sub(r"/\*.*?\*/", "", p.read_text(), flags=re.S) for p in tree.glob("*.java")}
    bound = resolve_sources(symbols, sources)
    symbols["outline_bindings"] = bound
    # Hash every source in the legacy tree: selection can delegate into neighboring blocks, properties and utility methods.
    symbols["outline_source_sha256"] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(tree.glob("*.java"))}
    args.out.write_text(json.dumps(symbols, indent=2) + "\n")


if __name__ == "__main__":
    main()
