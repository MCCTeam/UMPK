"""Verify native entity pose-change, explicit refresh and position-reapplication witnesses."""

import argparse
import json
import math
from pathlib import Path
import struct


def finite(values: object, count: int) -> list[float]:
    if not isinstance(values, list) or len(values) != count or any(type(value) not in (int, float) or not math.isfinite(value) for value in values):
        raise ValueError("Native entity geometry has invalid or non-finite coordinates")
    return values


def size(values: object) -> tuple[float, float, bool]:
    if not isinstance(values, list) or len(values) != 3 or type(values[2]) is not bool:
        raise ValueError("Native entity dimensions are incomplete")
    dimensions = finite(values[:2], 2)
    try:
        width, height = (struct.unpack("!f", struct.pack("!f", value))[0] for value in dimensions)
    except OverflowError as error:
        raise ValueError("Native entity dimensions exceed float range") from error
    if not 0 < width < math.inf or not 0 < height < math.inf:
        raise ValueError("Native player dimensions must be positive and finite")
    return width, height, values[2]


def centered(position: list[float], dimensions: tuple[float, float, bool]) -> list[float]:
    x, y, z = position
    width, height, _ = dimensions
    half = width / 2
    return [x - half, y, z - half, x + half, y + height, z + half]


def refresh(before: list[float], old_width: float, dimensions: tuple[float, float, bool], position: list[float], retained: bool) -> list[float]:
    width, height, _ = dimensions
    if not retained or width < old_width:
        return centered(position, dimensions)
    x, y, z = before[:3]
    return [x, y, z, x + width, y + height, z + width]


def verify(raw: dict) -> dict:
    poses, positions, rows = raw["poses"], raw["positions"], raw["rows"]
    if not isinstance(poses, list) or len(poses) < 2 or any(not isinstance(pose, str) or not pose for pose in poses) or len(set(poses)) != len(poses) or not positions:
        raise ValueError("Incomplete native pose or position coverage")
    for position in positions:
        finite(position, 3)
    expected = {(source, target, position) for source in range(len(poses)) for target in range(len(poses)) for position in range(len(positions))}
    seen, dimensions, matches = set(), {}, {False: True, True: True}
    growth = shrink = 0
    for row in rows:
        key = (row["from"], row["to"], row["position"])
        if any(type(index) is not int for index in key) or key not in expected or key in seen:
            raise ValueError("Duplicate or invalid native transition witness")
        seen.add(key)
        source, target, position = key
        old_size, new_size = size(row["from_size"]), size(row["to_size"])
        for pose, measured in ((source, old_size), (target, new_size)):
            if dimensions.setdefault(pose, measured) != measured:
                raise ValueError("Native dimensions differ for the same pose")
        before = finite(row["before"], 6)
        if before != centered(positions[position], old_size):
            raise ValueError("Native transition did not start from a recentered box")
        for name in ("after_sync", "after_refresh", "after_position"):
            finite(row[name], 6)
        if row["after_position"] != centered(positions[position], new_size):
            raise ValueError("Native position update did not reapply dimensions")
        for retained in matches:
            synchronized = refresh(before, old_size[0], new_size, positions[position], retained) if source != target else before
            refreshed = refresh(synchronized, new_size[0], new_size, positions[position], retained)
            matches[retained] &= row["after_sync"] == synchronized and row["after_refresh"] == refreshed
        growth += int(new_size[0] > old_size[0])
        shrink += int(new_size[0] < old_size[0])
    if seen != expected:
        raise ValueError("Native transitions do not cover every pose pair and position")
    if sum(matches.values()) != 1:
        raise ValueError("Native refresh witnesses must establish exactly one geometry profile")
    retained = matches[True]
    return {"profile": "retain_minimum_on_growth" if retained else "recenter_on_refresh", "witnesses": len(rows), "poses": len(poses), "positions": len(positions), "width_growth": growth, "width_shrink": shrink}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("measurement", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(json.loads(args.measurement.read_text(encoding="utf-8"))), indent=2))


if __name__ == "__main__":
    main()
