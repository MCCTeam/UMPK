using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>The original flattened SlabBlock placement rules. Legacy ItemSlab prechecks have a separate unresolved mutable-box phase.</summary>
internal readonly record struct VanillaSlabPlacement(bool Supported, bool FloatHitOffset)
{
    public static VanillaSlabPlacement ForProtocol(int protocol) => new(protocol is >= 393 and <= 777, protocol <= 404);

    public bool Supports(Identifier item, ItemUseOnInfo use)
        => Supported && use.Kind == ItemUseOnKind.Unknown && use.PlacedBlock == item && IsKnownSlab(item);

    public bool AboveHalf(BlockPos clicked, Vec3d cursor)
        => FloatHitOffset ? (float)cursor.Y > 0.5F : clicked.Y + cursor.Y - clicked.Y > 0.5;

    public static bool TryGetType(BlockState state, out string type)
        => state.TryGetProperty("type", out type) && type is "bottom" or "top" or "double";

    public static bool ReplacesClicked(string type, Direction face, bool aboveHalf)
        => type == "bottom" ? face == Direction.Up || aboveHalf && face is not (Direction.Up or Direction.Down)
            : type == "top" && (face == Direction.Down || !aboveHalf && face is not (Direction.Up or Direction.Down));

    public static bool TryGetCollisionShape(IBlockDataSource blocks, Identifier slab, string type, IBlockShapeSource shapes, out Aabb shape)
    {
        shape = type switch
        {
            "bottom" => new(0, 0, 0, 1, 0.5, 1),
            "top" => new(0, 0.5, 0, 1, 1, 1),
            _ => new(0, 0, 0, 1, 1, 1),
        };
        if (!blocks.Blocks.TryGet(slab, out RegistryEntry<BlockDefinition> block))
            return false;
        // Waterlogged changes the fluid state, not this slab's collision shape. Read the canonical dry representative of the selected result, including the resulting full double slab.
        for (int state = block.Value.MinStateId; state <= block.Value.MaxStateId; state++)
            if (block.Value.TryGetPropertyValue(state, "type", out string half) && half == type
                && block.Value.TryGetPropertyValue(state, "waterlogged", out string water) && water == "false")
            {
                ReadOnlySpan<Aabb> collision = shapes.GetCollisionShapes(new BlockState(blocks, state));
                return collision.Length == 1 && collision[0] == shape;
            }
        return false;
    }

    // Original registrations and inherited slab rules cover these exact names on393–777. A specialized Unknown item, a suffix, or a matching property alone cannot establish native participation.
    private static bool IsKnownSlab(Identifier item) => item.IsMinecraft && item.Path is
        "acacia_slab"
        or "andesite_slab"
        or "bamboo_mosaic_slab"
        or "bamboo_slab"
        or "birch_slab"
        or "black_concrete_slab"
        or "black_wool_slab"
        or "blackstone_slab"
        or "blue_concrete_slab"
        or "blue_wool_slab"
        or "brick_slab"
        or "brown_concrete_slab"
        or "brown_wool_slab"
        or "cherry_slab"
        or "cinnabar_brick_slab"
        or "cinnabar_slab"
        or "cobbled_deepslate_slab"
        or "cobblestone_slab"
        or "crimson_slab"
        or "cut_copper_slab"
        or "cut_red_sandstone_slab"
        or "cut_sandstone_slab"
        or "cyan_concrete_slab"
        or "cyan_wool_slab"
        or "dark_oak_slab"
        or "dark_prismarine_slab"
        or "deepslate_brick_slab"
        or "deepslate_tile_slab"
        or "diorite_slab"
        or "end_stone_brick_slab"
        or "exposed_cut_copper_slab"
        or "granite_slab"
        or "gray_concrete_slab"
        or "gray_wool_slab"
        or "green_concrete_slab"
        or "green_wool_slab"
        or "jungle_slab"
        or "light_blue_concrete_slab"
        or "light_blue_wool_slab"
        or "light_gray_concrete_slab"
        or "light_gray_wool_slab"
        or "lime_concrete_slab"
        or "lime_wool_slab"
        or "magenta_concrete_slab"
        or "magenta_wool_slab"
        or "mangrove_slab"
        or "mossy_cobblestone_slab"
        or "mossy_stone_brick_slab"
        or "mud_brick_slab"
        or "nether_brick_slab"
        or "oak_slab"
        or "orange_concrete_slab"
        or "orange_wool_slab"
        or "oxidized_cut_copper_slab"
        or "pale_oak_slab"
        or "petrified_oak_slab"
        or "pink_concrete_slab"
        or "pink_wool_slab"
        or "polished_andesite_slab"
        or "polished_blackstone_brick_slab"
        or "polished_blackstone_slab"
        or "polished_cinnabar_slab"
        or "polished_deepslate_slab"
        or "polished_diorite_slab"
        or "polished_granite_slab"
        or "polished_sulfur_slab"
        or "polished_tuff_slab"
        or "poplar_slab"
        or "prismarine_brick_slab"
        or "prismarine_slab"
        or "purple_concrete_slab"
        or "purple_wool_slab"
        or "purpur_slab"
        or "quartz_slab"
        or "red_concrete_slab"
        or "red_nether_brick_slab"
        or "red_sandstone_slab"
        or "red_wool_slab"
        or "resin_brick_slab"
        or "sandstone_slab"
        or "smooth_quartz_slab"
        or "smooth_red_sandstone_slab"
        or "smooth_sandstone_slab"
        or "smooth_stone_slab"
        or "spruce_slab"
        or "stone_brick_slab"
        or "stone_slab"
        or "sulfur_brick_slab"
        or "sulfur_slab"
        or "tuff_brick_slab"
        or "tuff_slab"
        or "warped_slab"
        or "waxed_cut_copper_slab"
        or "waxed_exposed_cut_copper_slab"
        or "waxed_oxidized_cut_copper_slab"
        or "waxed_weathered_cut_copper_slab"
        or "weathered_cut_copper_slab"
        or "white_concrete_slab"
        or "white_wool_slab"
        or "yellow_concrete_slab"
        or "yellow_wool_slab";
}
