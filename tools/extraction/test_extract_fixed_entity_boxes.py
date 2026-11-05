import copy
import unittest


class FixedEntityBoxTests(unittest.TestCase):
    def test_constant_native_box_is_extracted(self) -> None:
        from extract_entity_obstruction import fixed_dimensions

        self.assertEqual([2.0, 2.0], fixed_dimensions(fixture()))

    def test_changed_owners_selectors_flags_and_geometry_are_rejected(self) -> None:
        from extract_entity_obstruction import fixed_dimensions

        for defect in ("owner", "selector", "flag", "box", "size", "missing"):
            row = fixture()
            if defect == "owner":
                row["method_owners"]["getDimensions"] = "Special"
            elif defect == "selector":
                row["geometry"]["selectors"] = [{"kind": "boolean"}]
            elif defect == "flag":
                row["blocks_building"] = False
            elif defect == "box":
                row["geometry"]["default"]["poses"][0][4][0] += 0.125
            elif defect == "size":
                row["dimensions"]["STANDING"][0] = 3.0
            elif defect == "missing":
                row["geometry"]["default"]["poses"] = []
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                fixed_dimensions(row)


def fixture() -> dict:
    return copy.deepcopy({
        "class": "net.minecraft.world.entity.boss.enderdragon.EndCrystal",
        "constructor_blocks_building": True,
        "blocks_building": True,
        "method_owners": {"getDimensions": "net.minecraft.world.entity.Entity", "getBoundingBox": "net.minecraft.world.entity.Entity"},
        "dimensions": {"STANDING": [2.0, 2.0, False]},
        "geometry": {"selectors": [], "default": {"blocks_building": True, "poses": [["STANDING", 2.0, 2.0, False, [0.5, 64.0, 0.5, 2.5, 66.0, 2.5]]]}}
    })


if __name__ == "__main__":
    unittest.main()
