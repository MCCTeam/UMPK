import copy
import struct
import unittest


class EntityBoxTransitionTests(unittest.TestCase):
    def test_metadata_refresh_distinguishes_retained_minimum_from_recentered_boxes(self) -> None:
        from verify_entity_box_transitions import verify

        for retained in (False, True):
            raw = fixture(retained)
            result = verify(raw)
            self.assertEqual("retain_minimum_on_growth" if retained else "recenter_on_refresh", result["profile"])
            self.assertEqual(4, result["witnesses"])
            self.assertEqual(1, result["width_growth"])

    def test_missing_duplicate_nonfinite_and_wrong_refresh_witnesses_are_rejected(self) -> None:
        from verify_entity_box_transitions import verify

        for defect in ("missing", "duplicate", "nonfinite", "wrong_sync", "wrong_refresh", "wrong_position"):
            raw = fixture(True)
            if defect == "missing":
                raw["rows"].pop()
            elif defect == "duplicate":
                raw["rows"].append(copy.deepcopy(raw["rows"][0]))
            elif defect == "nonfinite":
                raw["rows"][0]["before"][0] = float("nan")
            else:
                key = {"wrong_sync": "after_sync", "wrong_refresh": "after_refresh", "wrong_position": "after_position"}[defect]
                raw["rows"][0][key][0] += 0.125
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                verify(raw)


def fixture(retained: bool) -> dict:
    f32 = lambda value: struct.unpack("!f", struct.pack("!f", value))[0]
    widths = [f32(0.2), f32(0.6)]
    heights = [f32(0.2), f32(1.8)]
    rows = []
    for source in range(2):
        for target in range(2):
            before = [1.5 - widths[source] / 2, 64.0, 1.5 - widths[source] / 2, 1.5 + widths[source] / 2, 64.0 + heights[source], 1.5 + widths[source] / 2]
            centered = [1.5 - widths[target] / 2, 64.0, 1.5 - widths[target] / 2, 1.5 + widths[target] / 2, 64.0 + heights[target], 1.5 + widths[target] / 2]
            updated = centered.copy()
            if retained and widths[target] >= widths[source]:
                updated = before[:3] + [before[0] + widths[target], before[1] + heights[target], before[2] + widths[target]]
            rows.append({"from": source, "to": target, "position": 0, "from_size": [widths[source], heights[source], source == 0], "to_size": [widths[target], heights[target], target == 0], "before": before, "after_sync": updated.copy() if source != target else before.copy(), "after_refresh": updated.copy(), "after_position": centered.copy()})
    return {"poses": ["SLEEPING", "STANDING"], "positions": [[1.5, 64.0, 1.5]], "rows": rows}


if __name__ == "__main__":
    unittest.main()
