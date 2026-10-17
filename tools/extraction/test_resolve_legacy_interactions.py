import unittest


class LegacyInteractionBindingsTests(unittest.TestCase):
    def test_native_argument_placement_contract_is_resolved_without_neighbor_assumptions(self) -> None:
        from resolve_legacy_interactions import resolve_sources

        symbols, sources, outline = fixture()
        result = resolve_sources(symbols, sources, outline)
        self.assertEqual("BlockItem", result["item_block"])
        self.assertEqual("activate", result["use"]["name"])
        self.assertEqual("replace", result["replace"]["name"])
        self.assertEqual(["place", "survive", "side", "collision", "bounds"], [row["name"] for row in result["block_steps"]])
        self.assertEqual("collision", result["collision"]["name"])
        self.assertEqual("use", result["item_use"]["name"])
        self.assertEqual(["use", "canPlaceSide", "getBlock"], [row["name"] for row in result["item_steps"]])
        self.assertEqual(["a", "b", "c", "d", "e", "f"], result["box_fields"])

    def test_ambiguous_or_changed_native_base_rule_is_rejected(self) -> None:
        from resolve_legacy_interactions import resolve_sources

        for changed in ["return true;", "return World.something();"]:
            symbols, sources, outline = fixture()
            sources["Block"] = sources["Block"].replace("boolean replace(World w, Pos p) { return false; }", "boolean replace(World w, Pos p) { " + changed + " }")
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                resolve_sources(symbols, sources, outline)

    def test_immutable_collision_delegate_can_cast_the_world_to_a_getter(self) -> None:
        from resolve_legacy_interactions import resolve_sources

        symbols, sources, outline = fixture()
        outline.update(mutable_bounds=False, box_type="Box")
        sources["Block"] = sources["Block"].replace("public Box collision(World w, Pos p, State state) { return new Box(0,0,0,1,1,1); }", "public Box shape(State s, Getter w, Pos p) { return FULL; } public Box collision(State s, World w, Pos p) { return s.shape((Getter)w,p); } public Box selected(State s, World w, Pos p) { return s.shape((Getter)w,p).offset(p); }")

        result = resolve_sources(symbols, sources, outline)

        self.assertEqual("collision", result["collision"]["name"])
        self.assertEqual(["place", "survive", "side", "shape", "collision"], [row["name"] for row in result["block_steps"]])


def fixture() -> tuple[dict, dict[str, str], dict]:
    symbols = {"block": "Block", "state": "State", "item": "Item", "stack": "Stack", "player": "Player"}
    outline = {"world": "World", "position": "Pos", "getter": "Getter", "direction_type": "Direction", "mutable_bounds": True, "bounds_update": "bounds"}
    sources = {
        "Block": """
public class Block {
 public static Block fromItem(Item item) { return item instanceof BlockItem ? ((BlockItem)item).getBlock() : null; }
 public boolean activate(World w, Pos p, State s, Player player, Direction d, float x, float y, float z) { return false; }
 public boolean replace(World w, Pos p) { return false; }
 public boolean survive(World w, Pos p) { return w.state(p).block().material.replaceable(); }
 public boolean side(World w, Pos p, Direction d) { return this.survive(w, p); }
 public State place(World w, Pos p, Direction d, float x, float y, float z, int meta, Living player) { return this.state(meta); }
 public Box collision(World w, Pos p, State state) { return new Box(0,0,0,1,1,1); }
 public void bounds(Getter w, Pos p) { }
}
""",
        "Item": "public class Item { public boolean use(Stack stack, Player p, World w, Pos pos, Direction d, float x, float y, float z) { return false; } }",
        "BlockItem": "public class BlockItem extends Item { public boolean use(Stack stack, Player p, World w, Pos pos, Direction d, float x, float y, float z) { return true; } public boolean canPlaceSide(World w, Pos pos, Direction d, Player player, Stack stack) { return w.canPlace(this.block,pos,d); } public Block getBlock() { return this.block; } }",
        "Box": "public class Box { public final double a; public final double b; public final double c; public final double d; public final double e; public final double f; }",
    }
    return symbols, sources, outline


if __name__ == "__main__":
    unittest.main()
