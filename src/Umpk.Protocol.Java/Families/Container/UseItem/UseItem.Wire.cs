using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class UseItemCodecs
{
    /// <summary>1.9-1.18.2 (107-758) use-item: a single hand VarInt, and nothing else. The sequence VarInt arrives at 1.19 and the yRot/xRot floats at 1.21, so neither belongs on this band.</summary>
    public static PacketCodec<ServerboundUseItemPacket> UseItemV1_9 { get; } =
        PacketCodec<ServerboundUseItemPacket>.Of(
            static (ref PacketWriter w, ServerboundUseItemPacket p, PacketCodecContext _) => w.WriteVarInt(p.Hand),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundUseItemPacket(r.ReadVarInt(), 0, 0f, 0f));

    /// <summary>1.21+ (767 and up) use-item: hand enum, VarInt sequence, float yRot, float xRot. The two rotation floats are the 1.21 addition; 1.20.6 (766) still writes only hand + sequence.</summary>
    public static PacketCodec<ServerboundUseItemPacket> UseItemModern { get; } =
        PacketCodec<ServerboundUseItemPacket>.Of(
            static (ref PacketWriter w, ServerboundUseItemPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Hand);
                w.WriteVarInt(p.Sequence);
                w.WriteFloat(p.YRot);
                w.WriteFloat(p.XRot);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundUseItemPacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadFloat(), r.ReadFloat()));

    /// <summary>1.19-1.20.6 (759-766) use-item: hand + sequence, and no rotation floats (those are 1.21+, where <see cref="UseItemModern"/> takes over). The 1.19 addition is the sequence VarInt, retained unchanged through 1.20.6.</summary>
    public static PacketCodec<ServerboundUseItemPacket> UseItemV1_19 { get; } =
        PacketCodec<ServerboundUseItemPacket>.Of(
            static (ref PacketWriter w, ServerboundUseItemPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Hand);
                w.WriteVarInt(p.Sequence);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundUseItemPacket(r.ReadVarInt(), r.ReadVarInt(), 0f, 0f));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUseItem(PacketBindings bindings)
    {
        // The use-item packet grows in two steps. Protocols 107-758 carry the hand VarInt alone; the sequence VarInt arrives at 1.19 (759) and the yRot/xRot floats at 1.21 (767); the gaps between those eras retain the corresponding wire form. Binding a later codec to an earlier band leaves trailing fields after the server finishes reading the packet and terminates the session.
        bindings.Packet(ItemPackets.Serverbound.UseItem)
            .From(JavaProtocols.V1_9, UseItemCodecs.UseItemV1_9)
            .From(JavaProtocols.V1_19, UseItemCodecs.UseItemV1_19)
            .From(JavaProtocols.V1_21, UseItemCodecs.UseItemModern);
    }
}
