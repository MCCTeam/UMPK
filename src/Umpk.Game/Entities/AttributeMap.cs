using Umpk.Game.Registries;

namespace Umpk.Game.Entities;

/// <summary>An entity's attribute set: a collection of <see cref="AttributeInstance"/>s keyed by attribute identity. Instances are created lazily on first touch so the map only holds attributes the server actually sends.</summary>
public sealed class AttributeMap
{
    private readonly Dictionary<Identifier, AttributeInstance> _instances = [];

    /// <summary>The attribute instances currently present.</summary>
    public IReadOnlyCollection<AttributeInstance> Instances => _instances.Values;

    /// <summary>The number of attributes present.</summary>
    public int Count => _instances.Count;

    /// <summary>True when an instance exists for the given attribute id.</summary>
    public bool Contains(Identifier attributeId) => _instances.ContainsKey(attributeId);

    /// <summary>Gets the instance for the given attribute id, if present.</summary>
    public bool TryGet(Identifier attributeId, out AttributeInstance instance) =>
        _instances.TryGetValue(attributeId, out instance!);

    /// <summary>Gets the existing instance for <paramref name="attribute"/> or creates one (seeded from the attribute definition's default base value). This is the entry point the client/navigator layer's attribute-update handlers call before setting the base value or modifiers.</summary>
    /// <exception cref="ArgumentException"><paramref name="attribute"/> is the unbound default handle.</exception>
    public AttributeInstance GetOrCreate(RegistryEntry<AttributeDefinition> attribute)
    {
        if (attribute.IsDefault)
            throw new ArgumentException("Cannot create an attribute instance for the unbound default handle.", nameof(attribute));

        return GetOrCreate(attribute.Id, attribute);
    }

    /// <summary>Gets or creates the instance for <paramref name="attribute"/> under an explicit map key rather than under the registry entry's own id. The definition still supplies the default/min/max.</summary>
    /// <remarks>This exists for one reason: vanilla renamed every attribute at 1.21.2 by dropping a category segment (<c>generic.movement_speed</c> to <c>movement_speed</c>), so a session on protocol 766 or 767 resolves a registry entry whose id is the PREFIXED name while every consumer asks for the canonical one. The caller passes <see cref="AttributeIds.Canonical"/> of the resolved id here, which keys the map identically on every protocol. The registry itself keeps the raw names, because <c>horse.jump_strength</c> and <c>generic.jump_strength</c> canonicalise together with different defaults and different ranges; only a per-session map, which holds one era at a time, can safely collapse them.</remarks>
    /// <param name="key">The map key to file the instance under.</param>
    /// <param name="attribute">The resolved registry handle supplying default/min/max.</param>
    /// <exception cref="ArgumentException"><paramref name="attribute"/> is the unbound default handle.</exception>
    public AttributeInstance GetOrCreate(Identifier key, RegistryEntry<AttributeDefinition> attribute)
    {
        if (attribute.IsDefault)
            throw new ArgumentException("Cannot create an attribute instance for the unbound default handle.", nameof(attribute));

        if (_instances.TryGetValue(key, out AttributeInstance? existing))
            return existing;

        var instance = new AttributeInstance(attribute);
        _instances[key] = instance;
        return instance;
    }

    /// <summary>Removes the instance for the given attribute id; returns true when one was removed.</summary>
    public bool Remove(Identifier attributeId) => _instances.Remove(attributeId);

    /// <summary>Removes every attribute instance.</summary>
    public void Clear() => _instances.Clear();

    /// <summary>The resolved value of the given attribute, or <paramref name="fallback"/> when the map holds no instance for it.</summary>
    public double GetValueOrDefault(Identifier attributeId, double fallback) =>
        _instances.TryGetValue(attributeId, out AttributeInstance? instance) ? instance.Value : fallback;
}
