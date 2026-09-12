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
    /// <summary>pick-item-from-block: blockpos, bool include data.</summary>
    public static PacketCodec<ServerboundPickItemFromBlockPacket> PickItemFromBlockModern { get; } =
        PacketCodec<ServerboundPickItemFromBlockPacket>.Of(
            static (ref PacketWriter w, ServerboundPickItemFromBlockPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteBool(p.IncludeData);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPickItemFromBlockPacket(r.ReadBlockPos(BlockPosLayout.Packed114), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePickItemFromBlock(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Serverbound.PickItemFromBlock)
            .From(JavaProtocols.V1_21_4, UseItemCodecs.PickItemFromBlockModern);
    }
}
