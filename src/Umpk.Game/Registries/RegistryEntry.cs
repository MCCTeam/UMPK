namespace Umpk.Game.Registries;

/// <summary>A cheap, comparable handle to a single registry entry. It carries the numeric network id, the <see cref="Umpk"/> <see cref="Identifier"/> key, and the definition value. Handles from the same registry compare by network id and key, so they can key dictionaries and be compared without touching <see cref="Value"/>.</summary>
/// <typeparam name="T">The definition type held by the owning registry.</typeparam>
public readonly struct RegistryEntry<T> : IEquatable<RegistryEntry<T>>
    where T : class
{
    private readonly T? _value;

    internal RegistryEntry(int networkId, Identifier id, T value)
    {
        _value = value;
        NetworkId = networkId;
        Id = id;
    }

    /// <summary>The numeric id this entry has on the wire for its version.</summary>
    public int NetworkId { get; }

    /// <summary>The namespaced key of this entry.</summary>
    public Identifier Id { get; }

    /// <summary>The definition value. Never null for an entry obtained from a registry.</summary>
    public T Value => _value ?? throw new InvalidOperationException("This registry entry is the default (unbound) value and has no definition.");

    /// <summary>True when this handle is the default (unbound) value rather than a real entry.</summary>
    public bool IsDefault => _value is null;

    /// <inheritdoc/>
    public bool Equals(RegistryEntry<T> other) =>
        NetworkId == other.NetworkId && Id.Equals(other.Id) && ReferenceEquals(_value, other._value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RegistryEntry<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(NetworkId, Id);

    /// <inheritdoc/>
    public override string ToString() => IsDefault ? "<unbound>" : $"{Id}#{NetworkId}";

    /// <summary>Value equality over network id, key and referenced definition.</summary>
    public static bool operator ==(RegistryEntry<T> left, RegistryEntry<T> right) => left.Equals(right);

    /// <summary>Inequality; see <see cref="operator=="/>.</summary>
    public static bool operator !=(RegistryEntry<T> left, RegistryEntry<T> right) => !left.Equals(right);
}
