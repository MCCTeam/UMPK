using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class StatusCodecs
{
    /// <summary>The empty status request.</summary>
    public static readonly PacketCodec<ServerboundStatusRequestPacket> Request =
        PacketCodec<ServerboundStatusRequestPacket>.Of(
            static (ref PacketWriter _, ServerboundStatusRequestPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundStatusRequestPacket(),
            WireShape.Of("empty"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareStatusRequest(PacketBindings bindings)
    {
        bindings.Packet(StatusPackets.Serverbound.StatusRequest)
            .From(JavaProtocols.V1_8, StatusCodecs.Request);
    }
}
