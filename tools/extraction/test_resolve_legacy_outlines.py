import unittest

from resolve_legacy_outlines import resolve_sources


class LegacyOutlineBindingsTests(unittest.TestCase):
    def fixture(self) -> tuple[dict, dict[str, str]]:
        symbols = {"block": "Block", "state": "State", "hardness_args": ["State", "World", "Pos"]}
        sources = {
            "Block": """
                public Hit trace(State s, World w, Pos p, Vector a, Vector b) { return this.clip(p, a, b, s.outline(w, p)); }
                public boolean event(State s, World w, Pos p, int a, int b) { return false; }
                protected Hit clip(Pos p, Vector a, Vector b, Box box) { return null; }
                """,
            "World": "public abstract class World implements View {}",
            "View": "public interface View {}",
            "Vector": "public final double x; public final double y; public final double z;",
            "Hit": "public Direction face; public Vector point;",
            "Direction": "public enum Direction { DOWN, UP, NORTH, SOUTH, WEST, EAST; }",
            "Box": "public final double x0; public final double y0; public final double z0; public final double x1; public final double y1; public final double z1;",
            "Piston": "public Hit trace(State s, World w, Pos p, Vector a, Vector b) { return null; }",
        }
        return symbols, sources

    def test_resolves_native_trace_and_empty_override(self) -> None:
        symbols, sources = self.fixture()
        actual = resolve_sources(symbols, sources)
        self.assertEqual("trace", actual["ray"])
        self.assertEqual("outline", actual["state_outline"])
        self.assertEqual("face", actual["hit_face"])
        self.assertEqual("point", actual["hit_point"])
        self.assertEqual(["x0", "y0", "z0", "x1", "y1", "z1"], actual["box_fields"])
        self.assertEqual({"Piston": {"kind": "empty"}}, actual["overrides"])

    def test_refuses_an_unclassified_native_trace_override(self) -> None:
        symbols, sources = self.fixture()
        sources["Unknown"] = "public Hit trace(State s, World w, Pos p, Vector a, Vector b) { return invented(); }"
        with self.assertRaisesRegex(ValueError, "Unclassified.*Unknown"):
            resolve_sources(symbols, sources)

    def test_refuses_missing_native_coordinates(self) -> None:
        symbols, sources = self.fixture()
        sources["Vector"] = "public final double x; public final double y;"
        with self.assertRaisesRegex(ValueError, "coordinates"):
            resolve_sources(symbols, sources)

    def test_resolves_the_native_legacy_offset_method(self) -> None:
        symbols, sources = self.fixture()
        sources["Block"] += "public Vector offset(State s, View w, Pos p) { return new Vector((seed >> 16 & 15L) / 15.0F, 0, 0); }"
        self.assertEqual("offset", resolve_sources(symbols, sources)["offset"])

    def test_binds_the_door_family_and_its_native_half_bit(self) -> None:
        symbols, sources = self.fixture()
        sources["Door"] = '"hinge" "half" "open" "powered"; public State decode(int meta) { return (meta & 8) == 0 ? lower : upper; }'
        actual = resolve_sources(symbols, sources)
        self.assertEqual("Door", actual["door_class"])
        self.assertEqual(8, actual["door_upper_bit"])

    def test_refuses_an_unrecognized_door_half_encoding(self) -> None:
        symbols, sources = self.fixture()
        sources["Door"] = '"hinge" "half" "open" "powered"; public State decode(int meta) { return unknown(meta); }'
        with self.assertRaisesRegex(ValueError, "door half"):
            resolve_sources(symbols, sources)

    def test_hit_type_enum_is_not_confused_with_the_direction(self) -> None:
        symbols, sources = self.fixture()
        sources["Hit"] += "public HitType type;"
        sources["HitType"] = "public enum HitType { MISS, BLOCK, ENTITY; }"
        self.assertEqual("Direction", resolve_sources(symbols, sources)["direction_type"])

    def test_mutable_bounds_follow_native_setter_argument_order(self) -> None:
        symbols, sources = self.fixture()
        symbols["hardness_args"] = ["World", "Pos"]
        sources["Block"] = """
            public Hit trace(World w, Pos p, Vector a, Vector b) { this.update((View)w, p); return null; }
            protected final void set(float a, float b, float c, float d, float e, float f) { this.minX = a; this.minY = b; this.minZ = c; this.maxX = d; this.maxY = e; this.maxZ = f; }
            """
        sources["Piston"] = "public Hit trace(World w, Pos p, Vector a, Vector b) { return null; }"
        actual = resolve_sources(symbols, sources)
        self.assertEqual("update", actual["bounds_update"])
        self.assertEqual(["minX", "minY", "minZ", "maxX", "maxY", "maxZ"], actual["bounds_fields"])

    def test_earliest_mutable_trace_can_omit_the_world_view_cast(self) -> None:
        symbols, sources = self.fixture()
        symbols["hardness_args"] = ["World", "Pos"]
        sources["Block"] = """
            public Hit trace(World w, Pos p, Vector a, Vector b) { this.update(w, p); return null; }
            protected final void set(float a, float b, float c, float d, float e, float f) { this.x0 = a; this.y0 = b; this.z0 = c; this.x1 = d; this.y1 = e; this.z1 = f; }
            """
        sources["Piston"] = "public Hit trace(World w, Pos p, Vector a, Vector b) { return null; }"
        self.assertEqual("update", resolve_sources(symbols, sources)["bounds_update"])


if __name__ == "__main__":
    unittest.main()
