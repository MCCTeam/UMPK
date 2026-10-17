import unittest

from resolve_flat_outlines import resolve_sources


class FlatOutlineBindingsTests(unittest.TestCase):
    def fixture(self) -> tuple[dict, dict[str, str]]:
        symbols = {"block": "Block", "state": "State", "item": "Item", "hardness_args": ["State", "View", "Pos"]}
        sources = {
            "Block": """
                public static Shape box(double a, double b, double c, double d, double e, double f) { return Shapes.make(a / 16.0, b / 16.0, c / 16.0, d / 16.0, e / 16.0, f / 16.0); }
                public Shape outline(State s, View v, Pos p) { return Shapes.full(); }
                public Shape interaction(State s, View v, Pos p) { return Shapes.empty(); }
                public Vector offset(State s, View v, Pos p) { return new Vector((1L & 15L) / 15.0F - 0.5, 0, 0.5); }
                """,
            "State": """
                default Shape outline(View v, Pos p) { return this.block().outline(this, v, p); }
                default Shape interaction(View v, Pos p) { return this.block().interaction(this, v, p); }
                default Vector offset(View v, Pos p) { return this.block().offset(this, v, p); }
                default Fluid fluid() { return null; }
                """,
            "Shapes": """
                private static final Shape FULL = build(new Bits(1, 1, 1));
                private static final Shape EMPTY = build(new Bits(0, 0, 0));
                public static Shape full() { return FULL; }
                public static Shape empty() { return EMPTY; }
                """,
            "Shape": "public List<Box> boxes() { return null; }",
            "Box": "public final double a; public final double b; public final double c; public final double d; public final double e; public final double f;",
            "Vector": "public final double x; public final double y; public final double z;",
            "View": "State state(Pos p); Fluid fluid(Pos p); Entity entity(Pos p);",
        }
        return symbols, sources

    def test_resolves_source_roles_without_assuming_obfuscated_names(self) -> None:
        symbols, sources = self.fixture()
        actual = resolve_sources(symbols, sources)
        self.assertEqual("outline", actual["outline"])
        self.assertEqual("interaction", actual["interaction"])
        self.assertEqual("offset", actual["offset"])
        self.assertEqual("boxes", actual["to_boxes"])
        self.assertEqual(["a", "b", "c", "d", "e", "f"], actual["box_fields"])
        self.assertEqual("Fluid", actual["fluid_type"])
        self.assertIsNone(actual["context"])

    def test_refuses_ambiguous_outline_binding(self) -> None:
        symbols, sources = self.fixture()
        sources["State"] += "default Shape other(View v, Pos p) { return this.block().outline(this, v, p); }"
        with self.assertRaisesRegex(ValueError, "outline"):
            resolve_sources(symbols, sources)

    def test_refuses_incomplete_box_coordinates(self) -> None:
        symbols, sources = self.fixture()
        sources["Box"] = sources["Box"].replace("public final double f;", "")
        with self.assertRaisesRegex(ValueError, "coordinates"):
            resolve_sources(symbols, sources)


if __name__ == "__main__":
    unittest.main()
