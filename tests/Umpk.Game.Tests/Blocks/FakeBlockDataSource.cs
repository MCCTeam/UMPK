using Umpk.Game.Blocks;
using Umpk.Game.Registries;

namespace Umpk.Game.Tests.Blocks;

/// <summary>A small modern-shaped (flattened) in-memory <see cref="IBlockDataSource"/> for tests. It models three blocks whose contiguous state ids carry flags, friction, and a per-state property map, mirroring the production data source's contract without any generated blobs.</summary>
internal sealed class FakeBlockDataSource : IBlockDataSource
{
    // stateId -> (blockNetworkId, flags, friction, propertyValues)
    private readonly BlockDefinition[] _defs;
    private readonly int[] _stateToBlock;
    private readonly BlockFlags[] _flags;
    private readonly float[] _friction;
    private readonly IReadOnlyList<string>[] _propNames;
    private readonly IReadOnlyDictionary<string, string>[] _propValues;

    /// <summary>The ladder state. Climbable, and not scaffolding.</summary>
    public const int LadderStateId = 6;

    /// <summary>The vine state. Climbable, and not scaffolding.</summary>
    public const int VineStateId = 7;

    /// <summary>The scaffolding state. Climbable, and the one that IS scaffolding.</summary>
    public const int ScaffoldingStateId = 8;

    /// <summary>The powder-snow state. NO flags at all, which is what the real dataset carries for it: vanilla's Its collision shape is empty for a non-entity context, and DataGen derives <c>BlocksMotion</c>/<c>Solid</c> from that shape.</summary>
    public const int PowderSnowStateId = 9;

    /// <summary>The snow LAYER state. A different block with a confusingly similar name.</summary>
    public const int SnowLayerStateId = 10;

    public FakeBlockDataSource()
    {
        // Registry: air, stone, oak_stairs, ladder, vine, scaffolding, powder_snow, snow. Entries 3-5 exist so the climbable family can be told apart by NAME as well as by flag: all three carry BlockFlags.Climbable and exactly one of them is scaffolding. Entries 6-7 do the same for powder snow: both are named "snow"-ish, only one is the block whose collision shape depends on the boots the body is wearing.
        Blocks = new RegistryBuilder<BlockDefinition>(RegistryIds.Block, 8)
            .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0))
            .Add(1, Identifier.Minecraft("stone"), new BlockDefinition(1, 1, 1))
            // oak_stairs owns states 2..5 with default 3; one "facing" property.
            .Add(2, Identifier.Minecraft("oak_stairs"), new BlockDefinition(2, 5, 3))
            .Add(3, Identifier.Minecraft("ladder"), new BlockDefinition(3, LadderStateId, LadderStateId))
            .Add(4, Identifier.Minecraft("vine"), new BlockDefinition(4, VineStateId, VineStateId))
            .Add(
                5,
                Identifier.Minecraft("scaffolding"),
                new BlockDefinition(5, ScaffoldingStateId, ScaffoldingStateId))
            .Add(
                6,
                Identifier.Minecraft("powder_snow"),
                new BlockDefinition(6, PowderSnowStateId, PowderSnowStateId))
            .Add(7, Identifier.Minecraft("snow"), new BlockDefinition(7, SnowLayerStateId, SnowLayerStateId))
            .Build();

        _defs =
        [
            Blocks[0].Value, Blocks[1].Value, Blocks[2].Value, Blocks[3].Value, Blocks[4].Value, Blocks[5].Value,
            Blocks[6].Value, Blocks[7].Value,
        ];

        // 11 states: 0 air, 1 stone, 2..5 stairs facing north/east/south/west, 6 ladder, 7 vine, 8 scaffolding, 9 powder snow, 10 snow layer.
        _stateToBlock = [0, 1, 2, 2, 2, 2, 3, 4, 5, 6, 7];
        _flags =
        [
            BlockFlags.Air,
            BlockFlags.Solid | BlockFlags.BlocksMotion,
            BlockFlags.BlocksMotion,
            BlockFlags.BlocksMotion,
            BlockFlags.BlocksMotion,
            BlockFlags.BlocksMotion | BlockFlags.Waterlogged,
            BlockFlags.Climbable,
            BlockFlags.Climbable,
            BlockFlags.Climbable | BlockFlags.BlocksMotion,
            BlockFlags.None,
            BlockFlags.BlocksMotion,
        ];
        _friction = [0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f];

        IReadOnlyList<string> none = Array.Empty<string>();
        IReadOnlyList<string> facing = new[] { "facing" };
        _propNames = [none, none, facing, facing, facing, facing, none, none, none, none, none];

        IReadOnlyDictionary<string, string> empty = new Dictionary<string, string>();
        _propValues =
        [
            empty,
            empty,
            new Dictionary<string, string> { ["facing"] = "north" },
            new Dictionary<string, string> { ["facing"] = "east" },
            new Dictionary<string, string> { ["facing"] = "south" },
            new Dictionary<string, string> { ["facing"] = "west" },
            empty,
            empty,
            empty,
            empty,
            empty,
        ];
    }

    public Registry<BlockDefinition> Blocks { get; }

    public int UnknownStateId => 0;

    public bool IsLegacy => false;

    public int StateCount => _stateToBlock.Length;

    public bool IsValidState(int stateId) => stateId >= 0 && stateId < _stateToBlock.Length;

    public int GetBlockNetworkId(int stateId) => IsValidState(stateId) ? _stateToBlock[stateId] : 0;

    public int GetDefaultStateId(int blockNetworkId) => _defs[blockNetworkId].DefaultStateId;

    public BlockFlags GetFlags(int stateId) => IsValidState(stateId) ? _flags[stateId] : BlockFlags.Air;

    public float GetFriction(int stateId) => IsValidState(stateId) ? _friction[stateId] : 0.6f;

    public float GetSpeedFactor(int stateId) => 1.0f;

    public float GetJumpFactor(int stateId) => 1.0f;

    public IReadOnlyList<string> GetPropertyNames(int stateId) =>
        IsValidState(stateId) ? _propNames[stateId] : Array.Empty<string>();

    public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
    {
        if (IsValidState(stateId) && _propValues[stateId].TryGetValue(propertyName, out var v))
        {
            value = v;
            return true;
        }

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
