using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>1.8 base no-op entity movement: just the entity id.</summary>
    public static readonly PacketCodec<ClientboundEntityPacket> EntityV1_8 =
        PacketCodec<ClientboundEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundEntityPacket p, PacketCodecContext _) => w.WriteVarInt(p.EntityId),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundEntityPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareEntity(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.Entity)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.EntityV1_8);
    }
}
