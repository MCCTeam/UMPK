using Umpk.Nbt;

namespace Umpk.Text;

/// <summary>The tooltip a <see cref="HoverEvent"/> shows. A closed union of the three wire actions: <see cref="HoverShowText"/>, <see cref="HoverShowItem"/>, and <see cref="HoverShowEntity"/>. The pre-1.21.5 wire form nests the payload under <c>contents</c> (with a legacy fallback under <c>value</c>); the 1.21.5+ form inlines the payload fields next to the <c>action</c> discriminator. This model is era-neutral; the serializers own the era differences.</summary>
public abstract record HoverEvent
{
    private protected HoverEvent()
    {
    }

    /// <summary>The vanilla serialized action name (<c>show_text</c>, <c>show_item</c>, <c>show_entity</c>).</summary>
    public abstract string ActionName { get; }
}

/// <summary>A <c>show_text</c> hover event whose tooltip is a full component.</summary>
public sealed record HoverShowText : HoverEvent
{
    /// <summary>Creates a show-text hover event.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public HoverShowText(Component text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The tooltip component.</summary>
    public Component Text { get; }

    /// <inheritdoc/>
    public override string ActionName => "show_text";
}

/// <summary>A <c>show_item</c> hover event. Item identity and stack data are carried era-neutrally: an item identifier, a count, and an optional NBT carrier holding the era-specific extra data (pre-1.21.5 <c>tag</c> compound, or 1.21.5+ component data). Full item-component modeling belongs to the game/protocol layers; <see cref="Umpk.Text"/> keeps the tooltip payload as opaque NBT.</summary>
public sealed record HoverShowItem : HoverEvent
{
    /// <summary>Creates a show-item hover event.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="itemId"/> is null.</exception>
    public HoverShowItem(string itemId, int count, NbtTag? data)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ItemId = itemId;
        Count = count;
        Data = data;
    }

    /// <summary>The item identifier (for example <c>minecraft:stone</c>).</summary>
    public string ItemId { get; }

    /// <summary>The stack count.</summary>
    public int Count { get; }

    /// <summary>Optional era-specific extra stack data as NBT (tag compound or component data); null if absent.</summary>
    public NbtTag? Data { get; }

    /// <inheritdoc/>
    public override string ActionName => "show_item";

    /// <inheritdoc/>
    public bool Equals(HoverShowItem? other) =>
        other is not null
        && string.Equals(ItemId, other.ItemId, StringComparison.Ordinal)
        && Count == other.Count
        && ((Data is null && other.Data is null) || (Data is not null && Data.Equals(other.Data)));

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.Ordinal.GetHashCode(ItemId), Count, Data);
}

/// <summary>A <c>show_entity</c> hover event. Pre-1.21.5 the wire fields are <c>type</c>/<c>id</c>/<c>name</c>; 1.21.5+ they are <c>id</c>/<c>uuid</c>/<c>name</c>. The model stores the entity type identifier, the entity UUID, and an optional custom name component.</summary>
public sealed record HoverShowEntity : HoverEvent
{
    /// <summary>Creates a show-entity hover event.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is null.</exception>
    public HoverShowEntity(string entityType, Guid id, Component? name)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        EntityType = entityType;
        Id = id;
        Name = name;
    }

    /// <summary>The entity type identifier (for example <c>minecraft:pig</c>).</summary>
    public string EntityType { get; }

    /// <summary>The entity UUID.</summary>
    public Guid Id { get; }

    /// <summary>The optional custom-name component; null if absent.</summary>
    public Component? Name { get; }

    /// <inheritdoc/>
    public override string ActionName => "show_entity";
}
