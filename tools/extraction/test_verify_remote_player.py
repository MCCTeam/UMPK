import copy
import unittest


def fixture() -> dict:
    from verify_remote_player import ACTOR, CASES

    cases = []
    for name in CASES:
        destination = [1.5, 64.0, 1.5] if name == "stationary" else [-2.5, 65.0, 3.5] if name == "retarget" else [4.5, 67.0, -1.5]
        velocity = [-0.15, 0.4, -0.35] if name == "motion_retarget" else [0.3, -0.2, 0.7] if name == "motion" else [0.0, 0.0, 0.0]
        ticks = []
        for tick in range(12):
            height = 1.5 if name == "pose" and tick >= 1 else 1.8
            state = {"position": destination, "rotation": [0.0, 0.0], "box": [0.0, 0.0, 0.0, 0.6, height, 0.6], "target": [*destination, 0.0, 0.0], "active": tick < 2 and name != "stationary"}
            ticks.append({"tick": tick, "operations": [], "before": copy.deepcopy(state), "after": copy.deepcopy(state), "velocity": velocity})
        cases.append({"name": name, "ticks": ticks})
    return {"actor": ACTOR, "scope": "controlled native remote steps", "cases": cases}


class RemotePlayerVerifierTests(unittest.TestCase):
    def test_accepts_complete_traces_with_a_stationary_control_and_pose_change(self) -> None:
        from verify_remote_player import verify

        result = verify(fixture())
        self.assertEqual(96, result["ticks"])
        self.assertEqual(3, result["position_steps"])

    def test_rejects_incomplete_nonfinite_or_replaced_actor_traces(self) -> None:
        from verify_remote_player import verify

        for defect in ("actor", "case", "duplicate", "tick", "nonfinite", "box", "unknown_operation"):
            raw = fixture()
            if defect == "actor": raw["actor"] = "owned survival Player subtype"
            elif defect == "case": raw["cases"].pop()
            elif defect == "duplicate": raw["cases"][1] = copy.deepcopy(raw["cases"][0])
            elif defect == "tick": raw["cases"][0]["ticks"][0]["tick"] = 1
            elif defect == "nonfinite": raw["cases"][0]["ticks"][0]["velocity"][0] = float("nan")
            elif defect == "box": raw["cases"][0]["ticks"][0]["after"]["box"][3] = -1
            else: raw["cases"][0]["ticks"][0]["operations"] = ["unmeasured"]
            with self.subTest(defect=defect), self.assertRaises(ValueError): verify(raw)

    def test_rejects_missing_movement_velocity_or_pose_effects(self) -> None:
        from verify_remote_player import verify

        for name, field in (("linear", "position"), ("motion", "velocity"), ("pose", "box")):
            raw = fixture(); case = next(row for row in raw["cases"] if row["name"] == name)
            if field == "position": case["ticks"][-1]["after"][field] = [1.5, 64.0, 1.5]
            elif field == "velocity": case["ticks"][-1][field] = [0.0, 0.0, 0.0]
            else:
                for row in case["ticks"]: row["after"][field][4] = 1.8
            with self.subTest(field=field), self.assertRaises(ValueError): verify(raw)


if __name__ == "__main__":
    unittest.main()
