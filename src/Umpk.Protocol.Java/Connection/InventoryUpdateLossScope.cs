namespace Umpk.Protocol.Java;

/// <summary>The inventory header whose compact item payload could not be decoded.</summary>
public enum InventoryUpdateLossKind
{
    /// <summary>A container slot addressed by container id and menu slot.</summary>
    ContainerSlot,

    /// <summary>A complete container snapshot, including its carried item.</summary>
    ContainerContents,

    /// <summary>A cursor item with no container or slot header.</summary>
    Cursor,

    /// <summary>A player slot addressed in player-inventory index space.</summary>
    PlayerInventorySlot,
}

/// <summary>Already-read inventory header fields for an undecodable compact item update. A null field was absent from the wire header. This metadata contains no partially decoded stacks and does not apply a state revision.</summary>
public sealed record InventoryUpdateLossScope(
    InventoryUpdateLossKind Kind,
    int? ContainerId,
    int? StateId,
    int? Slot,
    int? ItemCount);
