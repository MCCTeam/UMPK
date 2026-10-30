import copy
import unittest

from extract_block_interactions import validate_client_measurement, validate_measurement


class BlockInteractionExtractionTests(unittest.TestCase):
    def fixture(self) -> tuple[dict, dict[int, str], set[str]]:
        raw = {"states": {"0": ["minecraft:air", 1], "1": ["minecraft:stone", 3], "2": ["minecraft:stone", 3]},
               "items": {"minecraft:stone": ["simple_cube", "minecraft:stone"], "minecraft:stick": ["pass", ""]}}
        return raw, {0: "minecraft:air", 1: "minecraft:stone", 2: "minecraft:stone"}, {"minecraft:stone", "minecraft:stick"}

    def test_folds_only_equal_state_contracts(self) -> None:
        raw, states, items = self.fixture()
        self.assertEqual(({"minecraft:air": 1, "minecraft:stone": 3}, raw["items"]), validate_measurement(raw, states, items))

    def test_rejects_incomplete_or_mismatched_registry(self) -> None:
        for defect in ("missing-state", "extra-state", "wrong-block", "missing-item", "extra-item", "nonuniform"):
            with self.subTest(defect=defect):
                raw, states, items = copy.deepcopy(self.fixture())
                if defect == "missing-state":
                    del raw["states"]["2"]
                elif defect == "extra-state":
                    raw["states"]["3"] = ["minecraft:stone", 3]
                elif defect == "wrong-block":
                    raw["states"]["1"][0] = "minecraft:air"
                elif defect == "missing-item":
                    del raw["items"]["minecraft:stick"]
                elif defect == "extra-item":
                    raw["items"]["minecraft:missing"] = ["pass", ""]
                else:
                    raw["states"]["2"][1] = 2
                with self.assertRaises(ValueError):
                    validate_measurement(raw, states, items)

    def test_rejects_unproven_flags_and_item_contracts(self) -> None:
        for field, value in (("flag", 4), ("flag", True), ("kind", "success"), ("block", "minecraft:missing"), ("block", "")):
            with self.subTest(field=field, value=value):
                raw, states, items = self.fixture()
                if field == "flag":
                    raw["states"]["1"][1] = value
                else:
                    raw["items"]["minecraft:stone"][0 if field == "kind" else 1] = value
                with self.assertRaises(ValueError):
                    validate_measurement(raw, states, items)

    def test_requires_native_client_parity_and_class_origin_evidence(self) -> None:
        raw, _, _ = self.fixture()
        report = {"status": "complete", "client_server_equal": True, "native_client_classes": {name: name for name in (
            "net.minecraft.world.level.block.Block", "net.minecraft.world.level.block.state.BlockState", "net.minecraft.world.item.Item", "net.minecraft.world.item.BlockItem")}}
        validate_client_measurement(raw, copy.deepcopy(raw), report)
        for defect in ("different", "failed", "no-origin", "no-parity"):
            with self.subTest(defect=defect):
                client, evidence = copy.deepcopy(raw), copy.deepcopy(report)
                if defect == "different":
                    client["items"]["minecraft:stone"][0] = "unknown"
                elif defect == "failed":
                    evidence["status"] = "failed"
                elif defect == "no-origin":
                    del evidence["native_client_classes"]["net.minecraft.world.item.Item"]
                else:
                    evidence["client_server_equal"] = False
                with self.assertRaisesRegex(ValueError, "client"):
                    validate_client_measurement(raw, client, evidence)


if __name__ == "__main__":
    unittest.main()
