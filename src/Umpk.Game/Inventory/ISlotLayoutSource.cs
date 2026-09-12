using Umpk.Game.Registries;

namespace Umpk.Game.Inventory;

/// <summary>The seam that produces a <see cref="SlotLayout"/> for a menu type. It is keyed by the menu-type registry handle so the same source serves every version's menu ids.</summary>
public interface ISlotLayoutSource
{
    /// <summary>Resolves the slot layout for a menu type. Returns false when the menu type is unknown.</summary>
    bool TryGetLayout(RegistryEntry<MenuTypeDefinition> menuType, out SlotLayout layout);
}
