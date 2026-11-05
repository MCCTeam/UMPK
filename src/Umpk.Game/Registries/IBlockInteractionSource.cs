using Umpk.Geometry;

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

/// <summary>Measured replacement rules for ordinary cube items, before runtime placement conditions.</summary>
/// <param name="ReplacesClickedBlock">The native placement context uses the clicked cell rather than the adjacent cell.</param>
/// <param name="AllowsPlacement">The native resolved destination accepts an ordinary cube item.</param>
public readonly record struct BlockReplacementInfo(bool ReplacesClickedBlock, bool AllowsPlacement);

/// <summary>Measured support and material predicates for the cells used by a bed's exit search. These rules are independent of collision-box overlap.</summary>
public readonly record struct BedExitCellInfo(bool SupportsPlayer, bool AllowsBody);

/// <summary>Artifact-backed block and default-item use contracts. Custom stack components and specialized world predicates require additional evaluation.</summary>
public interface IBlockInteractionSource
{
    /// <summary>Whether this version has a native measurement.</summary>
    bool HasData { get; }
    /// <summary>Returns a measured block contract, or false for an unknown block or missing measurement.</summary>
    bool TryGetBlock(Identifier block, out BlockInteractionInfo info);
    /// <summary>Returns the measured default-item contract. A known item can have an explicit unknown specialized rule.</summary>
    bool TryGetItem(Identifier item, out ItemUseOnInfo info);
    /// <summary>Returns the measured horizontal facing of a bed state, or false when its state or version is unmeasured.</summary>
    bool TryGetBedFacing(int stateId, out Direction facing)
    {
        facing = default;
        return false;
    }
    /// <summary>Returns original bed-exit cell predicates, or false for missing measurements and natively rejected metadata.</summary>
    bool TryGetBedExitCell(int stateId, out BedExitCellInfo info)
    {
        info = default;
        return false;
    }
    /// <summary>Returns native ordinary-cube replacement rules. A lower state is required only for measured neighbor-dependent states; null means that cell is unknown.</summary>
    bool TryGetCubeReplacement(int stateId, int? belowStateId, out BlockReplacementInfo info)
    {
        info = default;
        return false;
    }
    /// <summary>Whether the measured vanilla entity type never obstructs placement. False includes missing measurements and state-dependent obstruction.</summary>
    bool IsAlwaysNonObstructingEntity(Identifier entityType) => false;
    /// <summary>Returns measured centered entity dimensions that are independent of pose and synchronized metadata. Position, movement and world effects remain runtime state.</summary>
    bool TryGetFixedEntityDimensions(Identifier entityType, out float width, out float height)
    {
        width = height = 0;
        return false;
    }
}
