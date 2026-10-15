import copy
import unittest

from extract_legacy_targeting import fold_legacy


class LegacyTargetingFoldTests(unittest.TestCase):
    def fixture(self) -> tuple[dict, dict]:
        def shape(z: float = 0) -> dict:
            return {"boxes": [[0, 0, z, 1, 1, z + 0.1875]], "offset": "none", "max_horizontal_offset": 0, "max_vertical_offset": 0}
        empty = {**shape(), "boxes": []}
        rows = {str(state): {"block": "minecraft:air" if state < 16 else "minecraft:wooden_door", "outline": empty if state < 16 else shape(),
                            "interaction": empty, "canonical_state": 0 if state < 16 else 16, "trace_selection": "nearest_from_start"} for state in range(32)}
        expanded = {"states": rows, "rejected_metadata": {}}
        doors = {"shape_pool": [shape(), shape(0.8125)], "doors": {"16": {"block": "minecraft:wooden_door", "offset": [0, 1, 0], "by_state": {"0": 0, "16": 1}}}}
        return expanded, doors

    def test_expands_context_inputs_and_deduplicates_alias_roots(self) -> None:
        expanded, doors = self.fixture()
        result = fold_legacy(expanded, doors, 32)
        self.assertEqual("aabb", result["clip_model"])
        self.assertEqual([[16, 16, 0]], result["context_roots"])
        self.assertEqual(1, len(result["context_nodes"]))
        ranges = result["context_nodes"][0]["ranges"]
        self.assertEqual([0, 16], ranges[0][:2])
        self.assertEqual([16, 16], ranges[1][:2])
        self.assertNotEqual(ranges[0][2], ranges[1][2])

    def test_native_rejected_metadata_has_no_invented_shape(self) -> None:
        expanded, doors = self.fixture()
        del expanded["states"]["31"]
        expanded["rejected_metadata"]["31"] = ["minecraft:wooden_door", "java.lang.IllegalArgumentException", "invalid"]
        result = fold_legacy(expanded, doors, 32)
        self.assertEqual([], result["shape_pool"][result["state_refs"][-1][2]]["boxes"])
        self.assertEqual(15, result["context_roots"][0][1])

    def test_preserves_the_native_stair_box_list_model(self) -> None:
        expanded, doors = self.fixture()
        expanded["states"]["16"]["trace_selection"] = "farthest_from_end"
        result = fold_legacy(expanded, doors, 32)
        self.assertTrue(any(shape.get("clip_model") == "aabb_list" for shape in result["shape_pool"]))

    def test_refuses_incomplete_native_neighbor_coverage(self) -> None:
        expanded, doors = self.fixture()
        del doors["doors"]["16"]["by_state"]["0"]
        with self.assertRaisesRegex(ValueError, "neighbor coverage"):
            fold_legacy(expanded, doors, 32)

    def test_refuses_an_unmeasured_context_shape(self) -> None:
        expanded, doors = self.fixture()
        doors["doors"]["16"]["by_state"]["0"] = 2
        with self.assertRaisesRegex(ValueError, "shape"):
            fold_legacy(expanded, doors, 32)

    def test_refuses_a_context_for_a_different_block(self) -> None:
        expanded, doors = self.fixture()
        doors["doors"]["16"]["block"] = "minecraft:stone"
        with self.assertRaisesRegex(ValueError, "block"):
            fold_legacy(expanded, doors, 32)


if __name__ == "__main__":
    unittest.main()
