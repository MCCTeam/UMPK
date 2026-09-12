using Umpk.Game.Entities;
using Umpk.Nbt;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>A wire-exact item-slot value retained by legacy entity-equipment packets and context-free metadata decoding. Live entity metadata uses registry-aware item-stack codecs and projects a generic <see cref="Umpk.Game.Items.ItemStack"/>.</summary>
/// <remarks>
/// The legacy (1.8) item format is self-delimiting, so <see cref="EntityItemSlot"/> keeps its parsed fields (<see cref="ItemId"/>, <see cref="Count"/>, <see cref="Damage"/>, <see cref="Tag"/>) and re-encodes them deterministically; legacy metadata/equipment therefore round-trips fully. The modern (1.20.5+ components) format requires the per-component codec table and therefore does not use this carrier. Implements <see cref="IMetadataSlot"/> so legacy equipment and context-free metadata values can live inside a <see cref="MetadataValue"/>.
/// <para>Entity-metadata slot decode must tolerate item ids that are absent from the item registry. Protocol 47 can emit block ids in metadata slots, such as id 4 for cobblestone, and the item-stack codec's registry resolve (<c>ItemStackCodecs.ReadLegacyStack</c>) throws on such ids, which faults the whole session. Any replacement for this raw-field seam therefore requires a tolerant resolve path (raw id carrier or block-id-aware registry), not the throwing lookup.</para>
/// </remarks>
public sealed record EntityItemSlot(bool IsEmpty, int ItemId, int Count, int Damage, NbtTag? Tag) : IMetadataSlot
{
    /// <summary>The empty (air) slot; the legacy wire form is the id short -1.</summary>
    public static EntityItemSlot Empty { get; } = new(true, -1, 0, 0, null);
}

/// <summary>Reads and writes legacy 1.8 <see cref="EntityItemSlot"/> values. The format is self-delimiting: short id; if id &gt;= 0, byte count, short damage, and a named-root NBT tag (a single 0 byte meaning no tag).</summary>
internal static class EntityItemSlotCodec
{
    /// <summary>Reads a legacy 1.8 item stack (exact because the format is self-delimiting).</summary>
    public static EntityItemSlot ReadLegacy(ref PacketReader r)
    {
        short id = r.ReadShort();
        if (id < 0)
            return EntityItemSlot.Empty;

        byte count = r.ReadByte();
        short damage = r.ReadShort();
        NbtTag tag = r.ReadNbt(NbtWireFormat.JavaNamedRoot);
        return new EntityItemSlot(false, id, count, damage, tag is NbtEnd ? null : tag);
    }

    /// <summary>Writes a legacy 1.8 item stack, mirroring the read path.</summary>
    public static void WriteLegacy(ref PacketWriter w, EntityItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.IsEmpty)
        {
            w.WriteShort(-1);
            return;
        }

        w.WriteShort((short)slot.ItemId);
        w.WriteByte((byte)slot.Count);
        w.WriteShort((short)slot.Damage);
        w.WriteNbt(slot.Tag ?? NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);
    }

    /// <summary>Reads a 1.13/1.13.1 item stack: short id (-1 = empty), byte count, named-root optional NBT. The flattening folded the damage short into the id and the NBT, so the field is simply gone; the present-flag/VarInt-id form arrives one patch later at 1.13.2.</summary>
    public static EntityItemSlot ReadShortId(ref PacketReader r)
    {
        short id = r.ReadShort();
        if (id < 0)
            return EntityItemSlot.Empty;

        byte count = r.ReadByte();
        NbtTag tag = r.ReadNbt(NbtWireFormat.JavaNamedRoot);
        return new EntityItemSlot(false, id, count, 0, tag is NbtEnd ? null : tag);
    }

    /// <summary>Writes a 1.13/1.13.1 item stack, mirroring <see cref="ReadShortId"/>.</summary>
    public static void WriteShortId(ref PacketWriter w, EntityItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.IsEmpty)
        {
            w.WriteShort(-1);
            return;
        }

        w.WriteShort((short)slot.ItemId);
        w.WriteByte((byte)slot.Count);
        w.WriteNbt(slot.Tag ?? NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);
    }

    /// <summary>Reads a 1.13.2 through 1.20.1 item stack: a present bool, then VarInt id, byte count and named-root optional NBT. Kept in this entity-family seam rather than delegating to the item family because entity slots must tolerate ids the item registry does not know; see the type remarks.</summary>
    public static EntityItemSlot ReadPresentId(ref PacketReader r)
    {
        if (!r.ReadBool())
            return EntityItemSlot.Empty;

        int id = r.ReadVarInt();
        byte count = r.ReadByte();
        NbtTag tag = r.ReadNbt(NbtWireFormat.JavaNamedRoot);
        return new EntityItemSlot(false, id, count, 0, tag is NbtEnd ? null : tag);
    }

    /// <summary>Writes a 1.13.2 through 1.20.1 item stack, mirroring <see cref="ReadPresentId"/>.</summary>
    public static void WritePresentId(ref PacketWriter w, EntityItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.IsEmpty)
        {
            w.WriteBool(false);
            return;
        }

        w.WriteBool(true);
        w.WriteVarInt(slot.ItemId);
        w.WriteByte((byte)slot.Count);
        w.WriteNbt(slot.Tag ?? NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);
    }
}
