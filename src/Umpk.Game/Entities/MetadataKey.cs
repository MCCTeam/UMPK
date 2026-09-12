namespace Umpk.Game.Entities;

/// <summary>The non-generic base of a tier-2 semantic metadata key. Carries the stable diagnostic name and is the identity an <see cref="IMetadataKeySource"/> maps to a tier-1 index. Reference identity is the key identity, so the canonical keys in <see cref="EntityMetadataKeys"/> are shared singletons.</summary>
public abstract class MetadataKey
{
    private protected MetadataKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>The stable diagnostic name of this key (for example <c>"health"</c>).</summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>A tier-2 semantic metadata key: a stable, typed name for a logical entity field (health, pose, custom name, shared flags, and so on) that resolves, per entity type and version, to a tier-1 metadata index and a projection from the raw <see cref="MetadataValue"/> to <typeparamref name="T"/>. Keys carry no per-session state; they are shared constants (see <see cref="EntityMetadataKeys"/>). Resolution runs through an <see cref="IMetadataKeySource"/> supplied by the Java data/codec layer's dataset.</summary>
/// <typeparam name="T">The projected value type this key reads.</typeparam>
public sealed class MetadataKey<T> : MetadataKey
{
    /// <summary>Creates a key with a diagnostic name and a projection from the raw value.</summary>
    /// <param name="name">A stable diagnostic name (for example <c>"health"</c>).</param>
    /// <param name="project">Projects the tier-1 value at the resolved index to <typeparamref name="T"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="project"/> is null.</exception>
    public MetadataKey(string name, Func<MetadataValue, T> project)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(project);
        Project = project;
    }

    /// <summary>Projects a tier-1 metadata value to this key's typed value.</summary>
    public Func<MetadataValue, T> Project { get; }
}
