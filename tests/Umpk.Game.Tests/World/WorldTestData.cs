using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;

namespace Umpk.Game.Tests.World;

/// <summary>Shared test scaffolding for world-storage tests: a permissive block data source where state id 0 is air and every other id is a solid, motion-blocking block, plus factory helpers for building a world/dimension/biome registry without any generated blobs.</summary>
internal static class WorldTestData
{
    internal const int AirState = 0;
    internal const int StoneState = 1;
    internal const int WaterState = 2;
    internal const int RedBedState = 3;
    internal const int BlueBedState = 4;

    internal static WorldBlockDataSource BlockData { get; } = new();

    internal static Registry<BiomeDefinition> Biomes { get; } = BuildBiomes();

    internal static DimensionState Overworld() =>
        new(DimensionType(-64, 384, hasSkylight: true), Identifier.Minecraft("overworld"));

    internal static RegistryEntry<DimensionTypeDefinition> DimensionType(int minY, int height, bool hasSkylight)
    {
        var reg = Registry.FromEntries(
            RegistryIds.DimensionType,
            [0],
            [Identifier.Minecraft("overworld")],
            [new DimensionTypeDefinition(minY, height, hasSkylight)]);
        return reg[0];
    }

    internal static Umpk.Game.World.World NewWorld() => new(Overworld(), BlockData, Biomes);

    private static Registry<BiomeDefinition> BuildBiomes() =>
        new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 3)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Add(1, Identifier.Minecraft("desert"), new BiomeDefinition())
            .Add(2, Identifier.Minecraft("ocean"), new BiomeDefinition())
            .Build();
}

/// <summary>A permissive block data source for world tests: state 0 is air, state 2 is water (fluid), and every other state id is solid and motion-blocking. Any id is "valid" so tests can write arbitrary ids through the paletted storage without palette-range constraints.</summary>
internal sealed class WorldBlockDataSource : IBlockDataSource
{
    public WorldBlockDataSource()
    {
        Blocks = new RegistryBuilder<BlockDefinition>(RegistryIds.Block, 5)
            .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0))
            .Add(1, Identifier.Minecraft("stone"), new BlockDefinition(1, 1, 1))
            .Add(2, Identifier.Minecraft("water"), new BlockDefinition(2, 2, 2))
            .Add(3, Identifier.Minecraft("red_bed"), new BlockDefinition(3, 3, 3))
            .Add(4, Identifier.Minecraft("blue_bed"), new BlockDefinition(4, 4, 4))
            .Build();
    }

    public Registry<BlockDefinition> Blocks { get; }

    public int UnknownStateId => 0;

    public bool IsLegacy => false;

    public int StateCount => int.MaxValue;

    public bool IsValidState(int stateId) => stateId >= 0;

    public int GetBlockNetworkId(int stateId) => stateId switch
    {
        0 => 0,
        2 => 2,
        3 => 3,
        4 => 4,
        _ => 1,
    };

    public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

    public BlockFlags GetFlags(int stateId) => stateId switch
    {
        0 => BlockFlags.Air,
        2 => BlockFlags.Fluid,
        _ => BlockFlags.Solid | BlockFlags.BlocksMotion,
    };

    public float GetFriction(int stateId) => 0.6f;

    public float GetSpeedFactor(int stateId) => 1.0f;

    public float GetJumpFactor(int stateId) => 1.0f;

    public IReadOnlyList<string> GetPropertyNames(int stateId) => Array.Empty<string>();

    public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
    {
        value = string.Empty;
        return false;
    }

    public bool TryDecodeLegacy(int stateId, out int blockId, out int meta)
    {
        blockId = 0;
        meta = 0;
        return false;
    }

    public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | (meta & 0xF);
}
