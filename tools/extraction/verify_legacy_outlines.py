"""Check extracted legacy selection boxes against native ray-trace witnesses."""

import argparse
import json
import math
from pathlib import Path


def clip_boxes(boxes: list[list[float]], segment: list[float], farthest_from_end: bool = False) -> list:
    start, end = segment[:3], segment[3:]
    delta = [end[axis] - start[axis] for axis in range(3)]
    selected: list = []
    selected_distance = 0.0 if farthest_from_end else math.inf
    for box in boxes:
        closest: list = []
        distance = math.inf
        for axis, faces in ((0, (4, 5)), (1, (0, 1)), (2, (2, 3))):
            if delta[axis] * delta[axis] < 1.0000000116860974e-7:
                continue
            for side in range(2):
                fraction = (box[axis + 3 * side] - start[axis]) / delta[axis]
                if fraction < 0 or fraction > 1:
                    continue
                point = [start[coordinate] + delta[coordinate] * fraction for coordinate in range(3)]
                if any(point[other] < box[other] or point[other] > box[other + 3] for other in range(3) if other != axis):
                    continue
                candidate_distance = sum((point[coordinate] - start[coordinate]) ** 2 for coordinate in range(3))
                if candidate_distance < distance:
                    distance = candidate_distance
                    closest = [faces[side], point]
        if not closest:
            continue
        distance = sum((closest[1][axis] - (end[axis] if farthest_from_end else start[axis])) ** 2 for axis in range(3))
        closer = distance > selected_distance if farthest_from_end else distance < selected_distance
        if closer:
            selected, selected_distance = closest, distance
    return selected


def verify(rows: dict, oracle: dict) -> int:
    if set(rows) != set(oracle["hits"]):
        raise ValueError("Native witness states differ from selection measurements")
    count = 0
    failures = []
    for state, row in rows.items():
        shape = row["outline"]
        offset = shape.get("native_origin_offset", [0, 0, 0])
        boxes = [[coordinate + offset[index % 3] for index, coordinate in enumerate(box)] for box in shape["boxes"]]
        hits = oracle["hits"][state]
        if len(hits) != len(oracle["segments"]):
            raise ValueError(f"Incomplete native witnesses for state {state}")
        for index, (segment, expected) in enumerate(zip(oracle["segments"], hits, strict=True)):
            actual = clip_boxes(boxes, segment, row.get("trace_selection") == "farthest_from_end")
            if bool(actual) != bool(expected) or actual and (actual[0] != expected[0]
                    or any(not math.isclose(a, b, rel_tol=0, abs_tol=1e-12) for a, b in zip(actual[1], expected[1], strict=True))):
                failures.append(f"state {state} {row.get('block', '')}, ray {index}: expected {expected}, got {actual}")
            count += 1
    if failures:
        raise ValueError(f"{len(failures)} native ray witness mismatches:\n" + "\n".join(failures[:20]))
    return count


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("outlines", type=Path)
    parser.add_argument("witnesses", type=Path)
    args = parser.parse_args()
    count = verify(json.loads(args.outlines.read_text()), json.loads(args.witnesses.read_text()))
    print(f"Verified {count} native legacy ray witnesses")


if __name__ == "__main__":
    main()
