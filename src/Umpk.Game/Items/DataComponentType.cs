namespace Umpk.Game.Items;

/// <summary>A typed key into a <see cref="DataComponentMap"/>. Each vanilla item data component (<c>minecraft:damage</c>, <c>minecraft:enchantments</c>, ...) has exactly one <see cref="DataComponentType{T}"/> instance whose type argument is the component's model record. The instances live on <see cref="DataComponents"/>; construction here is closed so the identity map from <see cref="Id"/> to key stays one-to-one.</summary>
/// <remarks>The non-generic <see cref="DataComponentType"/> base carries the wire identity so the map can hold a heterogeneous set of keys and so the Java codec layer can look a key up by its <see cref="Id"/> when decoding a component patch. The generic subtype adds the payload type.</remarks>
public abstract class DataComponentType
{
    private protected DataComponentType(Identifier id) => Id = id;

    /// <summary>The namespaced id vanilla assigns this component (e.g. <c>minecraft:custom_name</c>).</summary>
    public Identifier Id { get; }

    /// <summary>The CLR type of this component's payload record.</summary>
    public abstract Type PayloadType { get; }

    /// <inheritdoc/>
    public override string ToString() => Id.ToString();
}

/// <summary>The typed form of <see cref="DataComponentType"/>: the key that binds a component <see cref="Identifier"/> to its payload record type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The component payload record type.</typeparam>
public sealed class DataComponentType<T> : DataComponentType
    where T : class
{
    internal DataComponentType(Identifier id)
        : base(id)
    {
    }

    /// <inheritdoc/>
    public override Type PayloadType => typeof(T);
}
