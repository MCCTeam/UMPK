using Umpk.Game.Inventory;
using Umpk.Game.Registries;

namespace Umpk.Client.Internal;

/// <summary>Resolves the <c>minecraft:menu</c> registry entry an <c>open_screen</c> names, so a consumer can ask what KIND of container is open instead of guessing it from the slot count.</summary>
/// <remarks>
/// The two eras name the menu differently and neither key works on the other:
/// <list type="bullet">
/// <item>From 1.14 the packet carries the numeric <c>minecraft:menu</c> id, resolved directly.</item>
/// <item>
/// Before 1.14 there is no menu registry at all and the packet carries a STRING window type ("minecraft:chest", "minecraft:furnace", ... and the odd one out, "EntityHorse"). Those entries are in the registry too, keyed by identifier with a synthetic numeric id, so the string is what must be matched. Resolving a legacy window against a numeric id would be meaningless, and the synthetic ids are deliberately NOT wire values.
/// </item>
/// </list>
/// An unknown menu resolves to null rather than to a placeholder entry: naming a container wrongly is worse than admitting the kind is unknown, and the caller still has the raw id and title to fall back on.
/// </remarks>
internal static class MenuTypeResolver
{
    /// <summary>Resolves the menu type for an open-screen, or null when it cannot be named.</summary>
    /// <param name="registries">The session registries, or null before they are installed.</param>
    /// <param name="menuTypeId">The numeric menu id (1.14+), or negative on the legacy wire.</param>
    /// <param name="legacyWindowType">The pre-1.14 window-type string, or null on 1.14+.</param>
    internal static RegistryEntry<MenuTypeDefinition>? Resolve(
        RegistryAccess? registries,
        int menuTypeId,
        string? legacyWindowType)
    {
        if (registries is null)
            return null;

        Registry<MenuTypeDefinition> menus = registries.MenuTypes;

        if (legacyWindowType is not null)
        {
            // Almost every legacy window type already IS a well-formed identifier and hits the key index directly. "EntityHorse" is not, so fall back to matching the wire spelling the definition carries. The legacy table has at most fourteen entries, so the scan is cheap and happens once per container open.
            if (Identifier.TryParse(legacyWindowType, out Identifier key)
                && menus.TryGet(key, out RegistryEntry<MenuTypeDefinition> byKey))
                return byKey;

            foreach (RegistryEntry<MenuTypeDefinition> entry in menus)
                if (string.Equals(entry.Value.LegacyWindowType, legacyWindowType, StringComparison.Ordinal))
                    return entry;

            return null;
        }

        return menuTypeId >= 0 && menus.TryGet(menuTypeId, out RegistryEntry<MenuTypeDefinition> byId)
            ? byId
            : null;
    }
}
