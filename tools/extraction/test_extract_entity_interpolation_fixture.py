import hashlib
import json
import unittest

from test_verify_entity_interpolation import fixture
from verify_entity_interpolation import CASES


class EntityInterpolationFixtureTests(unittest.TestCase):
    def test_identical_alias_cases_are_deduplicated_with_all_release_rows(self) -> None:
        from extract_entity_interpolation_fixture import extract

        raw, rows = inputs()
        result = extract(rows, {"1.16.4", "1.16.5"}, lambda version: raw)
        self.assertEqual(16, len(result["cases"]))
        self.assertEqual(2, len(result["releases"]))
        self.assertEqual(result["releases"][0]["cases"], result["releases"][1]["cases"])

    def test_missing_releases_failed_runs_and_changed_measurements_are_rejected(self) -> None:
        from extract_entity_interpolation_fixture import extract

        for defect in ("missing", "duplicate", "failed", "hash"):
            raw, rows = inputs()
            if defect == "missing":
                rows.pop()
            elif defect == "duplicate":
                rows[1]["version"] = rows[0]["version"]
            elif defect == "failed":
                rows[0]["status"] = "failed"
            else:
                rows[0]["measurement_sha256"] = "0" * 64
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                extract(rows, {"1.16.4", "1.16.5"}, lambda version: raw)


def inputs() -> tuple[bytes, list[dict]]:
    data = json.dumps(fixture(CASES)).encode()
    rows = [{"version": version, "protocol": 754, "status": "complete", "measurement_sha256": hashlib.sha256(data).hexdigest(), "client_sha256": "1" * 64, "server_sha256": "2" * 64, "native_client_classes": {}, "source_audit": {}} for version in ("1.16.4", "1.16.5")]
    return data, rows


if __name__ == "__main__":
    unittest.main()
