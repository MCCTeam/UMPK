import copy
import unittest


class ContextInteractionExtractionTests(unittest.TestCase):
    def test_flat_native_registries_and_unit_shape_evidence_are_required(self) -> None:
        from extract_context_interactions import fold

        blocks, items, raw, geometry = fixture()
        result = fold(raw, geometry, blocks, items)
        self.assertEqual("flat", result["identity"])
        self.assertEqual(3, result["state_count"])
        self.assertEqual({"minecraft:air": 1, "minecraft:stone": 3}, result["blocks"])
        for defect in ("state", "item", "geometry", "nonuniform", "identity"):
            b, i, r, g = copy.deepcopy((blocks, items, raw, geometry))
            if defect == "state":
                r["states"].pop("2")
            elif defect == "item":
                r["items"].pop("minecraft:stone")
            elif defect == "geometry":
                g["geometry"]["2"][1][4] = 0.5
            elif defect == "nonuniform":
                r["states"]["2"][1] = 1
            else:
                b["identity"] = "legacy"
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                fold(r, g, b, i)


def fixture() -> tuple[dict, dict, dict, dict]:
    blocks = {"identity": "flat", "blocks": [{"name": "minecraft:air", "min_state": 0, "max_state": 0}, {"name": "minecraft:stone", "min_state": 1, "max_state": 2}]}
    items = {"identity": "flat", "entries": [{"name": "minecraft:air"}, {"name": "minecraft:stone"}]}
    raw = {"states": {"0": ["minecraft:air", 1], "1": ["minecraft:stone", 3], "2": ["minecraft:stone", 3]}, "items": {"minecraft:air": ["pass", ""], "minecraft:stone": ["simple_cube", "minecraft:stone"]}}
    geometry = {"registered_states_visited": 3, "duplicate_state_id_aliases": 0, "geometry": {str(state): ["minecraft:stone", [0, 0, 0, 1, 1, 1]] for state in (1, 2)}}
    return blocks, items, raw, geometry


if __name__ == "__main__":
    unittest.main()
