import unittest

from verify_legacy_outlines import clip_boxes, verify


class LegacyOutlineWitnessTests(unittest.TestCase):
    def test_inside_ray_exits_through_the_native_face(self) -> None:
        self.assertEqual([5, [1.0, 0.5, 0.5]], clip_boxes([[0, 0, 0, 1, 1, 1]], [0.5, 0.5, 0.5, 2, 0.5, 0.5]))

    def test_torch_uses_native_float_bounds(self) -> None:
        box = [0.4000000059604645, 0, 0.4000000059604645, 0.6000000238418579, 0.6000000238418579, 0.6000000238418579]
        self.assertEqual([2, [0.5, 0.3, box[2]]], clip_boxes([box], [0.5, 0.3, -1, 0.5, 0.3, 2]))

    def test_axis_delta_uses_the_native_float_squared_threshold(self) -> None:
        self.assertEqual([], clip_boxes([[0, 0, 0, 1, 1, 1]], [0.5, 0.5, -0.0001, 0.5, 0.5, 0.0001]))

    def test_stair_list_selection_excludes_the_exact_endpoint(self) -> None:
        boxes = [[0, 0, 0, 1, 0.5, 1], [0.5, 0.5, 0, 1, 1, 1]]
        segment = [0.25, 0.25, -1, 0.25, 0.25, 0]
        self.assertEqual([2, [0.25, 0.25, 0.0]], clip_boxes(boxes, segment))
        self.assertEqual([], clip_boxes(boxes, segment, farthest_from_end=True))

    def test_equal_distance_planes_keep_the_native_x_axis_priority(self) -> None:
        self.assertEqual([4, [0.0, 0.0, 0.0]], clip_boxes([[0, 0, 0, 1, 1, 1]], [-1, -1, -1, 2, 2, 2]))

    def test_a_native_face_mismatch_fails_the_witness_gate(self) -> None:
        rows = {"16": {"outline": {"boxes": [[0, 0, 0, 1, 1, 1]]}}}
        oracle = {"segments": [[0.5, 0.5, -1, 0.5, 0.5, 2]], "hits": {"16": [[3, [0.5, 0.5, 0.0]]]}}
        with self.assertRaisesRegex(ValueError, "16.*ray 0"):
            verify(rows, oracle)


if __name__ == "__main__":
    unittest.main()
