namespace Umpk.Game.Registries;

/// <summary>An immutable bag of every registry known for one point in a session's life, keyed by registry id. A generated data source produces the default snapshot for a version (<c>JavaVersion.DefaultRegistries</c>); server-sent registry data during the configuration/login phase produces a replacement snapshot, and the session swaps the whole snapshot atomically rather than mutating one in place. Build one with <see cref="RegistrySnapshotBuilder"/>.</summary>
public sealed class RegistrySnapshot
{
    private readonly Dictionary<Identifier, IRegistry> _registries;

    internal RegistrySnapshot(Dictionary<Identifier, IRegistry> registries)
    {
        _registries = registries;
    }

    /// <summary>The number of registries in the snapshot.</summary>
    public int Count => _registries.Count;

    /// <summary>The registry ids present in this snapshot.</summary>
    public IReadOnlyCollection<Identifier> RegistryIds => _registries.Keys;

    /// <summary>Resolves a registry by id in its non-generic form.</summary>
    public bool TryGetRegistry(Identifier registryId, out IRegistry registry)
    {
        if (_registries.TryGetValue(registryId, out var found))
        {
            registry = found;
            return true;
        }

        registry = null!;
        return false;
    }

    /// <summary>Resolves a typed registry by id. The stored registry must have element type <typeparamref name="T"/>.</summary>
    /// <returns>False when the id is absent or the stored registry has a different element type.</returns>
    public bool TryGetRegistry<T>(Identifier registryId, out Registry<T> registry)
        where T : class
    {
        if (_registries.TryGetValue(registryId, out var found) && found is Registry<T> typed)
        {
            registry = typed;
            return true;
        }

        registry = null!;
        return false;
    }

    /// <summary>Returns a copy of this snapshot with the given registries replaced or added, without mutating this instance. Used to apply server registry overrides onto the version defaults.</summary>
    public RegistrySnapshot With(params IRegistry[] overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var next = new Dictionary<Identifier, IRegistry>(_registries);
        foreach (var registry in overrides)
        {
            ArgumentNullException.ThrowIfNull(registry);
            next[registry.RegistryId] = registry;
        }

        return new RegistrySnapshot(next);
    }
}

/// <summary>Accumulates registries into one immutable <see cref="RegistrySnapshot"/>.</summary>
public sealed class RegistrySnapshotBuilder
{
    private readonly Dictionary<Identifier, IRegistry> _registries = new();
    private bool _built;

    /// <summary>The number of registries added so far.</summary>
    public int Count => _registries.Count;

    /// <summary>Adds or replaces a registry, keyed by its own <see cref="IRegistry.RegistryId"/>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> has already run.</exception>
    public RegistrySnapshotBuilder Add(IRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_built)
            throw new InvalidOperationException("This snapshot builder has already produced a snapshot.");

        _registries[registry.RegistryId] = registry;
        return this;
    }

    /// <summary>Produces the immutable snapshot and seals the builder.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> has already run.</exception>
    public RegistrySnapshot Build()
    {
        if (_built)
            throw new InvalidOperationException("This snapshot builder has already produced a snapshot.");

        _built = true;
        return new RegistrySnapshot(new Dictionary<Identifier, IRegistry>(_registries));
    }
}
