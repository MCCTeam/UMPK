using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>1.8 collect item: collected id VarInt and collector id VarInt.</summary>
    public static readonly PacketCodec<ClientboundTakeItemEntityPacket> TakeItemEntityV1_8 =
        PacketCodec<ClientboundTakeItemEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundTakeItemEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ItemEntityId);
                w.WriteVarInt(p.CollectorEntityId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundTakeItemEntityPacket(r.ReadVarInt(), r.ReadVarInt(), null));

    /// <summary>Modern take item entity: collected id, collector id, amount VarInt.</summary>
    public static readonly PacketCodec<ClientboundTakeItemEntityPacket> TakeItemEntityV1_11 =
        PacketCodec<ClientboundTakeItemEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundTakeItemEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ItemEntityId);
                w.WriteVarInt(p.CollectorEntityId);
                w.WriteVarInt(p.Amount ?? 0);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundTakeItemEntityPacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTakeItemEntity(PacketBindings bindings)
    {
        // The picked-up amount is a third VarInt added at 1.11: the 1.9, 1.9.4 and 1.10.2 collect-item packets read two VarInts, the 1.11, 1.11.2, 1.12, 1.12.1, 1.12.2 and 1.13.x ones read three.
        bindings.Packet(EntityPackets.Clientbound.TakeItemEntity)
            .From(JavaProtocols.V1_8, EntityStateCodecs.TakeItemEntityV1_8)
            .From(JavaProtocols.V1_11, EntityStateCodecs.TakeItemEntityV1_11)
            .AliasedAs(Identifier.Minecraft("collect_item"));
    }
}
