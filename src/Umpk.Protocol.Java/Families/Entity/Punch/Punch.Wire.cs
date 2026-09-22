using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>26.3 punch: an empty body, so the frame is the wire id and nothing else.</summary>
    public static readonly PacketCodec<ServerboundPunchPacket> PunchV26_3 =
        PacketCodec<ServerboundPunchPacket>.Of(
            static (ref PacketWriter _, ServerboundPunchPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundPunchPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePunch(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.Punch)
            .From(JavaProtocols.V26_3, EntityServerboundCodecs.PunchV26_3);
    }
}
