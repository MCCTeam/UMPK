import copy
import hashlib
import json
import unittest


def fixture() -> dict:
    return {"operation": "PositionMoveRotation.calculateAbsolute", "sine_sha256": "a" * 64, "inputs": [{"source": [1.0] * 8, "change": [2.0] * 8} for _ in range(8)], "rows": [{"input": index, "flags": flags, "result": [3.0] * 8} for index in range(8) for flags in range(512)]}


class EntityTeleportMeasurementTests(unittest.TestCase):
    def test_requires_complete_finite_unique_native_cases(self) -> None:
        from measure_entity_teleport import verify

        verify(fixture())
        for defect in ("missing", "duplicate", "nonfinite", "shape", "flags", "operation"):
            raw = fixture()
            if defect == "missing": raw["rows"].pop()
            elif defect == "duplicate": raw["rows"][-1] = copy.deepcopy(raw["rows"][0])
            elif defect == "nonfinite": raw["rows"][0]["result"][0] = float("nan")
            elif defect == "shape": raw["inputs"][0]["source"].pop()
            elif defect == "flags": raw["rows"][0]["flags"] = 512
            else: raw["operation"] = "other"
            with self.subTest(defect=defect), self.assertRaises(ValueError): verify(raw)

    def test_fixture_requires_hashes_complete_releases_and_equal_aliases(self) -> None:
        from measure_entity_teleport import extract

        contents = json.dumps(fixture()).encode()
        rows = [{"version": name, "protocol": 768, "status": "complete", "measurement_sha256": hashlib.sha256(contents).hexdigest()} for name in ("1.21.2", "1.21.3")]
        result = extract(rows, {"1.21.2", "1.21.3"}, lambda _: contents)
        self.assertEqual(1, len(result["groups"]))
        for defect in ("missing", "duplicate", "failed", "hash", "alias"):
            broken = copy.deepcopy(rows)
            other = contents
            if defect == "missing": broken.pop()
            elif defect == "duplicate": broken.append(copy.deepcopy(rows[0]))
            elif defect == "failed": broken[0]["status"] = "failed"
            elif defect == "hash": broken[0]["measurement_sha256"] = "0" * 64
            else:
                altered = fixture(); altered["rows"][0]["result"][0] = 4
                other = json.dumps(altered).encode(); broken[1]["measurement_sha256"] = hashlib.sha256(other).hexdigest()
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                extract(broken, {"1.21.2", "1.21.3"}, lambda name: other if name == "1.21.3" else contents)


if __name__ == "__main__":
    unittest.main()
