"""Resolve placement-context contracts in unmapped 1.13–1.14.3 native client source."""

import argparse
import hashlib
import json
from pathlib import Path
import re

from resolve_flat_outlines import METHOD
from resolve_legacy_mining import methods, one, params


def resolve_sources(symbols: dict, sources: dict[str, str], outline: dict) -> dict:
    block, state, item = symbols["block"], symbols["state"], symbols["item"]
    getter, position = outline["getter"], outline["position"]
    block_methods = list(methods(sources[block], METHOD))
    item_methods = list(methods(sources[item], METHOD))
    from_item = one([body for match, body in block_methods if match[1] == block and params(match[3]) == [item]], "block from item")
    item_block = one(re.findall(r"\binstanceof\s+(\w+)", from_item), "block item class")
    block_item_methods = list(methods(sources[item_block], METHOD))

    def spec(owner: str, match) -> dict:
        return {"owner": owner, "name": match[2], "args": params(match[3])}

    candidates = []
    for match, body in block_item_methods:
        if len(params(match[3])) != 1:
            continue
        native = [(base, value) for base, value in item_methods if (base[1], base[2]) == (match[1], match[2]) and params(base[3]) == params(match[3])]
        contexts = re.findall(r"\bnew\s+(\w+)\(\w+\)", body)
        if len(native) == 1 and len(contexts) == 1:
            candidates.append((match, body, native[0], contexts[0]))
    block_use, use_body, (item_use, item_body), placement_context = one(candidates, "block item use constructs placement context")
    use_context = params(block_use[3])[0]
    result_type = block_use[1]
    enum_body = sources[result_type].split("{", 1)[1].split(";", 1)[0]
    results = re.findall(r"\b(\w+)\s*(?:,|$)", enum_body.strip())
    if len(results) != 3 or not re.fullmatch(r"\s*return\s+" + re.escape(result_type + "." + results[1]) + r";\s*", item_body):
        raise ValueError("Native base item use is no longer the middle PASS result")
    item_place, body = one([(match, body) for match, body in block_item_methods if match[1] == result_type and params(match[3]) == [placement_context]], "block item placement")
    if not re.search(r"return\s+" + re.escape(result_type + "." + results[0]) + r";\s*$", body):
        raise ValueError("Native item placement no longer ends with SUCCESS")
    edible = re.findall(r"&&\s*this\.(\w+)\(\)", use_body)
    if len(edible) > 1:
        raise ValueError("Ambiguous native block-item food fallback")

    use, body = one([(match, body) for match, body in block_methods if match[1] == "boolean" and symbols["player"] in params(match[3]) and params(match[3])[0] == state], "passive block use")
    if not re.fullmatch(r"\s*return\s+false;\s*", body):
        raise ValueError("Native base block use is no longer passive")
    replace, body = one([(match, body) for match, body in block_methods if match[1] == "boolean" and params(match[3]) == [state, placement_context]], "base material replacement")
    material_replaceable = one(re.findall(r"return\s+this\.\w+\.(\w+)\(\)\s*&&", body), "material replacement guard")
    place, body = one([(match, body) for match, body in block_methods if match[1] == state and params(match[3]) == [placement_context]], "default placement state")
    if not re.fullmatch(r"\s*return\s+this\.\w+\(\);\s*", body):
        raise ValueError("Native base placement no longer returns a default state")
    survive, _ = one([(match, body) for match, body in block_methods if match[1] == "boolean" and len(params(match[3])) == 3 and params(match[3])[0] == state and params(match[3])[-1] == position and re.fullmatch(r"\s*return\s+true;\s*", body)], "unconditional block survival")
    shape_args = [getter, position] + ([outline["context"]] if outline["context"] else [])
    state_shape = one([body for match, body in methods(sources[state], METHOD) if match[2] == outline["outline"] and params(match[3]) == shape_args], "state outline delegate")
    shape_name = one(re.findall(r"return\s+this\.\w+\(\)\.(\w+)\(this,", state_shape), "base outline method")
    shape, body = one([(match, body) for match, body in block_methods if match[2] == shape_name and params(match[3]) == [state] + shape_args], "base outline")
    if not re.fullmatch(r"\s*return\s+" + re.escape(outline["shape_utility"]) + r"\.\w+\(\);\s*", body):
        raise ValueError("Native base outline is no longer a constant shape")
    collision, _ = one([(match, body) for match, body in block_methods if match[1] == outline["shape"] and params(match[3]) == [state] + shape_args and re.fullmatch(r"\s*return\s+this\.\w+\s*\?\s*\w+\.\w+\(\w+,\s*\w+\)\s*:\s*" + re.escape(outline["shape_utility"]) + r"\.\w+\(\);\s*", body)], "base collision delegates to outline when collidable")
    item_block_getter, _ = one([(match, body) for match, body in block_item_methods if match[1] == block and not params(match[3])], "block item block getter")
    steps = [match for match, body in block_item_methods if "static" not in match[0].split("(", 1)[0].split() and (placement_context in params(match[3]) or match is block_use or match is item_block_getter)]
    return {"item_block": item_block, "world": params(use[3])[1], "position": position, "use_context": use_context, "placement_context": placement_context, "result_type": result_type,
            "use": spec(block, use), "replace": spec(block, replace), "material_replaceable": material_replaceable,
            "block_steps": [spec(block, match) for match in (place, survive, shape, collision)], "collision": spec(block, collision),
            "item_use": spec(item, item_use), "item_block_getter": spec(item_block, item_block_getter), "item_steps": [spec(item_block, match) for match in steps],
            "edible": edible[0] if edible else None, "shape": outline["shape"], "to_boxes": outline["to_boxes"], "context": outline["context"], "context_empty": outline.get("context_empty"),
            "box_type": outline["box_type"], "box_fields": outline["box_fields"]}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--symbols", type=Path, required=True)
    parser.add_argument("--outline", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    symbols = json.loads(args.symbols.read_text(encoding="utf-8"))
    tree = args.official / (symbols["version"] + "-client-decompiled")
    paths = {path.stem: path for path in tree.glob("*.java")}
    sources = {name: re.sub(r"/\*.*?\*/", "", path.read_text(encoding="utf-8"), flags=re.S) for name, path in paths.items()}
    outline = json.loads(args.outline.read_text(encoding="utf-8"))["outline_bindings"]
    binding = resolve_sources(symbols, sources, outline)
    used = {symbols[key] for key in ("block", "state", "item", "stack", "player")}
    used.update(binding[key] for key in ("item_block", "world", "position", "use_context", "placement_context", "result_type", "box_type", "shape"))
    used.update((outline["getter"], outline["shape_utility"]))
    if binding["context"]:
        used.add(binding["context"])
    symbols["interaction_bindings"] = binding
    symbols["client_sources"] = {name + ".java": hashlib.sha256(paths[name].read_bytes()).hexdigest() for name in sorted(used)}
    args.out.write_text(json.dumps(symbols, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
