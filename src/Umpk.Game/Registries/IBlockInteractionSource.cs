namespace Umpk.Game.Registries;

/// <summary>Measured block-use inheritance and replacement contracts. False does not imply the opposite behavior; it means the specialized rule still needs evaluation.</summary>
/// <param name="PassiveUse">The block inherits vanilla's base pass-through use methods.</param>
/// <param name="NeverReplaceable">The block inherits the base replacement rule and has a nonreplaceable material or property.</param>
public readonly record struct BlockInteractionInfo(bool PassiveUse, bool NeverReplaceable);

/// <summary>A measured default item's use-on contract, before player, stack and world preconditions.</summary>
public enum ItemUseOnKind
{
    /// <summary>A specialized use rule has not yet been represented.</summary>
    Unknown = 0,
    /// <summary>The default item inherits vanilla's pass-through use-on method.</summary>
    Pass = 1,
    /// <summary>The item inherits ordinary block placement, and its block inherits unconditional survival and full-cube geometry. Replacement and obstruction still need evaluation.</summary>
    SimpleCubePlacement = 2,
}

/// <summary>A measured default item use-on contract.</summary>
/// <param name="Kind">The native contract.</param>
/// <param name="PlacedBlock">The block associated with a block item, or null for other items.</param>
public readonly record struct ItemUseOnInfo(ItemUseOnKind Kind, Identifier? PlacedBlock);

/// <summary>Artifact-backed block and default-item use contracts. Custom stack components and specialized world predicates require additional evaluation.</summary>
public interface IBlockInteractionSource
{
    /// <summary>Whether this version has a native measurement.</summary>
    bool HasData { get; }
    /// <summary>Returns a measured block contract, or false for an unknown block or missing measurement.</summary>
    bool TryGetBlock(Identifier block, out BlockInteractionInfo info);
    /// <summary>Returns the measured default-item contract. A known item can have an explicit unknown specialized rule.</summary>
    bool TryGetItem(Identifier item, out ItemUseOnInfo info);
    /// <summary>Whether the measured vanilla entity type never obstructs placement. False includes missing measurements and state-dependent obstruction.</summary>
    bool IsAlwaysNonObstructingEntity(Identifier entityType) => false;
    /// <summary>Returns measured centered entity dimensions that are independent of pose and synchronized metadata. Position, movement and world effects remain runtime state.</summary>
    bool TryGetFixedEntityDimensions(Identifier entityType, out float width, out float height)
    {
        width = height = 0;
        return false;
    }
}
