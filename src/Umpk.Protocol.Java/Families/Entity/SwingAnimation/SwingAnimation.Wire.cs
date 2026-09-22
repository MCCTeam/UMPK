using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>26.3 swing animation: VarInt entity id, VarInt hand, VarInt animation type, VarInt duration.</summary>
    public static readonly PacketCodec<ClientboundSwingAnimationPacket> SwingAnimationV26_3 =
        PacketCodec<ClientboundSwingAnimationPacket>.Of(
            static (ref PacketWriter w, ClientboundSwingAnimationPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.Hand);
                w.WriteVarInt(p.AnimationType);
                w.WriteVarInt(p.Duration);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSwingAnimationPacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt()),
            WireShape.Of("varint,varint,varint,varint"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSwingAnimation(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.SwingAnimation)
            .From(JavaProtocols.V26_3, EntityStateCodecs.SwingAnimationV26_3);
    }
}
