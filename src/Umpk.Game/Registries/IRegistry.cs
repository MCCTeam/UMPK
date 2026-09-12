namespace Umpk.Game.Registries;

/// <summary>The non-generic face of a registry, used where the entry type is not known statically (for example <see cref="RegistryAccess.TryGetRegistry"/>). It exposes id/key resolution without the definition value; callers that need the value downcast to <see cref="Registry{T}"/>.</summary>
public interface IRegistry
{
    /// <summary>The identifier of the registry itself (for example <c>minecraft:block</c>).</summary>
    Identifier RegistryId { get; }

    /// <summary>The number of entries in the registry.</summary>
    int Count { get; }

    /// <summary>True when an entry with the given network id exists.</summary>
    bool ContainsNetworkId(int networkId);

    /// <summary>True when an entry with the given key exists.</summary>
    bool ContainsKey(Identifier id);

    /// <summary>Resolves the key of the entry with the given network id.</summary>
    /// <returns>False when no such entry exists.</returns>
    bool TryGetKey(int networkId, out Identifier id);

    /// <summary>Resolves the network id of the entry with the given key.</summary>
    /// <returns>False when no such entry exists.</returns>
    bool TryGetNetworkId(Identifier id, out int networkId);
}
