import tempfile
import unittest
from pathlib import Path


class LegacyDeltaPositionBindingsTests(unittest.TestCase):
    def test_unique_native_long_floor_is_resolved(self) -> None:
        from measure_legacy_entity_delta_position import resolve_math

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "a.java").write_text(source("a"))
            self.assertEqual(("a", "d"), resolve_math(root))

    def test_missing_or_ambiguous_native_floor_is_rejected(self) -> None:
        from measure_legacy_entity_delta_position import resolve_math

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaises(ValueError):
                resolve_math(root)
            (root / "a.java").write_text(source("a"))
            (root / "b.java").write_text(source("b"))
            with self.assertRaises(ValueError):
                resolve_math(root)


def source(name: str) -> str:
    return f"""class {name} {{
        float[] table = new float[65536];
        double sine = Math.sin(0);
        public static long d(double x) {{ long whole = (long)x; return x < whole ? whole - 1L : whole; }}
    }}"""


if __name__ == "__main__":
    unittest.main()
