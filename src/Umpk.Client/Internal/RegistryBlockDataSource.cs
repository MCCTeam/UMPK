using Umpk.Game.Blocks;
using Umpk.Game.Registries;

namespace Umpk.Client.Internal;

/// <summary>The client's <see cref="IBlockDataSource"/>, answering every per-state question from the block registry the session installed. When that registry came from the version's generated data (the normal case: <c>Umpk.Data.Java.JavaGameData.Registries</c>), each <see cref="BlockDefinition"/> carries the block's real flags, physics scalars, and state-property domains, so properties, friction and the air/fluid/solid distinction are answered from data rather than assumed.</summary>
/// <remarks>
/// <para>Where a version's data genuinely lacks a fact, the answer is a refusal rather than a plausible constant. Concretely: the pre-flattening bands (protocols 47-340) carry no property names at all, so <see cref="GetPropertyNames"/> is empty and <see cref="TryGetPropertyValue"/> is false there; from 1.13 up the names are real, and a value is returned only for blocks whose value domains the generator could prove. <see cref="TryDecodeLegacy"/> is the mirror image: real on the pre-flattening bands, where the state id IS <c>(id &lt;&lt; 4) | meta</c>, and false on flattened ones where no such identity exists.</para>
/// <para>The state-to-block index is built once so <see cref="GetBlockNetworkId"/> does not scan the block registry on the physics and pathfinding read path.</para>
/// </remarks>
internal sealed class RegistryBlockDataSource : IBlockDataSource
{
    private static readonly IReadOnlyList<string> NoProperties = [];

    private readonly int _maxState;
    private readonly BlockDefinition?[] _byState;
    private readonly int[] _blockIdByState;

    public RegistryBlockDataSource(Registry<BlockDefinition> blocks, bool isLegacy)
    {
        Blocks = blocks;
        IsLegacy = isLegacy;

        int max = 0;
        foreach (RegistryEntry<BlockDefinition> entry in blocks)
            if (entry.Value.MaxStateId > max)
                max = entry.Value.MaxStateId;

        _maxState = max;
        _byState = new BlockDefinition?[max + 1];
        _blockIdByState = new int[max + 1];
        foreach (RegistryEntry<BlockDefinition> entry in blocks)
        {
            BlockDefinition definition = entry.Value;
            for (int state = definition.MinStateId; state <= definition.MaxStateId; state++)
            {
                if (state < 0 || state > max || _byState[state] is not null)
                    continue;

                _byState[state] = definition;
                _blockIdByState[state] = entry.NetworkId;
            }
        }
    }

    public Registry<BlockDefinition> Blocks { get; }

    public int UnknownStateId => 0;

    public bool IsLegacy { get; }

    public int StateCount => _maxState + 1;

    public bool IsValidState(int stateId) => stateId >= 0 && stateId <= _maxState && _byState[stateId] is not null;

    public int GetBlockNetworkId(int stateId)
        => stateId >= 0 && stateId <= _maxState ? _blockIdByState[stateId] : 0;

    public int GetDefaultStateId(int blockNetworkId)
        => Blocks.TryGet(blockNetworkId, out RegistryEntry<BlockDefinition> entry) ? entry.Value.DefaultStateId : 0;

    public BlockFlags GetFlags(int stateId)
        => Definition(stateId) is { } definition ? definition.FlagsForState(stateId) : BlockFlags.None;

    public float GetFriction(int stateId) => Definition(stateId)?.Friction ?? 0.6f;

    public float GetSpeedFactor(int stateId) => Definition(stateId)?.SpeedFactor ?? 1.0f;

    public float GetJumpFactor(int stateId) => Definition(stateId)?.JumpFactor ?? 1.0f;

    public IReadOnlyList<string> GetPropertyNames(int stateId)
    {
        if (Definition(stateId) is not { Properties.Count: > 0 } definition)
            return NoProperties;

        var names = new string[definition.Properties.Count];
        for (int i = 0; i < names.Length; i++)
            names[i] = definition.Properties[i].Name;

        return names;
    }

    public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
    {
        if (Definition(stateId) is { } definition)
            return definition.TryGetPropertyValue(stateId, propertyName, out value);

        value = string.Empty;
        return false;
    }

    public bool TryDecodeLegacy(int stateId, out int blockId, out int meta)
    {
        if (!IsLegacy || stateId < 0)
        {
            blockId = 0;
            meta = 0;
            return false;
        }

        blockId = stateId >> 4;
        meta = stateId & 0xF;
        return true;
    }

    public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | (meta & 0xF);

    private BlockDefinition? Definition(int stateId)
        => stateId >= 0 && stateId <= _maxState ? _byState[stateId] : null;
}
