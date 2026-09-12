using Umpk.Game.Blocks;
using Umpk.Game.Registries;

namespace Umpk.Game.Tests.Blocks;

/// <summary>A pre-flattening (protocol 47-style) in-memory <see cref="IBlockDataSource"/>: state ids are <c>(blockId &lt;&lt; 4) | meta</c>, there are no properties, and legacy decode/encode round-trips. Models air, stone, and wool with per-meta variants collapsed to the id-level material.</summary>
internal sealed class NumericBlockDataSource : IBlockDataSource
{
    private readonly Dictionary<int, int> _blockIdToNetworkId;

    public NumericBlockDataSource()
    {
        // Network ids equal the legacy block id here.
        Blocks = new RegistryBuilder<BlockDefinition>(RegistryIds.Block, 3)
            .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 15, 0))
            .Add(1, Identifier.Minecraft("stone"), new BlockDefinition(16, 31, 16))
            .Add(35, Identifier.Minecraft("wool"), new BlockDefinition(560, 575, 560))
            .Build();

        _blockIdToNetworkId = new Dictionary<int, int> { [0] = 0, [1] = 1, [35] = 35 };
    }

    public Registry<BlockDefinition> Blocks { get; }

    public int UnknownStateId => 0;

    public bool IsLegacy => true;

    // Legacy state space is 12 bits block id + 4 bits meta.
    public int StateCount => 4096 * 16;

    public bool IsValidState(int stateId)
    {
        int blockId = stateId >> 4;
        return _blockIdToNetworkId.ContainsKey(blockId);
    }

    public int GetBlockNetworkId(int stateId)
    {
        int blockId = stateId >> 4;
        return _blockIdToNetworkId.TryGetValue(blockId, out int netId) ? netId : 0;
    }

    public int GetDefaultStateId(int blockNetworkId) => Blocks[blockNetworkId].Value.DefaultStateId;

    public BlockFlags GetFlags(int stateId)
    {
        int blockId = stateId >> 4;
        return blockId switch
        {
            0 => BlockFlags.Air,
            _ => BlockFlags.Solid | BlockFlags.BlocksMotion,
        };
    }

    public float GetFriction(int stateId) => 0.6f;

    public float GetSpeedFactor(int stateId) => 1.0f;

    public float GetJumpFactor(int stateId) => 1.0f;

    // Property access degrades gracefully on legacy data.
    public IReadOnlyList<string> GetPropertyNames(int stateId) => Array.Empty<string>();

    public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
    {
        value = string.Empty;
        return false;
    }

    public bool TryDecodeLegacy(int stateId, out int blockId, out int meta)
    {
        blockId = stateId >> 4;
        meta = stateId & 0xF;
        return true;
    }

    public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | (meta & 0xF);
}
