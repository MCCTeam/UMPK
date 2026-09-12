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
    /// <summary>pick-item-from-entity: VarInt entity id, bool include data.</summary>
    public static PacketCodec<ServerboundPickItemFromEntityPacket> PickItemFromEntityModern { get; } =
        PacketCodec<ServerboundPickItemFromEntityPacket>.Of(
            static (ref PacketWriter w, ServerboundPickItemFromEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteBool(p.IncludeData);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPickItemFromEntityPacket(r.ReadVarInt(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePickItemFromEntity(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Serverbound.PickItemFromEntity)
            .From(JavaProtocols.V1_21_4, UseItemCodecs.PickItemFromEntityModern);
    }
}
