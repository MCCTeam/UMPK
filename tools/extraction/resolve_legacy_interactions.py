"""Resolve metadata-era block-use and ordinary placement contracts from that release's native client source."""

import argparse
import hashlib
import json
from pathlib import Path
import re

from resolve_flat_outlines import METHOD
from resolve_legacy_mining import methods, one, params


def resolve_sources(symbols: dict, sources: dict[str, str], outline: dict) -> dict:
    block, state, item = symbols["block"], symbols["state"], symbols["item"]
    world, position, getter = outline["world"], outline["position"], outline["getter"]
    direction = outline["direction_type"]
    block_methods = list(methods(sources[block], METHOD))
    from_item = one([body for match, body in block_methods if match[1] == block and params(match[3]) == [item]], "block from item")
    item_block = one(re.findall(r"\binstanceof\s+(\w+)", from_item), "block item class")
    item_methods = list(methods(sources[item], METHOD))
    block_item_methods = list(methods(sources[item_block], METHOD))

    def spec(owner: str, match) -> dict:
        return {"owner": owner, "name": match[2], "args": params(match[3])}

    use, body = one([(match, body) for match, body in block_methods if match[1] == "boolean" and symbols["player"] in params(match[3]) and params(match[3])[-3:] == ["float"] * 3], "passive block use")
    if not re.fullmatch(r"\s*return\s+false;\s*", body):
        raise ValueError("Native base block use is no longer passive")
    replace, _ = one([(match, body) for match, body in block_methods if match[1] == "boolean" and len(params(match[3])) == 2 and params(match[3])[-1] == position and re.fullmatch(r"\s*return\s+false;\s*", body)], "base nonreplacement")
    survive, _ = one([(match, body) for match, body in block_methods if match[1] == "boolean" and params(match[3]) == [world, position] and re.fullmatch(r"\s*return\s+\w+\.\w+\(\w+\)\.\w+\(\)\.\w+\.\w+\(\);\s*", body)], "placement into replaceable destination")
    side, _ = one([(match, body) for match, body in block_methods if match[1] == "boolean" and params(match[3]) == [world, position, direction] and re.search(r"return\s+this\." + survive[2] + r"\(", body)], "placement side delegates to survival")
    place, _ = one([(match, body) for match, body in block_methods if match[1] == state and len(params(match[3])) == 8 and params(match[3])[:3] == [world, position, direction] and params(match[3])[3:7] == ["float", "float", "float", "int"]], "block placement state")
    block_use, _ = one([(match, body) for match, body in block_item_methods if symbols["player"] in params(match[3]) and world in params(match[3]) and params(match[3])[-3:] == ["float"] * 3], "block item use")
    item_use, body = one([(match, body) for match, body in item_methods if match[2] == block_use[2] and params(match[3]) == params(block_use[3])], "base item use")
    if not re.fullmatch(r"\s*return\s+(?:false|\w+\.\w+);\s*", body):
        raise ValueError("Native base item use is no longer pass-through")
    item_block_getter, _ = one([(match, body) for match, body in block_item_methods if match[1] == block and not params(match[3])], "block item block getter")
    item_side, _ = one([(match, body) for match, body in block_item_methods if match[1] == "boolean" and params(match[3]) == [world, position, direction, symbols["player"], symbols["stack"]]], "block item placement precheck")
    block_steps = [spec(block, match) for match in (place, survive, side)]
    if outline["mutable_bounds"]:
        collision, _ = one([(match, body) for match, body in block_methods if params(match[3]) == [world, position, state] and match[1] not in (symbols["stack"], "boolean", "void")], "mutable collision box")
        bounds, _ = one([(match, body) for match, body in block_methods if match[2] == outline["bounds_update"] and params(match[3]) == [getter, position]], "mutable bounds update")
        block_steps.extend((spec(block, collision), spec(block, bounds)))
        box = collision[1]
    else:
        box = outline["box_type"]
        shape, _ = one([(match, body) for match, body in block_methods if match[1] == box and params(match[3]) == [state, getter, position] and re.fullmatch(r"\s*return\s+\w+;\s*", body)], "base full outline box")
        collision, _ = one([(match, body) for match, body in block_methods if match[1] == box and params(match[3]) in ([state, getter, position], [state, world, position]) and re.fullmatch(r"\s*return\s+\w+\.\w+\([^;]*\);\s*", body) and len(re.findall(r"\w+\s*\(", body)) == 1], "base collision delegates to outline")
        block_steps.extend((spec(block, shape), spec(block, collision)))
    box_fields = re.findall(r"public\s+final\s+double\s+(\w+)\s*;", sources[box])
    if len(box_fields) != 6:
        raise ValueError("Native collision box has no complete coordinate declaration")
    return {"item_block": item_block, "world": world, "position": position, "use": spec(block, use), "replace": spec(block, replace),
            "block_steps": block_steps, "collision": spec(block, collision), "box_type": box, "box_fields": box_fields,
            "item_use": spec(item, item_use), "block_item_use": spec(item_block, block_use), "item_block_getter": spec(item_block, item_block_getter),
            "item_steps": [spec(item_block, match) for match in (block_use, item_side, item_block_getter)]}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", required=True, type=Path)
    parser.add_argument("--symbols", required=True, type=Path)
    parser.add_argument("--outline", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    symbols = json.loads(args.symbols.read_text())
    tree = args.official / (symbols["version"] + "-client-decompiled")
    paths = {path.stem: path for path in tree.glob("*.java")}
    sources = {name: re.sub(r"/\*.*?\*/", "", path.read_text(), flags=re.S) for name, path in paths.items()}
    outline = json.loads(args.outline.read_text())["outline_bindings"]
    bindings = resolve_sources(symbols, sources, outline)
    used = {symbols[name] for name in ("block", "state", "item", "stack", "player")}
    used.update(bindings[name] for name in ("item_block", "world", "position", "box_type"))
    used.add(outline["getter"])
    symbols["interaction_bindings"] = bindings
    symbols["client_sources"] = {name + ".java": hashlib.sha256(paths[name].read_bytes()).hexdigest() for name in sorted(used)}
    args.out.write_text(json.dumps(symbols, indent=2) + "\n")


if __name__ == "__main__":
    main()
