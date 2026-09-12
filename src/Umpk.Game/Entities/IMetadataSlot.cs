namespace Umpk.Game.Entities;

/// <summary>The item-slot placeholder carried by <see cref="MetadataValue"/> of kind <see cref="MetadataValueKind.Slot"/>.</summary>
/// <remarks>The item library owns <c>ItemStack</c>; this module must not reference it. Rather than store an opaque <see cref="object"/>, the seam is this marker interface: the item library (or the composition layer wiring the join) implements it on its slot carrier, and consumers that hold a reference to the item assembly can pattern-match back to the concrete type. Tier-1 metadata storage therefore stays complete and typed without a compile-time dependency on items. A null carrier represents the empty slot.</remarks>
public interface IMetadataSlot
{
    /// <summary>True when this placeholder represents the empty (air) slot.</summary>
    bool IsEmpty { get; }
}
