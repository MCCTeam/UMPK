import unittest
import copy


class LegacyInteractionExtractionTests(unittest.TestCase):
    def test_known_1_8_air_difference_is_retained_without_accepting_other_alias_changes(self) -> None:
        from extract_legacy_interactions import fold, select_profile

        blocks, items, raw = fixture()
        later = fold(raw, blocks, items)
        later["measured_state_count"] = 1394
        earlier = copy.deepcopy(later)
        earlier["measured_state_count"] = 1391
        earlier["blocks"]["air"] = 3
        selected, differences = select_profile(47, [("1.8", earlier), ("1.8.9", later)])
        self.assertEqual(later, selected)
        self.assertEqual([{"version": "1.8", "blocks": {"air": 3}, "measured_state_count": 1391}], differences)
        earlier["blocks"]["stone"] = 1
        with self.assertRaises(ValueError):
            select_profile(47, [("1.8", earlier), ("1.8.9", later)])

    def test_simple_cube_items_require_complete_native_unit_box_evidence(self) -> None:
        from extract_legacy_interactions import validate_geometry

        _, _, raw = fixture()
        evidence = {"registered_states_visited": 4, "duplicate_state_id_aliases": 1, "geometry": {"16": ["minecraft:stone", [0, 0, 0, 1, 1, 1]], "17": ["minecraft:stone", [0, 0, 0, 1, 1, 1]]}}
        validate_geometry(raw, evidence)
        evidence["geometry"].pop("17")
        with self.assertRaises(ValueError):
            validate_geometry(raw, evidence)
        evidence["geometry"]["17"] = ["minecraft:stone", [0, 0, 0, 1, 0.5, 1]]
        with self.assertRaises(ValueError):
            validate_geometry(raw, evidence)

    def test_registered_metadata_states_and_native_names_are_folded_to_legacy_identifiers(self) -> None:
        from extract_legacy_interactions import fold

        blocks, items, raw = fixture()
        result = fold(raw, blocks, items)

        self.assertEqual({"air": 1, "stone": 3}, result["blocks"])
        self.assertEqual({"air": ["pass", ""], "stone": ["simple_cube", "stone"]}, result["items"])
        self.assertEqual(2, result["state_count"])
        self.assertEqual(3, result["measured_state_count"])

    def test_missing_registrations_wrong_names_and_nonuniform_flags_are_rejected(self) -> None:
        from extract_legacy_interactions import fold

        for defect in ["missing-block", "wrong-state-name", "unknown-block", "nonuniform", "missing-item"]:
            blocks, items, raw = fixture()
            if defect == "missing-block":
                raw["states"].pop("0")
            elif defect == "wrong-state-name":
                raw["states"]["16"][0] = "minecraft:air"
            elif defect == "unknown-block":
                raw["states"]["32"] = ["minecraft:unknown", 3]
            elif defect == "nonuniform":
                raw["states"]["17"][1] = 1
            else:
                raw["items"].pop("minecraft:stone")
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                fold(raw, blocks, items)


def fixture() -> tuple[dict, dict, dict]:
    blocks = {"identity": "legacy", "blocks": [{"block_id": 0, "state_id": 0, "material": "air"}, {"block_id": 1, "state_id": 16, "material": "stone"}]}
    items = {"entries": [{"name": "air"}, {"name": "stone"}]}
    raw = {"states": {"0": ["minecraft:air", 1], "16": ["minecraft:stone", 3], "17": ["minecraft:stone", 3]}, "items": {"minecraft:air": ["pass", ""], "minecraft:stone": ["simple_cube", "minecraft:stone"]}}
    return blocks, items, raw


if __name__ == "__main__":
    unittest.main()
