"""Keep native transition phases and incomplete geometry evidence distinct."""

import copy
import json
from pathlib import Path
import unittest

from extract_entity_geometry import legacy_villager_profile, villager_profile


class VillagerGeometryExtractionTests(unittest.TestCase):
    def setUp(self) -> None:
        path = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.14.4.json"
        self.raw = json.loads(path.read_text(encoding="utf-8"))

    def role_native(self, entity: str) -> dict:
        path = Path(__file__).parents[2] / "tests/Umpk.Client.Tests/Fixtures/EntityRoleGeometry.native.json"
        fixture = json.loads(path.read_text(encoding="utf-8"))
        release = next(row for row in fixture["releases"] if row["entity"] == entity)
        return fixture["cases"][release["case"]]

    def test_cat_role_keeps_age_and_sleeping_dimensions(self) -> None:
        from extract_entity_geometry import role_profile

        profile = role_profile(self.role_native("minecraft:cat"))
        self.assertEqual([14, "boolean", 1, 0], profile["selector"])
        self.assertIn([1, 0, .3, .35, False, True], profile["variants"])
        self.assertIn([0, 2, .2, .2, True, True], profile["variants"])

    def test_golem_pose_geometry_has_no_dummy_age_axis(self) -> None:
        from extract_entity_geometry import role_profile

        profile = role_profile(self.role_native("minecraft:iron_golem"))
        self.assertEqual([-1, "none", 0, 0], profile["selector"])
        self.assertEqual(7, len(profile["variants"]))
        self.assertIn([0, 0, 1.4, 2.7, False, True], profile["variants"])
        self.assertIn([0, 2, .2, .2, True, True], profile["variants"])

    def test_pose_only_role_rejects_age_mutation_and_incomplete_callbacks(self) -> None:
        from extract_entity_geometry import role_profile

        for defect in ("selector", "default", "age", "age-size", "age-callback", "missing", "duplicate", "pose", "blocking"):
            with self.subTest(defect=defect):
                raw = self.role_native("minecraft:iron_golem")
                if defect == "selector": raw["metadata_index"] = 14
                if defect == "default": raw["metadata_default"] = False
                if defect == "age": raw["rows"][0]["to_age"] = 1
                if defect == "age-size": raw["rows"][0]["age_size"][0] += .1
                if defect == "age-callback": raw["rows"][0]["after_age_sync"][0] += .1
                if defect == "missing": raw["rows"].pop()
                if defect == "duplicate": raw["rows"].append(copy.deepcopy(raw["rows"][0]))
                if defect == "pose": raw["poses"][1] = "UNKNOWN"
                if defect == "blocking": raw["blocks_building"] = False
                with self.assertRaises(ValueError):
                    role_profile(raw)

    def test_role_groups_keep_distinct_entities_and_reject_duplicate_or_unequal_aliases(self) -> None:
        from extract_entity_geometry import group_roles

        records = [{"version": "1.14", "protocol": 477, "entity": entity, "raw": self.role_native(entity)} for entity in ("minecraft:cat", "minecraft:iron_golem")]
        self.assertEqual(2, len(group_roles(records)[477]))
        with self.assertRaises(ValueError):
            group_roles([records[0], records[0]])
        alias = copy.deepcopy(records[1])
        alias["version"] = "alias"
        alias["raw"]["rows"].pop()
        with self.assertRaises(ValueError):
            group_roles([records[1], alias])

    def test_role_fixture_keeps_each_entity_and_measurement_receipt(self) -> None:
        from extract_entity_geometry import role_fixture

        records = [{"version": "1.14", "protocol": 477, "entity": entity, "raw": self.role_native(entity), "receipt": {"version": "1.14", "protocol": 477, "entity": entity, "measurement_sha256": entity, "client_sha256": "client", "server_sha256": "server"}} for entity in ("minecraft:cat", "minecraft:iron_golem")]
        fixture = role_fixture(records)
        self.assertEqual(2, len(fixture["cases"]))
        self.assertEqual(["minecraft:cat", "minecraft:iron_golem"], [row["measurement_sha256"] for row in fixture["releases"]])

    def test_equal_legacy_aliases_share_a_fixture_case_and_keep_both_receipts(self) -> None:
        from extract_entity_geometry import legacy_fixture

        path = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.11.json"
        raw = json.loads(path.read_text(encoding="utf-8"))
        records = [{"version": version, "protocol": 316, "raw": copy.deepcopy(raw), "receipt": {"measurement_sha256": version, "client_sha256": "client-" + version, "server_sha256": "server-" + version}} for version in ("1.11.1", "1.11.2")]
        fixture = legacy_fixture(records)
        self.assertEqual(1, len(fixture["cases"]))
        self.assertEqual(["1.11.1", "1.11.2"], [row["version"] for row in fixture["releases"]])
        self.assertEqual([0, 0], [row["case"] for row in fixture["releases"]])
        self.assertEqual(["1.11.1", "1.11.2"], [row["measurement_sha256"] for row in fixture["releases"]])

    def test_unequal_legacy_aliases_and_duplicate_releases_are_rejected(self) -> None:
        from extract_entity_geometry import group_legacy

        path = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.11.json"
        raw = json.loads(path.read_text(encoding="utf-8"))
        records = [{"version": version, "protocol": 316, "raw": copy.deepcopy(raw)} for version in ("1.11.1", "1.11.2")]
        self.assertEqual(2, len(group_legacy(records)[316]))
        with self.assertRaises(ValueError):
            group_legacy([records[0], records[0]])
        other = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.10.2.json"
        records[1]["raw"] = json.loads(other.read_text(encoding="utf-8"))
        with self.assertRaises(ValueError):
            group_legacy(records)

    def test_legacy_1_9_uses_index_eleven_and_retains_the_box_minimum(self) -> None:
        path = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.9.json"
        profile = legacy_villager_profile(json.loads(path.read_text(encoding="utf-8")))
        self.assertEqual([11, "boolean", 1, 0], profile["selector"])
        self.assertEqual("retain_minimum", profile["refresh"])
        self.assertEqual(-1, profile["pose_index"])

    def early_pose_native(self) -> dict:
        path = Path(__file__).parents[2] / "tests/Umpk.Client.Tests/Fixtures/LegacyVillagerGeometry.native.json"
        fixture = json.loads(path.read_text(encoding="utf-8"))
        release = next(row for row in fixture["releases"] if row["version"] == "1.14")
        return fixture["cases"][release["case"]]

    def test_unmapped_1_14_retains_native_pose_and_age_callbacks(self) -> None:
        profile = legacy_villager_profile(self.early_pose_native())
        self.assertEqual([14, "boolean", 1, 0], profile["selector"])
        self.assertEqual(6, profile["pose_index"])
        self.assertEqual("retain_minimum_on_growth", profile["refresh"])
        self.assertEqual(14, len(profile["variants"]))
        self.assertIn([1, 2, .2, .2, True, True], profile["variants"])
        self.assertIn([0, 7, .6, 1.95, False, True], profile["variants"])

    def test_unmapped_pose_evidence_requires_known_defaults_and_flags(self) -> None:
        for field, value in [("metadata_default", True), ("pose_index", 5), ("pose_index", 6.0), ("blocks_building", False)]:
            with self.subTest(field=field):
                raw = self.early_pose_native()
                raw[field] = value
                with self.assertRaises(ValueError):
                    legacy_villager_profile(raw)

    def test_legacy_shrinking_also_retains_the_minimum(self) -> None:
        path = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.10.2.json"
        profile = legacy_villager_profile(json.loads(path.read_text(encoding="utf-8")))
        self.assertEqual("retain_minimum", profile["refresh"])
        self.assertEqual(-1, profile["pose_index"])

    def test_legacy_1_11_shrinking_recenters_but_growth_retains_the_minimum(self) -> None:
        path = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.11.json"
        profile = legacy_villager_profile(json.loads(path.read_text(encoding="utf-8")))
        self.assertEqual("retain_minimum_on_growth", profile["refresh"])
        self.assertEqual([12, "boolean", 1, 0], profile["selector"])
        self.assertEqual(-1, profile["pose_index"])

    def test_incomplete_or_invalid_legacy_measurements_are_rejected(self) -> None:
        path = Path(__file__).parent / "tests/fixtures/villager-box-transitions-1.11.json"
        for defect in ("empty", "missing", "duplicate", "indices", "position", "callback", "metadata", "blocking"):
            with self.subTest(defect=defect):
                raw = json.loads(path.read_text(encoding="utf-8"))
                if defect == "empty": raw["positions"] = []; raw["rows"] = []
                if defect == "missing": raw["rows"].pop()
                if defect == "duplicate": raw["rows"].append(copy.deepcopy(raw["rows"][0]))
                if defect == "indices": raw["rows"][0]["from"] = False
                if defect == "position": raw["positions"][0][0] = float("nan")
                if defect == "callback": raw["rows"][0]["after_sync"][0] += .01
                if defect == "metadata": raw["metadata_index"] = -1
                if defect == "blocking": raw["blocks_building"] = False
                with self.assertRaises(ValueError):
                    legacy_villager_profile(raw)

    def test_native_growth_retains_the_minimum_until_position_is_reapplied(self) -> None:
        profile = villager_profile(self.raw, scale_attribute="")
        self.assertEqual("retain_minimum_on_growth", profile["refresh"])
        self.assertEqual([14, "boolean", 1, 0], profile["selector"])
        self.assertEqual(6, profile["pose_index"])
        self.assertEqual([[0, 0, .6, 1.95, False, True], [0, 2, .2, .2, True, True], [1, 0, .3, .975, False, True], [1, 2, .2, .2, True, True]], profile["variants"])

    def test_incomplete_or_inconsistent_native_phases_are_rejected(self) -> None:
        for defect in ("missing", "duplicate", "dimension", "callback", "position", "pose", "metadata"):
            with self.subTest(defect=defect):
                raw = copy.deepcopy(self.raw)
                if defect == "missing": raw["rows"].pop()
                if defect == "duplicate": raw["rows"].append(copy.deepcopy(raw["rows"][0]))
                if defect == "dimension": raw["rows"][0]["to_size"][0] = .7
                if defect == "callback": raw["rows"][0]["after_pose_sync"][0] += .01
                if defect == "position": raw["rows"][0]["after_position"][0] += .01
                if defect == "pose": raw["poses"][1] = "UNRECOGNIZED"
                if defect == "metadata": raw["metadata_index"] = -1
                with self.assertRaises(ValueError):
                    villager_profile(raw, scale_attribute="")


if __name__ == "__main__":
    unittest.main()
