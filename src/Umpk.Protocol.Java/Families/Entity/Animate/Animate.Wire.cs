using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Animation (all versions): entity id VarInt, action byte.</summary>
    public static readonly PacketCodec<ClientboundAnimatePacket> Animate =
        PacketCodec<ClientboundAnimatePacket>.Of(
            static (ref PacketWriter w, ClientboundAnimatePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteByte(p.Action);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundAnimatePacket(r.ReadVarInt(), r.ReadByte()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAnimate(PacketBindings bindings)
    {
        // One wire form on every protocol: VarInt entity id + action byte.
        bindings.Packet(EntityPackets.Clientbound.Animate)
            .From(JavaProtocols.V1_8, EntityStateCodecs.Animate);
    }
}
