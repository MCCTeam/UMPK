import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from verify_collision_grids import verify


def top_slab() -> dict:
    return {
        "schema": 1,
        "cell_order": "(x * y_size + y) * z_size + z",
        "states": {"0": 0},
        "groups": [{
            "canonical_full_block": False,
            "planes": [[0.0, 1.0], [0.0, 0.5, 1.0], [0.0, 1.0]],
            "cells": [1],
            "boxes": [[0.0, 0.5, 0.0, 1.0, 1.0, 1.0]],
        }],
    }


class CollisionGridVerificationTests(unittest.TestCase):
    def test_rejects_cells_that_disagree_with_the_native_box_volume(self) -> None:
        data = top_slab()
        data["groups"][0]["cells"] = [0]
        with self.assertRaisesRegex(ValueError, "cell"):
            verify(data)

    def test_accepts_a_native_top_slab_without_changing_its_grid(self) -> None:
        data = top_slab()
        before = copy.deepcopy(data)
        verify(data)
        self.assertEqual(before, data)

    def test_rejects_missing_shape_references(self) -> None:
        data = top_slab()
        data["states"]["0"] = 1
        with self.assertRaisesRegex(ValueError, "group"):
            verify(data)

    def test_rejects_a_missing_registry_state(self) -> None:
        data = top_slab()
        data["states"] = {"1": 0}
        with self.assertRaisesRegex(ValueError, "state"):
            verify(data)

    def test_rejects_grid_planes_that_run_backwards(self) -> None:
        data = top_slab()
        data["groups"][0]["planes"][0] = [1.0, 0.0]
        with self.assertRaisesRegex(ValueError, "plane"):
            verify(data)

    def test_preserves_native_legacy_wall_cells_with_zero_width(self) -> None:
        data = top_slab()
        data["groups"][0] = {
            "canonical_full_block": False,
            "planes": [[0.5, 0.5], [0.0, 1.5], [0.5, 0.5]],
            "cells": [0],
            "boxes": [[0.5, 0.0, 0.5, 0.5, 1.5, 0.5]],
        }
        before = copy.deepcopy(data)
        verify(data)
        self.assertEqual(before, data)

    def test_rejects_nonfinite_grid_coordinates(self) -> None:
        for value in (float("nan"), float("inf"), -float("inf")):
            with self.subTest(value=value):
                data = top_slab()
                data["groups"][0]["planes"][0][1] = value
                with self.assertRaisesRegex(ValueError, "coordinate"):
                    verify(data)

    def test_rejects_duplicate_or_out_of_range_occupied_cells(self) -> None:
        for cells in ([1, 1], [1, 2]):
            with self.subTest(cells=cells):
                data = top_slab()
                data["groups"][0]["cells"] = cells
                with self.assertRaisesRegex(ValueError, "cell"):
                    verify(data)

    def test_rejects_boxes_that_cut_across_a_grid_cell(self) -> None:
        data = top_slab()
        data["groups"][0]["boxes"][0][1] = 0.6
        with self.assertRaisesRegex(ValueError, "plane"):
            verify(data)

    def test_rejects_a_partial_shape_labeled_as_the_full_block_singleton(self) -> None:
        data = top_slab()
        data["groups"][0]["canonical_full_block"] = True
        with self.assertRaisesRegex(ValueError, "canonical"):
            verify(data)

    def test_command_reports_success_only_after_checking_the_dump(self) -> None:
        root = Path(self.enterContext(tempfile.TemporaryDirectory()))
        path = root / "grids.json"
        data = top_slab()
        path.write_text(json.dumps(data), encoding="utf-8")
        command = [sys.executable, str(Path(__file__).with_name("verify_collision_grids.py")), str(path)]
        valid = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.assertEqual(0, valid.returncode, valid.stderr)
        self.assertIn("1 states", valid.stdout)
        data["groups"][0]["cells"] = [0]
        path.write_text(json.dumps(data), encoding="utf-8")
        before = path.read_bytes()
        invalid = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.assertNotEqual(0, invalid.returncode)
        self.assertIn("cell", invalid.stderr)
        self.assertEqual(before, path.read_bytes())


if __name__ == "__main__":
    unittest.main()
