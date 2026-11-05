import unittest


class ContextInteractionBindingsTests(unittest.TestCase):
    def test_context_placement_binds_the_native_block_and_item_steps(self) -> None:
        from resolve_context_interactions import resolve_sources

        for context in (False, True):
            symbols, sources, outline = fixture(context)
            with self.subTest(collision_context=context):
                result = resolve_sources(symbols, sources, outline)
                self.assertEqual("PlaceContext", result["placement_context"])
                self.assertEqual("UseContext", result["use_context"])
                self.assertEqual("activate", result["use"]["name"])
                self.assertEqual("replace", result["replace"]["name"])
                self.assertEqual("replaceable", result["material_replaceable"])
                self.assertEqual(["place", "survive", "shape", "collision"], [row["name"] for row in result["block_steps"]])
                self.assertEqual({"use", "place", "placementState", "canPlace", "placeBlock", "getBlock"}, {row["name"] for row in result["item_steps"]})
                self.assertEqual("edible" if context else None, result["edible"])

    def test_changed_passive_block_or_item_use_is_rejected(self) -> None:
        from resolve_context_interactions import resolve_sources

        for owner, old, new in (("Block", "return false;", "return true;"), ("Item", "return Result.PASS;", "return Result.SUCCESS;")):
            symbols, sources, outline = fixture(False)
            sources[owner] = sources[owner].replace(old, new)
            with self.subTest(owner=owner), self.assertRaises(ValueError):
                resolve_sources(symbols, sources, outline)


def fixture(context: bool) -> tuple[dict, dict[str, str], dict]:
    suffix = ", CollisionContext c" if context else ""
    argument = ", c" if context else ""
    symbols = {"block": "Block", "state": "State", "item": "Item", "stack": "Stack", "player": "Player", "material": "material"}
    outline = {"getter": "Getter", "position": "Pos", "shape": "Shape", "shape_utility": "Shapes", "outline": "outline", "context": "CollisionContext" if context else None, "context_empty": "empty" if context else None, "to_boxes": "boxes", "box_type": "Box", "box_fields": list("abcdef")}
    body = "Result result = this.place(new PlaceContext(ctx)); return result != Result.SUCCESS && this.edible() ? this.eat(ctx.world, ctx.player, ctx.hand).result() : result;" if context else "return this.place(new PlaceContext(ctx));"
    sources = {
        "Block": """
public class Block {
 public static Block fromItem(Item item) { return item instanceof BlockItem ? ((BlockItem)item).getBlock() : null; }
 public boolean activate(State state, World world, Pos pos, Player player, Hand hand, Hit hit) { return false; }
 public boolean replace(State state, PlaceContext ctx) { return this.material.replaceable() && ctx.stack().item() != this.item(); }
 public boolean survive(State state, Reader world, Pos pos) { return true; }
 public State place(PlaceContext ctx) { return this.defaultState(); }
 public Shape shape(State state, Getter world, Pos posSUFFIX) { return Shapes.full(); }
 public Shape collision(State state, Getter world, Pos posSUFFIX) { return this.collidable ? state.outline(world, pos) : Shapes.empty(); }
 public Shape interaction(State state, Getter world, Pos pos) { return Shapes.empty(); }
}
""".replace("SUFFIX", suffix),
        "State": "public class State { public Shape outline(Getter world, Pos posSUFFIX) { return this.block().shape(this, world, posARGUMENT); } }".replace("SUFFIX", suffix).replace("ARGUMENT", argument),
        "Item": "public class Item { public Result use(UseContext ctx) { return Result.PASS; } }",
        "Result": "public enum Result { SUCCESS, PASS, FAIL; }",
        "BlockItem": """
public class BlockItem extends Item {
 public Result use(UseContext ctx) { BODY }
 public Result place(PlaceContext ctx) { return Result.SUCCESS; }
 protected State placementState(PlaceContext ctx) { return this.getBlock().place(ctx); }
 protected boolean canPlace(PlaceContext ctx, State state) { return state.survive(ctx.world(), ctx.pos()); }
 protected boolean placeBlock(PlaceContext ctx, State state) { return ctx.world().set(ctx.pos(), state, 11); }
 public Block getBlock() { return this.block; }
}
""".replace("BODY", body),
    }
    return symbols, sources, outline


if __name__ == "__main__":
    unittest.main()
