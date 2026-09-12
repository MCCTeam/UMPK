namespace Umpk.Game.Registries;

/// <summary>A block's reaction to piston movement, in wire declaration order.</summary>
public enum PistonPushReaction
{
    /// <summary>The block is carried by the piston.</summary>
    Normal = 0,

    /// <summary>The block is broken when an extending piston reaches it.</summary>
    Destroy = 1,

    /// <summary>The block stops the piston: nothing moves at all.</summary>
    Block = 2,

    /// <summary>Reserved and unused by blocks in every supported version. It follows the same block-entity handling as <see cref="Normal"/>.</summary>
    Ignore = 3,

    /// <summary>The block is pushable only along its own facing (the glazed terracottas).</summary>
    PushOnly = 4,
}

/// <summary>The three per-block facts used to determine whether a piston can move one block.</summary>
/// <param name="Reaction">The block's configured piston reaction.</param>
/// <param name="Unbreakable">Whether the block has destroy speed -1.0.</param>
/// <param name="HasBlockEntity">Whether the block has a block entity, which prevents ordinary piston movement.</param>
public readonly record struct BlockPushInfo(PistonPushReaction Reaction, bool Unbreakable, bool HasBlockEntity);

/// <summary>The per-version piston-pushability table. No packet carries these facts, so version data supplies them.</summary>
/// <remarks>
/// <para>A piston push sends no packet for the displacement. A compatible client must know the push reaction, whether the block is unbreakable, and whether it has a block entity. None of those facts is derivable from other UMPK data.</para>
/// <para>Data can be absent for versions that could not be measured. In that case, <see cref="HasData"/> is false and the caller must not model pushed blocks: the omission under-pushes, which is what the client already did, whereas a guessed reaction would push the wrong blocks and look right doing it.</para>
/// </remarks>
public interface IBlockPushSource
{
    /// <summary>True when this version was measured. False means "unknown", never "everything is normal".</summary>
    bool HasData { get; }

    /// <summary>The piston facts for a block identifier. Returns false when <see cref="HasData"/> is false and when the identifier is not a block this version was measured for, which a caller must treat as a refusal rather than as a default.</summary>
    bool TryGet(Identifier block, out BlockPushInfo info);
}
