using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Native ordinary stair placement geometry; returned states are collision representatives, not predicted fluid or world mutations.</summary>
internal readonly record struct VanillaStairPlacement(bool Supported, bool FloatHitOffset, bool DirectMathFloor)
{
    public static VanillaStairPlacement ForProtocol(int protocol) => new(protocol is >= 393 and <= 777, protocol <= 404, protocol >= 775);

    public bool Supports(Identifier item, ItemUseOnInfo use, IBlockDataSource blocks)
        => Supported && use.Kind == ItemUseOnKind.Unknown && use.PlacedBlock == item && IsStair(item)
            && blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> block) && block.Value.StateCount == 80 && block.Value.Properties.Count == 4;

    public bool HasPassiveUse(BlockState state)
        => Supported && state.IsValid && IsStair(state.Block.Id)
            && state.Block.Value.StateCount == 80 && state.Block.Value.Properties.Count == 4;

    public bool TryGetCollisionRepresentative(Identifier item, IBlockDataSource blocks, World world, EntityMembershipState membership, JukeboxPredictionState prediction,
        BlockPos clicked, BlockPos destination, Direction face, Vec3d cursor, float yaw, out BlockState representative)
    {
        representative = default;
        if (!float.IsFinite(yaw) || !blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> definition))
            return false;
        Direction facing = Facing(yaw);
        bool above = FloatHitOffset ? (float)cursor.Y > .5F : clicked.Y + cursor.Y - destination.Y > .5;
        string half = face == Direction.Down || face != Direction.Up && above ? "top" : "bottom";
        BlockState selected = new(blocks, definition.Value.DefaultStateId);
        if (!Set("facing", Name(facing)) || !Set("half", half))
            return false;
        if (!TryShape(world, membership, prediction, destination, facing, half, out string shape) || !Set("shape", shape))
            return false;
        // All native stair grids pair identical wet/dry geometry. This dry state is only a collision representative; unknown native fluid identity is not asserted empty.
        if (!Set("waterlogged", "false"))
            return false;
        representative = selected;
        return true;

        bool Set(string property, string value)
        {
            if (!JukeboxPredictionState.TryPropertyState(selected, property, value, out int id))
                return false;
            selected = new BlockState(blocks, id);
            return true;
        }
    }

    private static bool TryShape(World world, EntityMembershipState membership, JukeboxPredictionState prediction, BlockPos position, Direction facing, string half, out string shape)
    {
        shape = "straight";
        if (!Read(position.Offset(facing), out BlockState forward))
            return false;
        if (!TryOtherStair(forward, out bool forwardStair, out Direction forwardFacing, out string forwardHalf))
            return false;
        if (forwardStair && forwardHalf == half && forwardFacing.GetAxis() != facing.GetAxis())
        {
            if (!CanTake(forwardFacing.Opposite(), out bool allowed))
                return false;
            if (allowed)
            {
                shape = forwardFacing == CounterClockwise(facing) ? "outer_left" : "outer_right";
                return true;
            }
        }
        if (!Read(position.Offset(facing.Opposite()), out BlockState backward))
            return false;
        if (!TryOtherStair(backward, out bool backwardStair, out Direction backwardFacing, out string backwardHalf))
            return false;
        if (backwardStair && backwardHalf == half && backwardFacing.GetAxis() != facing.GetAxis())
        {
            if (!CanTake(backwardFacing, out bool allowed))
                return false;
            if (allowed)
                shape = backwardFacing == CounterClockwise(facing) ? "inner_left" : "inner_right";
        }
        return true;

        bool Read(BlockPos cell, out BlockState state)
        {
            state = default;
            if (world.GetColumn(cell) is null || !membership.HasColumn(cell))
                return false;
            state = prediction.GetBlock(world, cell);
            return state.IsValid;
        }

        bool CanTake(Direction side, out bool allowed)
        {
            allowed = false;
            if (!Read(position.Offset(side), out BlockState neighbor)
                || !TryOtherStair(neighbor, out bool stair, out Direction direction, out string neighborHalf))
                return false;
            allowed = !stair || direction != facing || neighborHalf != half;
            return true;
        }
    }

    private static bool TryOtherStair(BlockState state, out bool stair, out Direction facing, out string half)
    {
        stair = IsStair(state.Block.Id);
        facing = default;
        half = string.Empty;
        if (!stair)
            return true;
        return state.TryGetProperty("facing", out string value) && TryFacing(value, out facing)
            && state.TryGetProperty("half", out half) && half is "top" or "bottom";
    }

    private Direction Facing(float yaw)
    {
        // Native Direction.fromYRot promotes the observed float, divides by double 90 and uses Java floor, including its saturating cast and wrapping decrement.
        double value = yaw / 90.0 + .5;
        if (DirectMathFloor)
            value = Math.Floor(value);
        int integral = value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
        int quadrant = unchecked(!DirectMathFloor && value < integral ? integral - 1 : integral) & 3;
        return quadrant switch { 0 => Direction.South, 1 => Direction.West, 2 => Direction.North, _ => Direction.East };
    }

    private static Direction CounterClockwise(Direction facing) => facing switch
    {
        Direction.North => Direction.West,
        Direction.West => Direction.South,
        Direction.South => Direction.East,
        _ => Direction.North,
    };

    private static bool TryFacing(string name, out Direction facing)
    {
        facing = name switch { "north" => Direction.North, "south" => Direction.South, "west" => Direction.West, "east" => Direction.East, _ => Direction.Down };
        return facing != Direction.Down;
    }

    private static string Name(Direction facing) => facing switch
    {
        Direction.North => "north",
        Direction.South => "south",
        Direction.West => "west",
        _ => "east",
    };

    // Original registrations/factories and inherited stair rules cover these exact names on protocols 393–777. A suffix or schema alone does not establish native participation.
    private static bool IsStair(Identifier item) => item.IsMinecraft && item.Path is
        "acacia_stairs"
        or "andesite_stairs"
        or "bamboo_mosaic_stairs"
        or "bamboo_stairs"
        or "birch_stairs"
        or "black_concrete_stairs"
        or "black_wool_stairs"
        or "blackstone_stairs"
        or "blue_concrete_stairs"
        or "blue_wool_stairs"
        or "brick_stairs"
        or "brown_concrete_stairs"
        or "brown_wool_stairs"
        or "cherry_stairs"
        or "cinnabar_brick_stairs"
        or "cinnabar_stairs"
        or "cobbled_deepslate_stairs"
        or "cobblestone_stairs"
        or "crimson_stairs"
        or "cut_copper_stairs"
        or "cyan_concrete_stairs"
        or "cyan_wool_stairs"
        or "dark_oak_stairs"
        or "dark_prismarine_stairs"
        or "deepslate_brick_stairs"
        or "deepslate_tile_stairs"
        or "diorite_stairs"
        or "end_stone_brick_stairs"
        or "exposed_cut_copper_stairs"
        or "granite_stairs"
        or "gray_concrete_stairs"
        or "gray_wool_stairs"
        or "green_concrete_stairs"
        or "green_wool_stairs"
        or "jungle_stairs"
        or "light_blue_concrete_stairs"
        or "light_blue_wool_stairs"
        or "light_gray_concrete_stairs"
        or "light_gray_wool_stairs"
        or "lime_concrete_stairs"
        or "lime_wool_stairs"
        or "magenta_concrete_stairs"
        or "magenta_wool_stairs"
        or "mangrove_stairs"
        or "mossy_cobblestone_stairs"
        or "mossy_stone_brick_stairs"
        or "mud_brick_stairs"
        or "nether_brick_stairs"
        or "oak_stairs"
        or "orange_concrete_stairs"
        or "orange_wool_stairs"
        or "oxidized_cut_copper_stairs"
        or "pale_oak_stairs"
        or "pink_concrete_stairs"
        or "pink_wool_stairs"
        or "polished_andesite_stairs"
        or "polished_blackstone_brick_stairs"
        or "polished_blackstone_stairs"
        or "polished_cinnabar_stairs"
        or "polished_deepslate_stairs"
        or "polished_diorite_stairs"
        or "polished_granite_stairs"
        or "polished_sulfur_stairs"
        or "polished_tuff_stairs"
        or "poplar_stairs"
        or "prismarine_brick_stairs"
        or "prismarine_stairs"
        or "purple_concrete_stairs"
        or "purple_wool_stairs"
        or "purpur_stairs"
        or "quartz_stairs"
        or "red_concrete_stairs"
        or "red_nether_brick_stairs"
        or "red_sandstone_stairs"
        or "red_wool_stairs"
        or "resin_brick_stairs"
        or "sandstone_stairs"
        or "smooth_quartz_stairs"
        or "smooth_red_sandstone_stairs"
        or "smooth_sandstone_stairs"
        or "spruce_stairs"
        or "stone_brick_stairs"
        or "stone_stairs"
        or "sulfur_brick_stairs"
        or "sulfur_stairs"
        or "tuff_brick_stairs"
        or "tuff_stairs"
        or "warped_stairs"
        or "waxed_cut_copper_stairs"
        or "waxed_exposed_cut_copper_stairs"
        or "waxed_oxidized_cut_copper_stairs"
        or "waxed_weathered_cut_copper_stairs"
        or "weathered_cut_copper_stairs"
        or "white_concrete_stairs"
        or "white_wool_stairs"
        or "yellow_concrete_stairs"
        or "yellow_wool_stairs";
}
