using Umpk.Game.Blocks;
using Umpk.Game.Registries;

namespace Umpk.Physics.Tests.Fixtures;

/// <summary>A hand-built <see cref="IBlockDataSource"/> over the <see cref="BlockKind"/> set: one state id per kind, with the flags/scalars the engine reads (friction, speed factor, jump factor, fluid, climbable). State id == the enum's numeric value. This is the tests' voxel block table; it does not use generated data.</summary>
public sealed class FixtureBlockData : IBlockDataSource
{
    private static readonly string[] Paths =
    [
        "air", "stone", "ice", "blue_ice", "slime_block", "soul_sand",
        "honey_block", "ladder", "water", "lava", "glass", "smooth_stone_slab",
        "flowing_water", "flowing_lava",
        // Distinct registry keys, because the fixture registry is keyed by identifier. The engine classifies a fluid by "is it a fluid whose path mentions lava", not by exact key, so these are water and lava for every rule under test.
        "water_level_1", "water_level_2", "water_level_3", "water_level_4",
        "water_level_5", "water_level_7", "water_falling",
        "lava_level_1", "lava_level_2",
        "kelp", "scaffolding", "cobweb",
        // Two registry keys for one block, because the fixture registry is keyed by identifier and the engine reads `drag` off the state; GetBlockNetworkId maps both onto the first, so both answer minecraft:bubble_column to a path compare.
        "bubble_column", "bubble_column_down", "hay_block", "slime", "soul_soil", "powder_snow",
        "pointed_dripstone", "bamboo",
    ];

    private static readonly string[] LevelPropertyNames = ["level"];

    private static readonly string[] DragPropertyNames = ["drag"];

    /// <summary>The block-level value each levelled fluid kind carries. Level 0 maps to a source (amount 8), 1-7 to flowing amounts 7-1 and 8 or more to the FALLING state, so this table is what makes a fixture trench have a real height gradient for the flow vector.</summary>
    private static readonly Dictionary<BlockKind, string> FluidLevels = new()
    {
        [BlockKind.FlowingWater] = "6",
        [BlockKind.FlowingLava] = "6",
        [BlockKind.WaterLevel1] = "1",
        [BlockKind.WaterLevel2] = "2",
        [BlockKind.WaterLevel3] = "3",
        [BlockKind.WaterLevel4] = "4",
        [BlockKind.WaterLevel5] = "5",
        [BlockKind.WaterLevel7] = "7",
        [BlockKind.FallingWater] = "8",
        [BlockKind.LavaLevel1] = "1",
        [BlockKind.LavaLevel2] = "2",
    };

    private readonly Registry<BlockDefinition> _blocks;

    public FixtureBlockData()
    {
        int n = Paths.Length;
        var networkIds = new int[n];
        var keys = new Identifier[n];
        var values = new BlockDefinition[n];
        for (int i = 0; i < n; i++)
        {
            networkIds[i] = i;
            keys[i] = Identifier.Minecraft(Paths[i]);
            values[i] = new BlockDefinition(i, i, i);
        }

        _blocks = Registry.FromEntries<BlockDefinition>(Identifier.Minecraft("block"), networkIds, keys, values);
    }

    public Registry<BlockDefinition> Blocks => _blocks;

    public int UnknownStateId => (int)BlockKind.Air;

    public bool IsLegacy => false;

    public int StateCount => Paths.Length;

    public bool IsValidState(int stateId) => stateId >= 0 && stateId < Paths.Length;

    public int GetBlockNetworkId(int stateId) => (BlockKind)stateId switch
    {
        // Both column states are the SAME block, minecraft:bubble_column, and the engine tells them apart by the `drag` property exactly as it does on real data. The extra registry key exists only because this fixture's key space is one-per-state; nothing reads it.
        BlockKind.BubbleColumnDown => (int)BlockKind.BubbleColumnUp,
        _ => IsValidState(stateId) ? stateId : UnknownStateId,
    };

    public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

    public BlockFlags GetFlags(int stateId) => (BlockKind)stateId switch
    {
        BlockKind.Air => BlockFlags.Air | BlockFlags.Replaceable,
        BlockKind.Water => BlockFlags.Fluid | BlockFlags.Replaceable,
        BlockKind.Lava => BlockFlags.Fluid | BlockFlags.Replaceable,
        BlockKind.FlowingWater => BlockFlags.Fluid | BlockFlags.Replaceable,
        BlockKind.FlowingLava => BlockFlags.Fluid | BlockFlags.Replaceable,
        BlockKind.WaterLevel1 or BlockKind.WaterLevel2 or BlockKind.WaterLevel3
            or BlockKind.WaterLevel4 or BlockKind.WaterLevel5 or BlockKind.WaterLevel7
            or BlockKind.FallingWater
            or BlockKind.LavaLevel1 or BlockKind.LavaLevel2 => BlockFlags.Fluid | BlockFlags.Replaceable,
        BlockKind.Ladder => BlockFlags.Climbable,
        // Climbable AND motion-blocking: the generated table gives scaffolding shape 309, which is neither empty nor a single unit cube, so BlocksMotion is set and Solid is not.
        BlockKind.Scaffolding => BlockFlags.Climbable | BlockFlags.BlocksMotion,
        // Cobweb's collision ref is the empty shape, so ClassifyShape gives it neither BlocksMotion nor Solid; it is air with a hook.
        BlockKind.Cobweb => BlockFlags.None,
        // Powder snow's collision ref is the empty shape too, so it carries neither BlocksMotion nor Solid. To the shape table it is indistinguishable from a flower; only the NAME separates it, which is why BlockState.IsPowderSnow is a registry-path compare.
        BlockKind.PowderSnow => BlockFlags.None,
        // Waterlogged, no Fluid and no collision shape: what BlockAttributeResolver emits once shared/block-attributes.json's intrinsically_waterlogged list carries bubble_column.
        BlockKind.BubbleColumnUp or BlockKind.BubbleColumnDown => BlockFlags.Waterlogged,
        // Kelp contains water but is not water: Waterlogged, no Fluid, and no collision shape, which is what the generated table emits for it.
        BlockKind.Kelp => BlockFlags.Waterlogged,
        BlockKind.Slab => BlockFlags.BlocksMotion,
        // The two offset blocks: a collision shape, so BlocksMotion, but NOT a full unit cube, so NOT Solid. That is exactly what BlockAttributeResolver.ClassifyShape emits for a 6/16 and a 3/16 column, and it is load-bearing rather than decorative: BlockShapeOffset.For guards on Solid to keep an ordinary floor off the registry path, so marking these fixtures Solid would disable the offset and make every BlockShapeOffsetTests row read zero. OffsetBlockShapeTests.OffsetBlocks_AreNotFlaggedSolid pins the same premise on the REAL dataset, so the fixture and generated data cannot drift apart on it.
        BlockKind.PointedDripstone or BlockKind.Bamboo => BlockFlags.BlocksMotion,
        _ => BlockFlags.Solid | BlockFlags.BlocksMotion,
    };

    public float GetFriction(int stateId) => (BlockKind)stateId switch
    {
        BlockKind.Ice => 0.98f,
        BlockKind.BlueIce => 0.989f,
        BlockKind.SlimeBlock or BlockKind.LegacySlimeBlock => 0.8f,
        _ => 0.6f,
    };

    public float GetSpeedFactor(int stateId) => (BlockKind)stateId switch
    {
        BlockKind.SoulSand => 0.4f,
        BlockKind.HoneyBlock => 0.4f,
        _ => 1.0f,
    };

    public float GetJumpFactor(int stateId) => (BlockKind)stateId switch
    {
        BlockKind.HoneyBlock => 0.5f,
        _ => 1.0f,
    };

    public IReadOnlyList<string> GetPropertyNames(int stateId) => (BlockKind)stateId switch
    {
        BlockKind.BubbleColumnUp or BlockKind.BubbleColumnDown => DragPropertyNames,
        _ => FluidLevels.ContainsKey((BlockKind)stateId) ? LevelPropertyNames : [],
    };

    public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
    {
        if (propertyName == "level" && FluidLevels.TryGetValue((BlockKind)stateId, out string? level))
        {
            value = level;
            return true;
        }

        if (propertyName == "drag" && (BlockKind)stateId is BlockKind.BubbleColumnUp or BlockKind.BubbleColumnDown)
        {
            value = (BlockKind)stateId == BlockKind.BubbleColumnDown ? "true" : "false";
            return true;
        }

        value = string.Empty;
        return false;
    }

    public bool TryDecodeLegacy(int stateId, out int blockId, out int meta)
    {
        blockId = -1;
        meta = -1;
        return false;
    }

    public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | meta;
}
