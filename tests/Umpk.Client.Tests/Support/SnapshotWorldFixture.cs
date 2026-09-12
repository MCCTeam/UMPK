using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;

namespace Umpk.Client.Tests.Support;

/// <summary>A small in-memory <see cref="World"/> fixture for the pure <c>Umpk.Client.Snapshots</c> statics (<c>BlockSnapshot</c>, <c>SurfaceRegionSnapshot</c>, <c>ChunkStatusGrid</c>): four distinguishable states (air, stone, water, a waterlogged fence) so tests can assert on real <see cref="BlockState"/> flags and block ids without pulling in a version's generated data.</summary>
internal static class SnapshotWorldFixture
{
    internal const int AirState = 0;
    internal const int StoneState = 1;
    internal const int WaterState = 2;
    internal const int WaterloggedFenceState = 3;

    internal static SnapshotBlockDataSource BlockData { get; } = new();

    internal static Registry<BiomeDefinition> Biomes { get; } = BuildBiomes();

    internal static World NewWorld() => new(Overworld(), BlockData, Biomes);

    internal static DimensionState Overworld() =>
        new(DimensionType(-64, 384, hasSkylight: true), Identifier.Minecraft("overworld"));

    private static RegistryEntry<DimensionTypeDefinition> DimensionType(int minY, int height, bool hasSkylight)
    {
        var reg = Registry.FromEntries(
            RegistryIds.DimensionType,
            [0],
            [Identifier.Minecraft("overworld")],
            [new DimensionTypeDefinition(minY, height, hasSkylight)]);
        return reg[0];
    }

    private static Registry<BiomeDefinition> BuildBiomes() =>
        new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 1)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Build();
}

/// <summary>A permissive block data source: state 0 is air, state 1 stone, state 2 water (fluid), state 3 an oak fence carrying <see cref="BlockFlags.Waterlogged"/> so tests can tell two non-air states apart by their flags. Every other id degrades to stone, matching the permissiveness of the Game-package world fixture this mirrors.</summary>
internal sealed class SnapshotBlockDataSource : IBlockDataSource
{
    public SnapshotBlockDataSource()
    {
        Blocks = new RegistryBuilder<BlockDefinition>(RegistryIds.Block, 4)
            .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0))
            .Add(1, Identifier.Minecraft("stone"), new BlockDefinition(1, 1, 1))
            .Add(2, Identifier.Minecraft("water"), new BlockDefinition(2, 2, 2))
            .Add(3, Identifier.Minecraft("oak_fence"), new BlockDefinition(3, 3, 3))
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
        1 => 1,
        2 => 2,
        3 => 3,
        _ => 1,
    };

    public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

    public BlockFlags GetFlags(int stateId) => stateId switch
    {
        0 => BlockFlags.Air,
        2 => BlockFlags.Fluid,
        3 => BlockFlags.BlocksMotion | BlockFlags.Waterlogged,
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
