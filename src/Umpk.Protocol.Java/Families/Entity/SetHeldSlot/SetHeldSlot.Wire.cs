using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>1.8 held item slot: a single byte.</summary>
    public static readonly PacketCodec<ClientboundSetHeldSlotPacket> SetHeldSlotV1_8 =
        PacketCodec<ClientboundSetHeldSlotPacket>.Of(
            static (ref PacketWriter w, ClientboundSetHeldSlotPacket p, PacketCodecContext _) => w.WriteByte((byte)p.Slot),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetHeldSlotPacket(r.ReadByte()));

    /// <summary>Modern set held slot: a VarInt.</summary>
    public static readonly PacketCodec<ClientboundSetHeldSlotPacket> SetHeldSlotV1_21_4 =
        PacketCodec<ClientboundSetHeldSlotPacket>.Of(
            static (ref PacketWriter w, ClientboundSetHeldSlotPacket p, PacketCodecContext _) => w.WriteVarInt(p.Slot),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetHeldSlotPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetHeldSlot(PacketBindings bindings)
    {
        // 1.8 spells this held_item_slot; 107-767 spell it set_carried_item; 768 onward spells it set_held_slot again. It is the same byte-encoded hotbar slot through 1.21.3. Protocol 1.21.4 changes the field to a VarInt, so the boundary is 769 rather than 768. Values 0-8 encode as one byte in both forms, but larger values distinguish them.
        bindings.Packet(EntityPackets.Clientbound.SetHeldSlot)
            .From(JavaProtocols.V1_8, EntityStateCodecs.SetHeldSlotV1_8)
            .From(JavaProtocols.V1_21_4, EntityStateCodecs.SetHeldSlotV1_21_4)
            .AliasedAs(Identifier.Minecraft("held_item_slot"))
            .AliasedAs(Identifier.Minecraft("set_carried_item"), JavaProtocols.V1_9, JavaProtocols.V1_21);
    }
}
