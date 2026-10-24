import copy
import unittest


class EntityInterpolationVerifierTests(unittest.TestCase):
    def test_edge_traces_reject_unknown_operations(self) -> None:
        from verify_entity_interpolation import verify_edges

        raw = edge_fixture()
        self.assertEqual(368, verify_edges(raw)["ticks"])
        raw["cases"][0]["ticks"][0]["operations"][0]["kind"] = "unmeasured"
        with self.assertRaises(ValueError):
            verify_edges(raw)

    def test_complete_but_stationary_controller_is_rejected(self) -> None:
        from verify_entity_interpolation import CASES, verify

        raw = fixture(CASES)
        raw["cases"][0]["ticks"][2]["after"]["position"] = [1.5, 64.0, 1.5]
        with self.assertRaises(ValueError):
            verify(raw)

    def test_complete_native_trace_is_accepted(self) -> None:
        from verify_entity_interpolation import CASES, verify

        raw = fixture(CASES)
        result = verify(raw)
        self.assertEqual(len(CASES) * 12, result["ticks"])
        self.assertEqual("living_inline", result["profile"])

    def test_missing_duplicate_nonfinite_and_unproven_traces_are_rejected(self) -> None:
        from verify_entity_interpolation import CASES, verify

        for defect in ("missing_case", "duplicate_case", "missing_tick", "bad_tick", "nonfinite", "box", "factory", "boundary"):
            raw = fixture(CASES)
            if defect == "missing_case":
                raw["cases"].pop()
            elif defect == "duplicate_case":
                raw["cases"].append(copy.deepcopy(raw["cases"][0]))
            elif defect == "missing_tick":
                raw["cases"][0]["ticks"].pop()
            elif defect == "bad_tick":
                raw["cases"][0]["ticks"][0]["tick"] = 2
            elif defect == "nonfinite":
                raw["cases"][0]["ticks"][0]["after"]["position"][0] = float("nan")
            elif defect == "box":
                raw["cases"][0]["ticks"][0]["after"]["box"][3] = -10
            elif defect == "factory":
                raw["entity_profiles"]["minecraft:cow"] = {"error": "factory failed"}
            elif defect == "boundary":
                raw["legacy_tick_boundary"] = "guessed"
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                verify(raw)


def fixture(names) -> dict:
    state = {"position": [1.5, 64.0, 1.5], "rotation": [0.0, 0.0], "box": [1.2, 64.0, 1.2, 1.8, 65.8, 1.8], "target": [1.5, 64.0, 1.5, 0.0, 0.0], "active": False}
    raw = {"schema": 1, "profile": "living_inline", "legacy_tick_boundary": "first_velocity_set_after_interpolation", "partial_updates": False, "stepped_paths": False, "entity_profiles": {"minecraft:cow": {"class": "Cow", "controller": "LivingEntity"}}, "cases": [{"name": name, "ticks": [{"tick": tick, "before": copy.deepcopy(state), "after": copy.deepcopy(state), "collision_queries": []} for tick in range(12)]} for name in names]}
    for case in raw["cases"]:
        if case["name"] in ("linear", "length_one", "external_clear", "external_blocked"):
            for row in case["ticks"]:
                step = 3 if case["name"] == "length_one" else min(row["tick"] + 1, 3)
                row["after"]["position"] = [1.5 + step, 64.0 + step, 1.5 - step]
    return raw


def edge_fixture() -> dict:
    from verify_entity_interpolation import CASES

    raw = fixture(CASES)
    raw["cases"] = []
    state = {"position": [4.5, 67.0, -1.5], "rotation": [90.0, 30.0], "box": [4.2, 67.0, -1.8, 4.8, 68.8, -1.2], "target": [4.5, 67.0, -1.5, 90.0, 30.0], "active": False}
    for name in ("native_default", "fractional_rotation", "external_rotation", "pose_during_interpolation", "burst"):
        count = 320 if name == "burst" else 12
        ticks = [{"tick": tick, "operations": [], "before": copy.deepcopy(state), "after": copy.deepcopy(state), "collision_queries": []} for tick in range(count)]
        ticks[0]["operations"] = [{"kind": "target", "position": [4.5, 67.0, -1.5], "yaw": 90.0, "pitch": 30.0}]
        raw["cases"].append({"name": name, "length": 3, "ticks": ticks})
    return raw


if __name__ == "__main__":
    unittest.main()
