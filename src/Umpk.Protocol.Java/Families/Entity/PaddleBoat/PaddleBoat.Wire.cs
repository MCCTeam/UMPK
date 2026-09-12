using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Paddle boat (modern): left and right paddle booleans.</summary>
    public static readonly PacketCodec<ServerboundPaddleBoatPacket> PaddleBoat =
        PacketCodec<ServerboundPaddleBoatPacket>.Of(
            static (ref PacketWriter w, ServerboundPaddleBoatPacket p, PacketCodecContext _) =>
            {
                w.WriteBool(p.Left);
                w.WriteBool(p.Right);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPaddleBoatPacket(r.ReadBool(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePaddleBoat(PacketBindings bindings)
    {
        // Two bools since the packet arrived with boats-with-paddles at 1.9.
        bindings.Packet(EntityPackets.Serverbound.PaddleBoat)
            .From(JavaEras.Combat, EntityServerboundCodecs.PaddleBoat);
    }
}
