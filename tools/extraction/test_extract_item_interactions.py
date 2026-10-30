import copy
import unittest

from extract_item_interactions import validate_default_tools


class DefaultToolExtractionTests(unittest.TestCase):
    def fixture(self) -> dict:
        return {"minecraft:stone": None, "minecraft:pickaxe": {"rules": [
            {"blocks": "#minecraft:mineable/pickaxe", "speed": 8.0, "correct_for_drops": True},
            {"blocks": ["minecraft:stone"], "correct_for_drops": False},
        ]}}

    def test_preserves_native_rule_order_and_selectors(self) -> None:
        original = self.fixture()
        actual = validate_default_tools(original, set(original), {"minecraft:stone"})
        self.assertEqual(original, actual)

    def test_requires_exact_item_coverage(self) -> None:
        for items in ({"minecraft:stone"}, {"minecraft:stone", "minecraft:pickaxe", "minecraft:missing"}):
            with self.subTest(items=items), self.assertRaisesRegex(ValueError, "item"):
                validate_default_tools(self.fixture(), items, {"minecraft:stone"})

    def test_rejects_invalid_tool_fields_and_selectors(self) -> None:
        for defect in ("block", "tag", "speed", "boolean", "damage", "default", "creative"):
            with self.subTest(defect=defect):
                raw = copy.deepcopy(self.fixture())
                tool = raw["minecraft:pickaxe"]
                if defect == "block":
                    tool["rules"][1]["blocks"] = "minecraft:missing"
                elif defect == "tag":
                    tool["rules"][0]["blocks"] = "#"
                elif defect == "speed":
                    tool["rules"][0]["speed"] = 0
                elif defect == "boolean":
                    tool["rules"][0]["correct_for_drops"] = 1
                elif defect == "damage":
                    tool["damage_per_block"] = -1
                elif defect == "default":
                    tool["default_mining_speed"] = float("inf")
                else:
                    tool["can_destroy_blocks_in_creative"] = 0
                with self.assertRaises(ValueError):
                    validate_default_tools(raw, set(raw), {"minecraft:stone"})


if __name__ == "__main__":
    unittest.main()
