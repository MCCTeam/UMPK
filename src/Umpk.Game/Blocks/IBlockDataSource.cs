using Umpk.Game.Registries;

namespace Umpk.Game.Blocks;

/// <summary>
/// The per-version block table seam. Implemented in <c>Umpk.Data.Java</c> over generated binary tables; every member here is designed to resolve through array-index math on contiguous blobs rather than per-entry object lookup. A <see cref="BlockState"/> is a thin handle of (state id + a reference to one of these), so all of a state's facts are answered here.
///
/// <para>Pre-flattening versions (protocols 47-340): the state id is <c>(blockId &lt;&lt; 4) | meta</c>, there are no property schemas, and property access degrades gracefully (no properties reported). Modern versions carry real property schemas and per-state property values derived by state-offset math. <see cref="IsLegacy"/> tells the two apart, and <see cref="TryDecodeLegacy"/> / <see cref="EncodeLegacy"/> expose the legacy identity round-trip.</para>
/// </summary>
public interface IBlockDataSource
{
    /// <summary>The block registry for this version; block network ids index it.</summary>
    Registry<BlockDefinition> Blocks { get; }

    /// <summary>The state id used for out-of-range or unknown states (the designated air/unknown state).</summary>
    int UnknownStateId { get; }

    /// <summary>True when this is a pre-flattening data source keyed by <c>(id &lt;&lt; 4) | meta</c>.</summary>
    bool IsLegacy { get; }

    /// <summary>The total number of valid block-state ids (states are <c>0 .. StateCount - 1</c> on modern data).</summary>
    int StateCount { get; }

    /// <summary>True when the given state id resolves to a known state.</summary>
    bool IsValidState(int stateId);

    /// <summary>The block network id that owns the given state id, or the unknown block for out-of-range ids.</summary>
    int GetBlockNetworkId(int stateId);

    /// <summary>The default state id of the block with the given network id.</summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">No such block.</exception>
    int GetDefaultStateId(int blockNetworkId);

    /// <summary>The flag bitset for the given state id.</summary>
    BlockFlags GetFlags(int stateId);

    /// <summary>The friction (slipperiness) scalar for the given state id.</summary>
    float GetFriction(int stateId);

    /// <summary>The horizontal speed factor for the given state id (soul sand, honey).</summary>
    float GetSpeedFactor(int stateId);

    /// <summary>The jump factor for the given state id (honey).</summary>
    float GetJumpFactor(int stateId);

    /// <summary>The property names defined on the block that owns this state, in vanilla declaration order. Empty for legacy data and for propertyless blocks.</summary>
    IReadOnlyList<string> GetPropertyNames(int stateId);

    /// <summary>Resolves the value of a named property for the given state id (for example <c>"facing" -&gt; "north"</c>). Returns false for legacy data, unknown properties, and unknown states.</summary>
    bool TryGetPropertyValue(int stateId, string propertyName, out string value);

    /// <summary>Legacy identity decode: splits a legacy state id into its block id and metadata. Returns false on non-legacy data.</summary>
    bool TryDecodeLegacy(int stateId, out int blockId, out int meta);

    /// <summary>Legacy identity encode: composes <c>(blockId &lt;&lt; 4) | meta</c>. The result is only a meaningful state id on legacy data; callers guard on <see cref="IsLegacy"/>.</summary>
    int EncodeLegacy(int blockId, int meta);
}
