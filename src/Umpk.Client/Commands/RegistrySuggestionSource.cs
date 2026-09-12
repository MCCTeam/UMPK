using Umpk.Commands;
using Umpk.Game.Registries;

namespace Umpk.Client.Commands;

/// <summary>Resolves <see cref="Arguments.RegistryId"/> suggestions from a client session's live <see cref="RegistryAccess"/>, read fresh through <paramref name="access"/> on every call instead of pinned to whatever was live when this source was built. A session goes from no registries at all (before login) to a real snapshot, and a configuration re-entry can later swap the whole snapshot for a replacement (<see cref="RegistrySnapshot.With"/>), so a suggestion query has to re-read <see cref="ClientState.Registries"/> every time rather than capture it once.</summary>
/// <remarks>The non-generic <see cref="IRegistry"/> supports key lookup but not key enumeration, so this source uses <see cref="RegistryAccess"/>'s typed registries and can suggest only the registries listed here.</remarks>
public sealed class RegistrySuggestionSource(Func<RegistryAccess?> access) : IRegistrySuggestionSource
{
    /// <inheritdoc/>
    public IEnumerable<Identifier> GetEntries(Identifier registryId)
    {
        RegistryAccess? registries = access();
        if (registries is null)
            return [];

        if (registryId.Equals(RegistryIds.Item))
            return Keys(registries.Items);

        if (registryId.Equals(RegistryIds.Block))
            return Keys(registries.Blocks);

        if (registryId.Equals(RegistryIds.EntityType))
            return Keys(registries.EntityTypes);

        if (registryId.Equals(RegistryIds.DimensionType))
            return Keys(registries.DimensionTypes);

        if (registryId.Equals(RegistryIds.Biome))
            return Keys(registries.Biomes);

        if (registryId.Equals(RegistryIds.Enchantment))
            return Keys(registries.Enchantments);

        if (registryId.Equals(RegistryIds.MobEffect))
            return Keys(registries.MobEffects);

        if (registryId.Equals(RegistryIds.Attribute))
            return Keys(registries.Attributes);

        if (registryId.Equals(RegistryIds.Menu))
            return Keys(registries.MenuTypes);

        if (registryId.Equals(RegistryIds.ChatType))
            return registries.ChatTypes is { } chatTypes ? Keys(chatTypes) : [];

        if (registryId.Equals(RegistryIds.LegacyObjectType))
            return registries.LegacyObjectTypes is { } legacyObjectTypes ? Keys(legacyObjectTypes) : [];

        return [];
    }

    private static IEnumerable<Identifier> Keys<T>(Registry<T> registry)
        where T : class =>
        registry.Select(entry => entry.Id);
}
