using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class ContainerCodecs
{
    /// <summary>1.8 clientbound confirm-transaction: ubyte window id, short action, bool accepted.</summary>
    public static PacketCodec<ClientboundTransactionPacket> TransactionClientV1_8 { get; } =
        PacketCodec<ClientboundTransactionPacket>.Of(
            static (ref PacketWriter w, ClientboundTransactionPacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort(p.ActionNumber);
                w.WriteBool(p.Accepted);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundTransactionPacket(r.ReadByte(), r.ReadShort(), r.ReadBool()));

    /// <summary>1.8 serverbound confirm-transaction.</summary>
    public static PacketCodec<ServerboundTransactionPacket> TransactionServerV1_8 { get; } =
        PacketCodec<ServerboundTransactionPacket>.Of(
            static (ref PacketWriter w, ServerboundTransactionPacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort(p.ActionNumber);
                w.WriteBool(p.Accepted);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundTransactionPacket(r.ReadByte(), r.ReadShort(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTransaction(PacketBindings bindings)
    {
        // The window-transaction packet. The 1.8 dataset spells it minecraft:transaction; every dataset from 1.9 to 1.16.4 spells the same wire minecraft:container_ack, so without the alias the whole 107-754 band fell through to a marker in both directions and the client could not see, let alone answer, a rejected click. The server drops subsequent container clicks after a rejection until the acknowledgement echo arrives. The wire is byte-identical across 47-754: i8 container id, i16 uid, i8 accepted. The packet disappears at 1.17, so no later dataset uses the name and the alias needs no upper bound.
        bindings.Packet(ItemPackets.Clientbound.Transaction)
            .From(JavaProtocols.V1_8, ContainerCodecs.TransactionClientV1_8)
            .AliasedAs(Identifier.Minecraft("container_ack"));

        // The serverbound echo, same 1.8 minecraft:transaction / 1.9-1.16.4 minecraft:container_ack name split as the clientbound half above.
        bindings.Packet(ItemPackets.Serverbound.Transaction)
            .From(JavaProtocols.V1_8, ContainerCodecs.TransactionServerV1_8)
            .AliasedAs(Identifier.Minecraft("container_ack"));
    }
}
