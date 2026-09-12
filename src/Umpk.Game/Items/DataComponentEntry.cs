namespace Umpk.Game.Items;

/// <summary>One entry of a component patch. A patch entry either adds or overrides a component (<see cref="IsRemoval"/> false, <see cref="Value"/> non-null) or marks the item-default component as removed (<see cref="IsRemoval"/> true, <see cref="Value"/> null). The 1.20.5+ wire carries exactly this patch, so the Java codecs enumerate a map's <see cref="DataComponentMap.Patch"/> to reproduce it.</summary>
public sealed record DataComponentEntry
{
    private DataComponentEntry(DataComponentType type, object? value, bool isRemoval)
    {
        Type = type;
        Value = value;
        IsRemoval = isRemoval;
    }

    /// <summary>The component key this entry targets.</summary>
    public DataComponentType Type { get; }

    /// <summary>The added/overriding value, or null when this is a removal marker.</summary>
    public object? Value { get; }

    /// <summary>True when this entry removes the item-default component rather than setting one.</summary>
    public bool IsRemoval { get; }

    /// <summary>Creates an add/override entry.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="value"/> is null.</exception>
    public static DataComponentEntry Set(DataComponentType type, object value)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(value);
        return new DataComponentEntry(type, value, isRemoval: false);
    }

    /// <summary>Creates a removal marker.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static DataComponentEntry Remove(DataComponentType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new DataComponentEntry(type, value: null, isRemoval: true);
    }
}
