import copy
import unittest

from extract_outlines import fold_outlines


class OutlineExtractionTests(unittest.TestCase):
    def shape(self, boxes: list, offset: str = "none") -> dict:
        return {"boxes": boxes, "offset": offset, "max_horizontal_offset": 0.25, "max_vertical_offset": 0.2}

    def fixture(self) -> dict:
        empty = self.shape([])
        cube = self.shape([[0, 0, 0, 1, 1, 1]])
        return {
            "0": {"block": "minecraft:air", "outline": empty, "interaction": empty},
            "1": {"block": "minecraft:scaffolding", "outline": self.shape([[0, 0.875, 0, 1, 1, 1]]),
                  "interaction": cube, "held_item": "minecraft:scaffolding", "held_outline": cube},
            "2": {"block": "minecraft:flower", "outline": self.shape([[0.3, 0, 0.3, 0.7, 0.6, 0.7]], "xz"), "interaction": empty},
        }

    def fold(self, raw: dict) -> dict:
        return fold_outlines(raw, {0: "minecraft:air", 1: "minecraft:scaffolding", 2: "minecraft:flower"}, {"minecraft:scaffolding"})

    def test_keeps_separate_outline_interaction_held_shapes_and_offsets(self) -> None:
        folded = self.fold(self.fixture())
        refs = folded["state_refs"]
        self.assertEqual(3, len(refs))
        self.assertEqual("minecraft:scaffolding", refs[1][4])
        self.assertNotEqual(refs[1][2], refs[1][3])
        self.assertEqual(refs[1][3], refs[1][5])
        self.assertEqual("xz", folded["shape_pool"][refs[2][2]]["offset"])
        self.assertEqual("none", folded["shape_pool"][refs[0][2]]["offset"])

    def test_retains_the_source_backed_ray_clip_model(self) -> None:
        states = {0: "minecraft:air", 1: "minecraft:scaffolding", 2: "minecraft:flower"}
        measured = fold_outlines(self.fixture(), states, {"minecraft:scaffolding"}, clip_model="voxel_forward")
        self.assertEqual("voxel_forward", measured["clip_model"])
        with self.assertRaises(ValueError):
            fold_outlines(self.fixture(), states, {"minecraft:scaffolding"}, clip_model="unknown")

    def test_keeps_the_legacy_box_list_selection_model_per_shape(self) -> None:
        raw = self.fixture()
        raw["2"]["outline"]["clip_model"] = "aabb_list"
        measured = self.fold(raw)
        self.assertEqual("aabb_list", measured["shape_pool"][measured["state_refs"][2][2]]["clip_model"])

    def test_requires_exact_state_identity(self) -> None:
        for defect in ("missing", "extra", "name"):
            raw = copy.deepcopy(self.fixture())
            if defect == "missing":
                del raw["1"]
            elif defect == "extra":
                raw["3"] = raw["1"]
            else:
                raw["1"]["block"] = "minecraft:flower"
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                self.fold(raw)

    def test_rejects_invalid_geometry_offset_or_held_selector(self) -> None:
        for defect in ("box", "nan", "offset", "bound", "item", "missing-held"):
            raw = copy.deepcopy(self.fixture())
            if defect == "box":
                raw["2"]["outline"]["boxes"][0][3] = -1
            elif defect == "nan":
                raw["2"]["outline"]["boxes"][0][0] = float("nan")
            elif defect == "offset":
                raw["2"]["outline"]["offset"] = "unknown"
            elif defect == "bound":
                raw["2"]["outline"]["max_horizontal_offset"] = -1
            elif defect == "item":
                raw["1"]["held_item"] = "minecraft:missing"
            else:
                del raw["1"]["held_outline"]
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                self.fold(raw)


if __name__ == "__main__":
    unittest.main()
