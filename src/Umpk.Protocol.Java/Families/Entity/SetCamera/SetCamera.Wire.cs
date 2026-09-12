using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Camera (all versions): the spectated entity id VarInt.</summary>
    public static readonly PacketCodec<ClientboundSetCameraPacket> SetCamera =
        PacketCodec<ClientboundSetCameraPacket>.Of(
            static (ref PacketWriter w, ClientboundSetCameraPacket p, PacketCodecContext _) => w.WriteVarInt(p.CameraId),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetCameraPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetCamera(PacketBindings bindings)
    {
        // One wire form on every protocol: the spectated entity id as a VarInt.
        bindings.Packet(EntityPackets.Clientbound.SetCamera)
            .From(JavaProtocols.V1_8, EntityStateCodecs.SetCamera)
            .AliasedAs(Identifier.Minecraft("camera"));
    }
}
