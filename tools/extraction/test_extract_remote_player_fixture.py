import hashlib
import json
import unittest

from test_verify_remote_player import fixture


class RemotePlayerFixtureTests(unittest.TestCase):
    def test_preserves_aliases_and_deduplicates_measured_cases(self) -> None:
        from extract_remote_player_fixture import extract

        contents, reports, profiles = inputs()
        result = extract(reports, {"1.16.4", "1.16.5"}, lambda _: contents, profiles)
        self.assertEqual(8, len(result["cases"]))
        self.assertEqual(2, len(result["releases"]))
        self.assertEqual(3, result["releases"][0]["position_steps"])
        self.assertEqual(result["releases"][0]["cases"], result["releases"][1]["cases"])

    def test_rejects_missing_failed_changed_and_unequal_aliases(self) -> None:
        from extract_remote_player_fixture import extract

        for defect in ("missing", "failed", "hash", "alias", "profile"):
            contents, reports, profiles = inputs(); other = contents
            if defect == "missing": reports.pop()
            elif defect == "failed": reports[0]["status"] = "failed"
            elif defect == "hash": reports[0]["measurement_sha256"] = "0" * 64
            elif defect == "profile": profiles.pop("1.16.4")
            else:
                raw = fixture(); raw["cases"][0]["ticks"][0]["after"]["rotation"][0] = 1
                other = json.dumps(raw).encode(); reports[1]["measurement_sha256"] = hashlib.sha256(other).hexdigest()
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                extract(reports, {"1.16.4", "1.16.5"}, lambda version: other if version == "1.16.5" else contents, profiles)


def inputs() -> tuple[bytes, list[dict], dict[str, dict]]:
    contents = json.dumps(fixture()).encode()
    reports = [{"version": version, "protocol": 754, "status": "complete", "measurement_sha256": hashlib.sha256(contents).hexdigest(), "client_sha256": "1" * 64, "server_sha256": "2" * 64} for version in ("1.16.4", "1.16.5")]
    profiles = {row["version"]: {"protocol": 754, "trace_group": "1.14.4"} for row in reports}
    return contents, reports, profiles


if __name__ == "__main__":
    unittest.main()
