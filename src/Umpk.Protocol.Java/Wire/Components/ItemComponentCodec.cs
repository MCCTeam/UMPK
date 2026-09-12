using Umpk.Game.Items;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>A version-specific serializer for one item data-component payload. A component codec reads/writes the component value on the modern component wire and, for the 1.21.5+ hashed-slot optimization, hashes the value through <see cref="HashOps"/> (structural hashing over the DATA form, not the wire bytes). Codecs are constructed once and closed over era layout values; there is no per-call state.</summary>
/// <remarks>The <see cref="Type"/> binds this codec to its well-known <see cref="DataComponentType"/> key so the decoder can build a typed <see cref="DataComponentEntry"/>. Components that are not fully typed on this era have no codec in the era table: the compact component wire is not self-delimiting, so an untyped component id raises a <see cref="ProtocolViolationException"/> with its identity rather than silently desyncing. The typed set covers the common gameplay components; the rest are deferred.</remarks>
internal abstract class ItemComponentCodec
{
    protected ItemComponentCodec(DataComponentType type) => Type = type;

    /// <summary>The well-known component key this codec serializes.</summary>
    public DataComponentType Type { get; }

    /// <summary>Decodes the component value from the reader.</summary>
    public abstract object Decode(ref PacketReader reader, PacketCodecContext context);

    /// <summary>Encodes the component value into the writer.</summary>
    public abstract void Encode(ref PacketWriter writer, object value, PacketCodecContext context);

    /// <summary>Hashes the component value through Mojang's structural HashOps model.</summary>
    public abstract int Hash(in HashOps ops, object value, PacketCodecContext context);
}

/// <summary>The wire form a nested item stack takes inside a bundle/container/charged-projectiles payload. This is a separate era axis from the component id ORDERING: 26.1 changed the nested stack itself while leaving the surrounding list framing alone.</summary>
internal enum NestedStackForm
{
    /// <summary>1.20.5-1.21.11: a VarInt count first, where zero means the empty stack, then the item holder id and the patch.</summary>
    CountFirst,

    /// <summary>26.1+: the item holder id first, then a VarInt count, and no empty sentinel; an optional slot is wrapped in a leading present bool instead.</summary>
    Template,
}

/// <summary>A component codec whose payload contains nested item stacks (bundle, container, charged projectiles). Those nested stacks carry their own component patches, which must decode under the same component era table. The table is bound once when the owning <see cref="ItemComponentTable"/> is constructed (instance state with a table-scoped lifetime, not a mutable static).</summary>
internal abstract class NestedStackComponentCodec(DataComponentType type) : ItemComponentCodec(type)
{
    private ItemComponentTable? _table;

    /// <summary>The bound nested-stack era table.</summary>
    protected ItemComponentTable Table =>
        _table ?? throw new InvalidOperationException("Nested-stack component codec was used before its era table was bound.");

    /// <summary>Binds the era table for nested-stack decoding. Called once at table construction.</summary>
    public void Bind(ItemComponentTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (_table is not null)
            throw new InvalidOperationException("This nested-stack component codec is already bound to a table.");

        _table = table;
    }
}
