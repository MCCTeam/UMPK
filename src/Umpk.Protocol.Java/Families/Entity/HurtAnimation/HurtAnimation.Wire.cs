using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Hurt animation (1.19.4+): entity id VarInt, yaw float.</summary>
    public static readonly PacketCodec<ClientboundHurtAnimationPacket> HurtAnimation =
        PacketCodec<ClientboundHurtAnimationPacket>.Of(
            static (ref PacketWriter w, ClientboundHurtAnimationPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteFloat(p.Yaw);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundHurtAnimationPacket(r.ReadVarInt(), r.ReadFloat()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareHurtAnimation(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.HurtAnimation)
            .From(JavaProtocols.V1_19_4, EntityStateCodecs.HurtAnimation);
    }
}
