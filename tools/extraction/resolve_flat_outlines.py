"""Resolve pre-mappings flattened outline symbols from native source signatures and delegation."""

import argparse
import hashlib
import json
import re
from pathlib import Path

from resolve_legacy_mining import methods, one, params

METHOD = r"(?:public|protected|private|default)\s+(?:(?:static|final)\s+)*(\w+(?:<[^>]+>)?)\s+(\w+)\(([^)]*)\)\s*\{"


def resolve_sources(symbols: dict, sources: dict[str, str]) -> dict:
    block = symbols["block"]
    state, getter, position = symbols["hardness_args"]
    block_methods = list(methods(sources[block], METHOD))
    state_methods = list(methods(sources[state], METHOD))
    factory, body = one([(m, b) for m, b in block_methods if params(m[3]) == ["double"] * 6 and "/ 16.0" in b], "shape factory")
    shape = factory[1]
    utility = re.search(r"return\s+(\w+)\.\w+\(", body)[1]
    utility_source = sources[utility]
    bits = one(list(re.finditer(r"new\s+\w+\(1,\s*1,\s*1\)", utility_source)), "full cube bits")
    declarations = list(re.finditer(r"(?:private|public)\s+static\s+final\s+" + shape + r"\s+(\w+)\s*=", utility_source[:bits.start()]))
    if not declarations:
        raise ValueError("No full cube singleton declaration")
    full_field = declarations[-1][1]
    utility_methods = list(methods(utility_source, METHOD))
    cube = one([m[2] for m, b in utility_methods if m[1] == shape and not params(m[3]) and re.fullmatch(r"\s*return\s+" + full_field + r";\s*", b)], "full cube accessor")
    empty = one([m[2] for m, b in utility_methods if m[1] == shape and not params(m[3]) and m[2] != cube and re.fullmatch(r"\s*return\s+\w+;\s*", b)], "empty shape accessor")
    outline, _ = one([(m, b) for m, b in block_methods if m[1] == shape and params(m[3])[:3] == [state, getter, position] and re.fullmatch(r"\s*return\s+" + utility + r"\." + cube + r"\(\);\s*", b)], "block outline")
    interaction = one([m[2] for m, b in block_methods if m[1] == shape and params(m[3]) == [state, getter, position] and re.fullmatch(r"\s*return\s+" + utility + r"\." + empty + r"\(\);\s*", b)], "block interaction")

    def delegate(returns: str, arguments: list[str], target: str, label: str) -> str:
        return one([m[2] for m, b in state_methods if m[1] == returns and params(m[3]) == arguments and re.search(r"\." + target + r"\(this\s*,", b)], label)

    offset, _ = one([(m, b) for m, b in block_methods if params(m[3]) == [state, getter, position] and "15L" in b and "0.5" in b], "block offset")
    to_boxes, box = one([(m[2], re.fullmatch(r"List<(\w+)>", m[1])[1]) for m, b in methods(sources[shape], METHOD) if re.fullmatch(r"List<\w+>", m[1]) and not params(m[3])], "shape boxes")
    vector = offset[1]
    box_fields = re.findall(r"public\s+final\s+double\s+(\w+)\s*;", sources[box])
    vector_fields = re.findall(r"public\s+final\s+double\s+(\w+)\s*;", sources[vector])
    if len(box_fields) != 6 or len(vector_fields) != 3:
        raise ValueError("Incomplete box or vector coordinates")
    getter_members = list(re.finditer(r"\b(\w+)\s+(\w+)\(" + position + r"\s+\w+\)\s*;", sources[getter]))
    fluid = one([m[1] for m in getter_members if m[1] != state and any(sm[1] == m[1] and not params(sm[3]) for sm, _ in state_methods)], "fluid state getter")
    block_entity = one([m[1] for m in getter_members if m[1] not in (state, fluid)], "block entity getter")
    empty_getters = [name for name, source in sources.items() if re.search(r"public\s+enum\s+" + name + r"\s+implements\s+" + getter + r"\b", source)]
    if len(empty_getters) > 1:
        raise ValueError("Ambiguous native empty block getter")
    context = params(outline[3])[3] if len(params(outline[3])) == 4 else None
    result = {"getter": getter, "position": position, "shape": shape, "shape_utility": utility,
              "outline": delegate(shape, params(outline[3])[1:], outline[2], "state outline"),
              "interaction": delegate(shape, [getter, position], interaction, "state interaction"),
              "offset": delegate(vector, [getter, position], offset[2], "state offset"),
              "to_boxes": to_boxes, "box_type": box, "box_fields": box_fields,
              "vector_type": vector, "vector_fields": vector_fields, "context": context,
              "fluid_type": fluid,
              "block_entity_type": block_entity, "empty_getter": empty_getters[0] if empty_getters else None,
              "getter_defaults": {m[1]: int(m[2]) for m in re.finditer(r"default\s+int\s+(\w+)\(\)\s*\{\s*return\s+(\d+);", sources[getter])},
              "fluid_method": one([m[2] for m, b in state_methods if m[1] == fluid and not params(m[3])], "state fluid")}
    if context is not None:
        context_source = sources[context]
        result["context_empty"] = one(re.findall(r"static\s+" + context + r"\s+(\w+)\(\)", context_source), "empty shape context")
        result["context_holding"] = one(re.findall(r"boolean\s+(\w+)\(" + symbols["item"] + r"\s+\w+\)", context_source), "held item context")
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
    sources = {path.stem: re.sub(r"/\*.*?\*/", "", path.read_text(), flags=re.S) for path in tree.glob("*.java")}
    resolved = resolve_sources(symbols, sources)
    symbols["outline_bindings"] = resolved
    for role in ("getter", "position", "shape", "shape_utility", "box_type", "vector_type", "context", "fluid_type", "empty_getter"):
        if resolved[role] is not None:
            name = resolved[role]
            symbols["sources"][name] = hashlib.sha256((tree / (name + ".java")).read_bytes()).hexdigest()
    args.out.write_text(json.dumps(symbols, indent=2) + "\n")


if __name__ == "__main__":
    main()
