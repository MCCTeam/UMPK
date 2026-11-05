import copy
import math
import unittest

import extract_mining


class MiningExtractionTests(unittest.TestCase):
    def test_legacy_checks_every_registered_meta_and_keeps_native_names(self) -> None:
        dump = {
            "states": {"16": ["minecraft:stone", 1.5, True], "17": ["minecraft:stone", 1.5, True]},
            "items": {"minecraft:air": {}, "minecraft:wooden_pickaxe": {"16": [2.0, True], "17": [2.0, True]}},
        }
        blocks = {"identity": "legacy", "blocks": [{"block_id": 1, "meta": 0, "state_id": 16, "material": "stone"}]}
        items = {"entries": [{"name": "air"}, {"name": "wooden_pickaxe"}]}

        facts, measured_states = extract_mining.fold_dataset(dump, blocks, items)

        self.assertEqual(2, measured_states)
        self.assertEqual({"stone": [1.5, True]}, facts["blocks"])
        self.assertEqual({"wooden_pickaxe": {"stone": [2.0, True]}}, facts["tools"])
        for mutation in ("id", "name", "missing", "speed"):
            with self.subTest(mutation=mutation):
                changed = copy.deepcopy(dump)
                if mutation == "id":
                    changed["states"]["32"] = changed["states"].pop("17")
                elif mutation == "name":
                    changed["states"]["17"][0] = "minecraft:dirt"
                elif mutation == "missing":
                    changed["states"] = {}
                else:
                    changed["items"]["minecraft:wooden_pickaxe"]["17"][0] = 3.0
                with self.assertRaises(ValueError):
                    extract_mining.fold_dataset(changed, blocks, items)

    def fixture(self) -> dict:
        return {
            "states": {"0": ["minecraft:stone", 1.5, True], "1": ["minecraft:stone", 1.5, True]},
            "items": {"minecraft:air": {}, "minecraft:wooden_pickaxe": {"0": [2.0, True], "1": [2.0, True]}},
        }

    def test_preserves_hardness_and_tool_rules_from_each_state(self) -> None:
        blocks = {0: "minecraft:stone", 1: "minecraft:stone"}
        dump = {
            "states": {"0": ["minecraft:stone", 1.5, True], "1": ["minecraft:stone", 1.5, True]},
            "items": {"minecraft:air": {}, "minecraft:wooden_pickaxe": {"0": [2.0, True], "1": [2.0, True]}},
        }

        result = extract_mining.fold(dump, blocks, set(dump["items"]))

        self.assertEqual({"minecraft:stone": [1.5, True]}, result["blocks"])
        self.assertEqual({"minecraft:wooden_pickaxe": {"minecraft:stone": [2.0, True]}}, result["tools"])

    def test_refuses_incomplete_or_misidentified_state_measurements(self) -> None:
        for change in ("missing", "extra", "name"):
            with self.subTest(change=change):
                dump = self.fixture()
                if change == "missing":
                    del dump["states"]["1"]
                elif change == "extra":
                    dump["states"]["2"] = ["minecraft:stone", 1.5, True]
                else:
                    dump["states"]["1"][0] = "minecraft:dirt"
                with self.assertRaisesRegex(ValueError, "state"):
                    extract_mining.fold(dump, {0: "minecraft:stone", 1: "minecraft:stone"}, set(dump["items"]))

    def test_refuses_collapsing_state_dependent_hardness_or_tool_behavior(self) -> None:
        for change in ("hardness", "harvest", "speed", "drops", "implicit-default"):
            with self.subTest(change=change):
                dump = self.fixture()
                if change == "hardness":
                    dump["states"]["1"][1] = 2.0
                elif change == "harvest":
                    dump["states"]["1"][2] = False
                elif change == "speed":
                    dump["items"]["minecraft:wooden_pickaxe"]["1"][0] = 3.0
                elif change == "drops":
                    dump["items"]["minecraft:wooden_pickaxe"]["1"][1] = False
                else:
                    del dump["items"]["minecraft:wooden_pickaxe"]["1"]
                with self.assertRaisesRegex(ValueError, "state-dependent"):
                    extract_mining.fold(dump, {0: "minecraft:stone", 1: "minecraft:stone"}, set(dump["items"]))

    def test_refuses_unknown_or_missing_items_and_unknown_tool_states(self) -> None:
        for change in ("missing", "extra", "state"):
            with self.subTest(change=change):
                dump = self.fixture()
                items = set(dump["items"])
                if change == "missing":
                    del dump["items"]["minecraft:air"]
                elif change == "extra":
                    dump["items"]["minecraft:unknown"] = {}
                else:
                    dump["items"]["minecraft:wooden_pickaxe"]["2"] = [2.0, True]
                with self.assertRaises(ValueError):
                    extract_mining.fold(dump, {0: "minecraft:stone", 1: "minecraft:stone"}, items)

    def test_refuses_invalid_numeric_values_and_non_boolean_flags(self) -> None:
        for value in (math.nan, math.inf, -2.0, True, "1.5"):
            with self.subTest(hardness=value):
                dump = self.fixture()
                for row in dump["states"].values():
                    row[1] = value
                with self.assertRaises(ValueError):
                    extract_mining.fold(dump, {0: "minecraft:stone", 1: "minecraft:stone"}, set(dump["items"]))
        for value in (math.nan, math.inf, -1.0, True, "2.0"):
            with self.subTest(speed=value):
                dump = self.fixture()
                for row in dump["items"]["minecraft:wooden_pickaxe"].values():
                    row[0] = value
                with self.assertRaises(ValueError):
                    extract_mining.fold(dump, {0: "minecraft:stone", 1: "minecraft:stone"}, set(dump["items"]))
        dump = self.fixture()
        for row in dump["states"].values():
            row[2] = 1
        with self.assertRaises(ValueError):
            extract_mining.fold(dump, {0: "minecraft:stone", 1: "minecraft:stone"}, set(dump["items"]))


if __name__ == "__main__":
    unittest.main()
