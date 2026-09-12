using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class StatusCodecs
{
    /// <summary>Ping request carrying a nonce.</summary>
    public static readonly PacketCodec<ServerboundPingRequestPacket> Ping =
        PacketCodec<ServerboundPingRequestPacket>.Of(
            static (ref PacketWriter w, ServerboundPingRequestPacket p, PacketCodecContext _) => CommonPayloads.WritePingPayload(ref w, p.Payload),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundPingRequestPacket(CommonPayloads.ReadPingPayload(ref r)),
            WireShape.Of("long"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePingRequestStatus(PacketBindings bindings)
    {
        bindings.Packet(StatusPackets.Serverbound.PingRequest)
            .From(JavaProtocols.V1_8, StatusCodecs.Ping);
    }
}
