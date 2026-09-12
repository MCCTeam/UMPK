using Umpk.Game.Registries;

namespace Umpk.Game.Blocks;

/// <summary>
/// A lightweight handle to one block state: a state id plus a reference to the per-version <see cref="IBlockDataSource"/> that answers questions about it. Reads (flags, properties, owning block) are array-index lookups on the source. Two block states are equal when they share a source and a state id.
///
/// <para>Pre-flattening identity is honored: on legacy sources the <see cref="StateId"/> is <c>(blockId &lt;&lt; 4) | meta</c>, <see cref="TryGetProperty"/> returns false, and <see cref="LegacyId"/>/<see cref="LegacyMeta"/> expose the split.</para>
/// </summary>
public readonly struct BlockState : IEquatable<BlockState>
{
    /// <summary>The registry path for <c>minecraft:scaffolding</c>.</summary>
    private const string ScaffoldingPath = "scaffolding";

    /// <summary>The registry path for <c>minecraft:powder_snow</c>.</summary>
    private const string PowderSnowPath = "powder_snow";

    private readonly IBlockDataSource? _source;

    /// <summary>Wraps a state id against a data source.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public BlockState(IBlockDataSource source, int stateId)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        StateId = stateId;
    }

    /// <summary>The raw block-state id (wire identity for its version).</summary>
    public int StateId { get; }

    /// <summary>The data source backing this state.</summary>
    /// <exception cref="InvalidOperationException">This is a default-constructed value.</exception>
    public IBlockDataSource Source => _source ?? throw new InvalidOperationException("This BlockState is the default value and has no data source.");

    /// <summary>True when this is a default-constructed value with no backing source.</summary>
    public bool IsDefault => _source is null;

    /// <summary>The block registry entry that owns this state.</summary>
    public RegistryEntry<BlockDefinition> Block
    {
        get
        {
            var source = Source;
            int blockId = source.GetBlockNetworkId(StateId);
            return source.Blocks[blockId];
        }
    }

    /// <summary>The flag bitset for this state.</summary>
    public BlockFlags Flags => Source.GetFlags(StateId);

    /// <summary>True when this state is a valid, known state on its source.</summary>
    public bool IsValid => _source is not null && _source.IsValidState(StateId);

    // Flag tests are bitwise, not Enum.HasFlag: HasFlag boxes both operands in unoptimized codegen (the JIT intrinsic that elides the boxing runs only in optimized code), and these properties sit on the physics engine's per-tick path where the 0 B/tick guarantee must hold in every JIT tier.

    /// <summary><c>minecraft:air</c> and cave/void air.</summary>
    public bool IsAir => (Flags & BlockFlags.Air) != 0;

    /// <summary>Water or lava, source or flowing.</summary>
    public bool IsFluid => (Flags & BlockFlags.Fluid) != 0;

    /// <summary>The <c>waterlogged</c> property is set.</summary>
    public bool IsWaterlogged => (Flags & BlockFlags.Waterlogged) != 0;

    /// <summary>The state blocks entity motion.</summary>
    public bool BlocksMotion => (Flags & BlockFlags.BlocksMotion) != 0;

    /// <summary>The state is a full solid block.</summary>
    public bool IsSolid => (Flags & BlockFlags.Solid) != 0;

    /// <summary>The state is climbable (ladder/vine/scaffolding).</summary>
    public bool IsClimbable => (Flags & BlockFlags.Climbable) != 0;

    /// <summary>True for <c>minecraft:scaffolding</c>, whose collision shape and climb clamp depend on the body.</summary>
    /// <remarks>
    /// <para>A descending body meets an empty shape, while other bodies meet the top plate. Sneaking also does not pin a body inside scaffolding as it does on a ladder. Both rules are required for controlled descent through a scaffold tower.</para>
    /// <para>The rule is keyed by block identity. It cannot be derived from "climbable with a collision shape": a ladder is that too, and its 3/16 wall panel is real from every direction.</para>
    /// <para>The climbable bit is tested first, so the string compare is unreachable for the ~28,000 states that are not climbable at all. The <see cref="IsDefault"/> term is not decoration: <see cref="Flags"/> throws on a default-constructed state, and every caller here reads this off a world lookup that can legitimately return the default for an unloaded cell.</para>
    /// </remarks>
    public bool IsScaffolding => !IsDefault && IsClimbable && Block.Id.Path == ScaffoldingPath;

    /// <summary>True for <c>minecraft:powder_snow</c>, whose collision shape depends on the body's equipment.</summary>
    /// <remarks>
    /// <para>The collision shape is empty unless a player wears leather boots in the feet slot. The block-shape table has no entity context, so it records an empty shape. The cube a booted body meets is synthesised at the collision call site by <c>Umpk.Physics.PowderSnowCollision</c>. This property is how both that helper and the planner's boots-conditional arms name the block.</para>
    /// <para>Nothing about powder snow's FLAGS distinguishes it: it carries none at all, which it shares with every flower, torch and sapling in the game. A per-block registry-path comparison is therefore required.</para>
    /// <para>Powder snow arrived in 1.17, so on every earlier protocol no block carries this path and this answers false for every state in the world - no protocol number appears here or in any consumer. <c>BlockAttributeTests</c> pins both halves.</para>
    /// <para>Air and fluid states are tested first because they are the overwhelming majority of the cells the collision scan walks, and both are a bitwise flag read, so the string compare is unreachable for them. The <see cref="IsDefault"/> term is not decoration: <see cref="Flags"/> throws on a default-constructed state, and every caller reads this off a world lookup that can legitimately return the default for an unloaded cell.</para>
    /// </remarks>
    public bool IsPowderSnow => !IsDefault && !IsAir && !IsFluid && Block.Id.Path == PowderSnowPath;

    /// <summary>The state can be replaced by block placement.</summary>
    public bool IsReplaceable => (Flags & BlockFlags.Replaceable) != 0;

    /// <summary>Slipperiness scalar (physics input).</summary>
    public float Friction => Source.GetFriction(StateId);

    /// <summary>Horizontal speed factor (soul sand, honey).</summary>
    public float SpeedFactor => Source.GetSpeedFactor(StateId);

    /// <summary>Jump factor (honey).</summary>
    public float JumpFactor => Source.GetJumpFactor(StateId);

    /// <summary>The property names on the owning block, in declaration order. Empty for legacy states.</summary>
    public IReadOnlyList<string> PropertyNames => Source.GetPropertyNames(StateId);

    /// <summary>The legacy block id when the source is pre-flattening (<c>StateId &gt;&gt; 4</c>), else -1.</summary>
    public int LegacyId => Source.TryDecodeLegacy(StateId, out int blockId, out _) ? blockId : -1;

    /// <summary>The legacy metadata when the source is pre-flattening (<c>StateId &amp; 0xF</c>), else -1.</summary>
    public int LegacyMeta => Source.TryDecodeLegacy(StateId, out _, out int meta) ? meta : -1;

    /// <summary>Resolves a named property value (for example <c>"facing" -&gt; "north"</c>). Returns false for legacy states, unknown properties, and unknown states.</summary>
    public bool TryGetProperty(string name, out string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_source is null)
        {
            value = string.Empty;
            return false;
        }

        return _source.TryGetPropertyValue(StateId, name, out value);
    }

    /// <inheritdoc/>
    public bool Equals(BlockState other) => ReferenceEquals(_source, other._source) && StateId == other.StateId;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BlockState other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_source, StateId);

    /// <inheritdoc/>
    public override string ToString() => _source is null ? "<default>" : $"{Block.Id}#{StateId}";

    /// <summary>Value equality over source reference and state id.</summary>
    public static bool operator ==(BlockState left, BlockState right) => left.Equals(right);

    /// <summary>Inequality; see <see cref="operator=="/>.</summary>
    public static bool operator !=(BlockState left, BlockState right) => !left.Equals(right);
}
