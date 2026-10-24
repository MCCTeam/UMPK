"""Check an exact native collision-grid dump before converting it into data."""

import argparse
import json
import math
from pathlib import Path


def number(value: object) -> bool:
    return type(value) in (int, float) and math.isfinite(value)


def verify(document: object, *, offsets: bool = False) -> None:
    if not isinstance(document, dict) or type(document.get("schema")) is not int or document["schema"] != 1:
        raise ValueError("expected collision-grid schema 1")
    if document.get("cell_order") != "(x * y_size + y) * z_size + z":
        raise ValueError("unknown collision-grid cell order")
    states = document.get("states")
    groups = document.get("groups")
    if not isinstance(states, dict) or not states or not isinstance(groups, list) or not groups:
        raise ValueError("the state registry and shape groups must be nonempty")
    if offsets:
        count = document.get("state_count")
        if type(count) is not int or count <= 0 or any(
            not isinstance(key, str) or not key.isascii() or not key.isdecimal()
            or str(int(key)) != key or not 0 <= int(key) < count for key in states
        ):
            raise ValueError("offset state ids must be canonical decimal keys within the native registry")
    elif set(states) != {str(index) for index in range(len(states))}:
        raise ValueError("state ids must be contiguous canonical decimal keys starting at zero")
    if any(type(group) is not int or group < 0 or group >= len(groups) for group in states.values()):
        raise ValueError("a state refers to an absent shape group")
    if set(states.values()) != set(range(len(groups))):
        raise ValueError("the dump contains an unreferenced shape group")
    for index, group in enumerate(groups):
        if not isinstance(group, dict) or type(group.get("canonical_full_block")) is not bool:
            raise ValueError(f"group {index}: canonical full-block identity must be boolean")
        if offsets:
            limit = group.get("max_horizontal_offset")
            if not number(limit) or limit <= 0 or group["canonical_full_block"] or not group.get("cells"):
                raise ValueError(f"group {index}: offset grids require occupied cells, positive finite offset limits and noncanonical identity")
        if not isinstance(group.get("planes"), list) or len(group["planes"]) != 3:
            raise ValueError(f"group {index}: expected three coordinate-plane lists")
        planes = group["planes"]
        if all(isinstance(axis, list) and not axis for axis in planes):
            if group["canonical_full_block"] or group.get("cells") != [] or group.get("boxes") != []:
                raise ValueError(f"group {index}: absent coordinate planes require an empty noncanonical shape")
            continue
        for axis in planes:
            if not isinstance(axis, list) or not axis or any(not number(value) for value in axis):
                raise ValueError(f"group {index}: coordinates must be finite numbers")
            if any(low > high for low, high in zip(axis, axis[1:])):
                raise ValueError(f"group {index}: coordinate planes must not run backwards")
        nx, ny, nz = (len(axis) - 1 for axis in planes)
        cells = group.get("cells")
        if not isinstance(cells, list) or any(type(cell) is not int or cell < 0 or cell >= nx * ny * nz for cell in cells):
            raise ValueError(f"group {index}: occupied cells must be integer indices inside the grid")
        if cells != sorted(set(cells)):
            raise ValueError(f"group {index}: occupied cells must be unique and ascending")
        occupied = set(cells)
        boxes = group.get("boxes")
        if not isinstance(boxes, list):
            raise ValueError(f"group {index}: expected a box list")
        for box in boxes:
            if not isinstance(box, list) or len(box) != 6 or any(not number(value) for value in box):
                raise ValueError(f"group {index}: each box must have six finite coordinates")
            if any(box[axis] > box[axis + 3] for axis in range(3)):
                raise ValueError(f"group {index}: collision boxes must not have negative extent")
            if any(box[axis] not in planes[axis % 3] for axis in range(6)):
                raise ValueError(f"group {index}: box boundaries must coincide with grid planes")
        if group["canonical_full_block"] and (planes != [[0, 1], [0, 1], [0, 1]] or cells != [0] or boxes != [[0, 0, 0, 1, 1, 1]]):
            raise ValueError(f"group {index}: canonical full block must contain exactly the native unit cube")
        covered_boxes = [False] * len(boxes)
        for x in range(nx):
            for y in range(ny):
                for z in range(nz):
                    bounds = [(axis[at], axis[at + 1]) for axis, at in zip(planes, (x, y, z))]
                    covers = [all(box[axis] <= low and high <= box[axis + 3] for axis, (low, high) in enumerate(bounds)) for box in boxes]
                    cell = (x * ny + y) * nz + z
                    positive_volume = all(low < high for low, high in bounds)
                    if cell in occupied:
                        if not any(covers):
                            raise ValueError(f"group {index}: occupied cell {cell} is absent from the boxes")
                        covered_boxes = [before or current for before, current in zip(covered_boxes, covers)]
                    elif positive_volume and any(covers):
                        raise ValueError(f"group {index}: cell {cell} disagrees with the box volume")
        if not all(covered_boxes):
            raise ValueError(f"group {index}: a collision box contains no occupied cell")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump", type=Path)
    parser.add_argument("--offsets", action="store_true", help="check a sparse registry supplement with native position-offset grids")
    args = parser.parse_args()
    try:
        document = json.loads(args.dump.read_text(encoding="utf-8"))
        verify(document, offsets=args.offsets)
    except (OSError, ValueError) as error:
        parser.error(str(error))
    print(f"Verified {len(document['states'])} states in {len(document['groups'])} collision-grid groups")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
