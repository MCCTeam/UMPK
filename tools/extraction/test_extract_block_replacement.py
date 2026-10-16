import copy
import unittest

import extract_block_replacement as extraction
from extract_block_replacement import compact_states, replacement_flags


class ReplacementExtractionTests(unittest.TestCase):
    def test_selects_the_latest_equal_protocol_profile_without_mutating_it(self) -> None:
        profile = {"schema": 1, "identity": "flat", "state_count": 2, "ranges": [[0, 0, 3], [1, 1, 0]], "below_overrides": {}}
        selected, differences = extraction.select_profile(754, [("1.16.4", profile), ("1.16.5", profile)])

        self.assertEqual(profile, selected)
        self.assertEqual([], differences)
        selected["ranges"][0][2] = 0
        self.assertEqual(3, profile["ranges"][0][2])

    def test_rejects_unreviewed_differences_between_equal_protocol_releases(self) -> None:
        profile = {"schema": 1, "identity": "flat", "state_count": 1, "ranges": [[0, 0, 3]], "below_overrides": {}}
        earlier = copy.deepcopy(profile)
        earlier["ranges"][0][2] = 2

        with self.assertRaises(ValueError):
            extraction.select_profile(754, [("1.16.4", earlier), ("1.16.5", profile)])

    def test_retains_the_reviewed_native_1_8_air_and_double_plant_difference(self) -> None:
        upper = [[2802, 2803, 3]]
        latest = {"schema": 1, "identity": "legacy", "state_count": 5, "ranges": [[0, 0, 3], [2808, 2811, 2]], "below_overrides": {str(state): upper for state in range(2808, 2812)}}
        earlier = {"schema": 1, "identity": "legacy", "state_count": 2, "ranges": [[0, 0, 2], [2808, 2808, 2]], "below_overrides": {"2808": upper}}

        selected, differences = extraction.select_profile(47, [("1.8", earlier), ("1.8.9", latest)])

        self.assertEqual(latest, selected)
        self.assertEqual([{"version": "1.8", "profile": earlier}], differences)
        for defect in ["another-release", "another-state", "another-neighbor"]:
            with self.subTest(defect=defect):
                corrupted = copy.deepcopy(earlier)
                if defect == "another-state":
                    corrupted["ranges"][1][2] = 3
                elif defect == "another-neighbor":
                    corrupted["below_overrides"]["2808"] = [[2802, 2803, 2]]
                with self.assertRaises(ValueError):
                    extraction.select_profile(47, [("1.8.1" if defect == "another-release" else "1.8", corrupted), ("1.8.9", latest)])

    def test_folds_native_rules_against_the_owning_registry_and_cube_items(self) -> None:
        blocks = {"identity": "flat", "blocks": [{"name": "minecraft:air", "min_state": 0, "max_state": 0}, {"name": "minecraft:stone", "min_state": 1, "max_state": 1}]}
        interactions = {"items": {"minecraft:air": ["pass", ""], "minecraft:stone": ["simple_cube", "minecraft:stone"]}}
        native = {"items": ["minecraft:stone"], "states": {"0": ["minecraft:air", 4095], "1": ["minecraft:stone", 0]}}

        self.assertEqual({"schema": 1, "identity": "flat", "state_count": 2, "ranges": [[0, 0, 3], [1, 1, 0]], "below_overrides": {}}, extraction.fold(native, blocks, interactions))

    def test_rejects_incomplete_or_duplicate_ordinary_cube_item_measurements(self) -> None:
        blocks = {"identity": "flat", "blocks": [{"name": "minecraft:stone", "min_state": 1, "max_state": 1}]}
        interactions = {"items": {"minecraft:stone": ["simple_cube", "minecraft:stone"]}}
        for items in [[], ["minecraft:dirt"], ["minecraft:stone", "minecraft:stone"]]:
            with self.subTest(items=items):
                with self.assertRaises(ValueError):
                    extraction.fold({"items": items, "states": {"1": ["minecraft:stone", 0]}}, blocks, interactions)

    def test_folds_metadata_states_without_treating_metadata_as_block_ids(self) -> None:
        blocks = {"identity": "legacy", "blocks": [{"name": "air", "block_id": 0}, {"name": "stone", "block_id": 1}]}
        interactions = {"items": {"stone": ["simple_cube", "stone"]}}
        native = {"items": ["minecraft:stone"], "states": {"0": ["minecraft:air", 4095], "16": ["minecraft:stone", 0], "17": ["minecraft:stone", 0]}}

        self.assertEqual({"schema": 1, "identity": "legacy", "state_count": 3, "ranges": [[0, 0, 3], [16, 17, 0]], "below_overrides": {}}, extraction.fold(native, blocks, interactions))

        for block in blocks["blocks"]:
            block["material"] = block.pop("name")
        self.assertEqual({"schema": 1, "identity": "legacy", "state_count": 3, "ranges": [[0, 0, 3], [16, 17, 0]], "below_overrides": {}}, extraction.fold(native, blocks, interactions))

    def test_preserves_both_replacement_phases(self) -> None:
        for native, expected in [(0, 0), (63, 1), (4032, 2), (4095, 3)]:
            with self.subTest(native=native):
                self.assertEqual(expected, replacement_flags(native))

    def test_rejects_unrepresented_face_rules_and_invalid_masks(self) -> None:
        for native in [True, -1, 1, 64, 4096, "4095", None]:
            with self.subTest(native=native):
                with self.assertRaises(ValueError):
                    replacement_flags(native)

    def test_compacts_equal_ranges_without_filling_state_gaps(self) -> None:
        native = {"0": ["minecraft:air", 4095], "2": ["minecraft:stone", 0], "3": ["minecraft:stone", 0]}
        expected = {0: "minecraft:air", 2: "minecraft:stone", 3: "minecraft:stone"}

        self.assertEqual({"state_count": 3, "ranges": [[0, 0, 3], [2, 3, 0]], "below_overrides": {}}, compact_states(native, expected))

    def test_rejects_incomplete_coverage_and_wrong_state_identity(self) -> None:
        for defect in ["missing", "extra", "wrong-name", "invalid-id", "negative-id", "wrong-row"]:
            with self.subTest(defect=defect):
                native = {"0": ["minecraft:air", 4095], "1": ["minecraft:stone", 0]}
                if defect == "missing":
                    del native["1"]
                elif defect == "extra":
                    native["2"] = ["minecraft:stone", 0]
                elif defect == "wrong-name":
                    native["1"][0] = "minecraft:air"
                elif defect == "invalid-id":
                    native["01"] = native.pop("1")
                elif defect == "negative-id":
                    native["-1"] = native.pop("1")
                else:
                    native["1"] = ["minecraft:stone", 0, "extra"]

                with self.assertRaises(ValueError):
                    compact_states(native, {0: "minecraft:air", 1: "minecraft:stone"})

    def test_preserves_measured_legacy_lower_block_dependency(self) -> None:
        native = {
            "0": ["minecraft:air", 4095],
            "16": ["minecraft:stone", 0],
            "2802": ["minecraft:double_plant", 4095],
            "2803": ["minecraft:double_plant", 4095],
            "2808": ["minecraft:double_plant", {"default": 4032, "below": {"2802": 4095, "2803": 4095}, "below_states_tested": 5}],
        }

        self.assertEqual(
            {"state_count": 5, "ranges": [[0, 0, 3], [16, 16, 0], [2802, 2803, 3], [2808, 2808, 2]], "below_overrides": {"2808": [[2802, 2803, 3]]}},
            compact_states(native, {0: "minecraft:air", 1: "minecraft:stone", 175: "minecraft:double_plant"}, legacy=True),
        )

    def test_rejects_unmeasured_and_redundant_neighbor_rules(self) -> None:
        original = {"default": 4032, "below": {"0": 4095}, "below_states_tested": 2}
        for defect in ["unknown-below", "incomplete-probe", "redundant", "invalid-mask", "extra-axis", "flat-neighbor", "wrong-below", "bool-count"]:
            with self.subTest(defect=defect):
                rule = copy.deepcopy(original)
                if defect == "unknown-below":
                    rule["below"] = {"2": 4095}
                elif defect == "incomplete-probe":
                    rule["below_states_tested"] = 1
                elif defect == "redundant":
                    rule["below"]["0"] = 4032
                elif defect == "invalid-mask":
                    rule["below"]["0"] = 1
                elif defect == "extra-axis":
                    rule["above"] = {}
                elif defect == "wrong-below":
                    rule["below"] = []
                elif defect == "bool-count":
                    rule["below_states_tested"] = True
                native = {"0": ["minecraft:air", 4095], "16": ["minecraft:stone", rule]}

                with self.assertRaises(ValueError):
                    compact_states(native, {0: "minecraft:air", 1: "minecraft:stone"} if defect != "flat-neighbor" else {0: "minecraft:air", 16: "minecraft:stone"}, legacy=defect != "flat-neighbor")


if __name__ == "__main__":
    unittest.main()
