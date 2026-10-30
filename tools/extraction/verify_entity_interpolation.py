"""Validate complete native living-interpolation traces without treating a partial run as evidence."""

import argparse
import json
import math
from pathlib import Path

CASES = ("linear", "repeat", "retarget", "length_zero", "length_one", "length_six", "cancel", "external_clear", "external_blocked", "small_external_low", "small_external_high", "world_border", "wrap_rotation", "speed_half", "speed_double", "speed_zero")


def finite_vector(value: object, length: int) -> bool:
    return isinstance(value, list) and len(value) == length and all(type(axis) in (int, float) and math.isfinite(axis) for axis in value)


def verify(raw: dict) -> dict:
    profile = raw.get("profile")
    if raw.get("schema") != 1 or profile not in ("living_inline", "living_handler", "living_steps"):
        raise ValueError("Missing native interpolation profile")
    boundary = "first_velocity_set_after_interpolation" if profile == "living_inline" else "not_used"
    if raw.get("legacy_tick_boundary") != boundary or type(raw.get("partial_updates")) is not bool or type(raw.get("stepped_paths")) is not bool:
        raise ValueError("Unproven native tick boundary or capability")
    if raw["stepped_paths"] != (profile == "living_steps") or profile == "living_inline" and raw["partial_updates"]:
        raise ValueError("Native controller capability contradicts its profile")
    names = list(CASES)
    if raw["partial_updates"]:
        names += ["partial_position", "partial_rotation"]
    if raw["stepped_paths"]:
        names += ["stepped_path", "queued_path", "predicted_clear", "predicted_blocked", "predicted_small"]
    rows = raw.get("cases", [])
    if [row.get("name") for row in rows] != names:
        raise ValueError("Native interpolation cases are missing, duplicated or out of order")
    entities = raw.get("entity_profiles", {})
    if not entities or any(not isinstance(row, dict) or "error" in row or not row.get("class") or not row.get("controller") for row in entities.values()):
        raise ValueError("Missing native entity interpolation factory or controller")
    queries = 0
    for case in rows:
        ticks = case.get("ticks", [])
        if len(ticks) != 12 or [row.get("tick") for row in ticks] != list(range(12)):
            raise ValueError("Native interpolation tick trace is incomplete")
        for row in ticks:
            for phase in ("before", "after"):
                state = row.get(phase, {})
                if any(not finite_vector(state.get(key), length) for key, length in (("position", 3), ("rotation", 2), ("box", 6), ("target", 5))) or type(state.get("active")) is not bool:
                    raise ValueError("Native interpolation state is nonfinite or incomplete")
                box = state["box"]
                if any(box[axis] > box[axis + 3] for axis in range(3)):
                    raise ValueError("Native interpolation box is inverted")
            collision_queries = row.get("collision_queries")
            if not isinstance(collision_queries, list) or any(not finite_vector(box, 6) for box in collision_queries):
                raise ValueError("Native interpolation collision query is invalid")
            queries += len(collision_queries)
    by_name = {row["name"]: row["ticks"] for row in rows}
    start, end = [1.5, 64.0, 1.5], [4.5, 67.0, -1.5]
    linear = by_name["linear"]
    if linear[0]["before"]["position"] != start or linear[2]["after"]["position"] != end or linear[-1]["after"]["position"] != end:
        raise ValueError("Native linear controller did not reach its declared target in three ticks")
    if any(not min(start[axis], end[axis]) < linear[0]["after"]["position"][axis] < max(start[axis], end[axis]) for axis in range(3)):
        raise ValueError("Native linear controller did not advance between its endpoints")
    if by_name["length_one"][0]["after"]["position"] != end or by_name["length_zero"][0]["after"]["position"] != (start if profile == "living_inline" else end):
        raise ValueError("Native zero/one-length controller boundary differs")
    cancelled = by_name["cancel"][1]["before"]["position"]
    if any(row["after"]["position"] != cancelled for row in by_name["cancel"][1:]):
        raise ValueError("Cancelled native interpolation continued moving")
    shifted = [4.75, 67.0, -1.5] if profile != "living_inline" else end
    if by_name["external_clear"][-1]["after"]["position"] != shifted or by_name["external_blocked"][-1]["after"]["position"] != end:
        raise ValueError("Native external-movement collision control did not select its declared branch")
    if profile == "living_steps":
        stopped = by_name["speed_zero"][0]["after"]["position"]
        if any(row["after"]["position"] != stopped or not row["after"]["active"] for row in by_name["speed_zero"]):
            raise ValueError("Native zero-tick-speed path unexpectedly advanced")
    return {"profile": profile, "entities": len(entities), "cases": len(rows), "ticks": len(rows) * 12, "collision_queries": queries,
            "duplicate_targets_restart": by_name["repeat"][1]["after"]["position"] != linear[1]["after"]["position"],
            "external_collision_controls": True}


def verify_edges(raw: dict) -> dict:
    profile = raw.get("profile")
    names = ["native_default", "fractional_rotation", "external_rotation", "pose_during_interpolation", "burst"]
    if raw.get("schema") != 1 or profile not in ("living_inline", "living_handler", "living_steps") or [row.get("name") for row in raw.get("cases", [])] != names:
        raise ValueError("Native interpolation edge suite is incomplete")
    expected_boundary = "first_velocity_set_after_interpolation" if profile == "living_inline" else "not_used"
    if raw.get("legacy_tick_boundary") != expected_boundary:
        raise ValueError("Native edge tick boundary is unproven")
    queries = 0
    total = 0
    for case in raw["cases"]:
        count = 320 if case["name"] == "burst" else 12
        ticks = case.get("ticks", [])
        if type(case.get("length")) is not int or not 0 < case["length"] <= 10 or len(ticks) != count or [row.get("tick") for row in ticks] != list(range(count)):
            raise ValueError("Native edge trace or default length is invalid")
        last_target = None
        for row in ticks:
            for phase in ("before", "after"):
                state = row.get(phase, {})
                if any(not finite_vector(state.get(key), size) for key, size in (("position", 3), ("rotation", 2), ("box", 6), ("target", 5))) or type(state.get("active")) is not bool:
                    raise ValueError("Native edge state is incomplete or nonfinite")
            operations = row.get("operations")
            if not isinstance(operations, list):
                raise ValueError("Native edge input operations are absent")
            for op in operations:
                kind = op.get("kind")
                if kind not in ("target", "rotation", "dimensions"):
                    raise ValueError("Native edge operation is unmeasured")
                fields = ("width", "height") if kind == "dimensions" else ("yaw", "pitch")
                if any(type(op.get(key)) not in (int, float) or not math.isfinite(op[key]) for key in fields):
                    raise ValueError("Native edge input is nonfinite")
                if kind == "target":
                    if not finite_vector(op.get("position"), 3):
                        raise ValueError("Native target position is invalid")
                    last_target = op["position"]
                if kind == "dimensions" and any(op[key] <= 0 for key in fields):
                    raise ValueError("Native dimensions must be positive")
            boxes = row.get("collision_queries")
            if not isinstance(boxes, list) or any(not finite_vector(box, 6) for box in boxes):
                raise ValueError("Native edge collision queries are invalid")
            queries += len(boxes)
        if case["name"] in ("native_default", "burst") and (last_target is None or ticks[-1]["after"]["position"] != last_target or ticks[-1]["after"]["active"]):
            raise ValueError("Native default or sustained stream failed to settle at its final target")
        total += len(ticks)
    return {"profile": profile, "cases": len(names), "ticks": total, "collision_queries": queries, "native_length": raw["cases"][0]["length"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("measurement", type=Path)
    args = parser.parse_args()
    raw = json.loads(args.measurement.read_text())
    print(json.dumps(verify_edges(raw) if raw.get("suite") == "edges" else verify(raw), indent=2))
