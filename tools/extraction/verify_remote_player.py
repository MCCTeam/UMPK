"""Check complete controlled native RemotePlayer movement, velocity and pose traces."""

import argparse
import json
import math
from pathlib import Path

ACTOR = "owned RemotePlayer subtype; only survival game-mode queries overridden"
CASES = ("linear", "repeat", "retarget", "motion", "motion_retarget", "wrap_rotation", "pose", "stationary")
OPERATIONS = {"target", "repeat", "retarget", "motion", "motion_retarget", "crouch"}


def vector(value: object, size: int) -> bool:
    return isinstance(value, list) and len(value) == size and all(type(number) in (int, float) and math.isfinite(number) for number in value)


def verify(raw: dict) -> dict:
    if raw.get("actor") != ACTOR or not isinstance(raw.get("scope"), str):
        raise ValueError("The native remote-player actor or scope is unproven")
    cases = raw.get("cases", [])
    if len(cases) != len(CASES) or {row.get("name") for row in cases} != set(CASES):
        raise ValueError("Native remote-player scenarios are missing or duplicated")
    by_name = {}
    for case in cases:
        ticks = case.get("ticks", [])
        if len(ticks) != 12:
            raise ValueError("Native remote-player ticks are incomplete")
        for index, row in enumerate(ticks):
            if row.get("tick") != index or not isinstance(row.get("operations"), list) or any(operation not in OPERATIONS for operation in row["operations"]):
                raise ValueError("Unknown or unordered native remote-player input")
            if not vector(row.get("velocity"), 3):
                raise ValueError("Invalid native remote-player velocity")
            for key in ("before", "after"):
                state = row.get(key, {})
                if not vector(state.get("position"), 3) or not vector(state.get("rotation"), 2) or not vector(state.get("box"), 6) or not vector(state.get("target"), 5) or type(state.get("active")) is not bool:
                    raise ValueError("Invalid native remote-player state")
                box = state["box"]
                if any(box[axis + 3] <= box[axis] for axis in range(3)):
                    raise ValueError("Inverted native remote-player bounds")
        destination = [1.5, 64.0, 1.5] if case["name"] == "stationary" else [-2.5, 65.0, 3.5] if case["name"] == "retarget" else [4.5, 67.0, -1.5]
        if ticks[-1]["after"]["position"] != destination or ticks[-1]["after"]["active"]:
            raise ValueError("The native remote-player target did not settle")
        expected_velocity = [-0.15, 0.4, -0.35] if case["name"] == "motion_retarget" else [0.3, -0.2, 0.7] if case["name"] == "motion" else [0.0, 0.0, 0.0]
        if any(abs(actual - expected) > 1.0e-12 for actual, expected in zip(ticks[-1]["velocity"], expected_velocity)):
            raise ValueError("The native remote-player velocity did not settle")
        by_name[case["name"]] = ticks
    if any(row["after"]["position"] != [1.5, 64.0, 1.5] or row["after"]["active"] for row in by_name["stationary"]):
        raise ValueError("The stationary native control moved")
    pose = by_name["pose"]
    first_height = pose[0]["after"]["box"][4] - pose[0]["after"]["box"][1]
    last_height = pose[-1]["after"]["box"][4] - pose[-1]["after"]["box"][1]
    if not 0 < last_height < first_height:
        raise ValueError("The native remote-player crouch did not change bounds")
    settled = next((index + 1 for index, row in enumerate(by_name["linear"]) if not row["after"]["active"]), None)
    if settled not in (2, 3):
        raise ValueError("Unexpected native remote-player position step count")
    return {"cases": len(cases), "ticks": len(cases) * 12, "position_steps": settled, "standing_height": first_height, "crouching_height": last_height}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("measurement", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(json.loads(args.measurement.read_text())), sort_keys=True))


if __name__ == "__main__":
    main()
