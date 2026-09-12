using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class StatusCodecs
{
    /// <summary>Pong reply echoing the ping nonce.</summary>
    public static readonly PacketCodec<ClientboundPongResponsePacket> Pong =
        PacketCodec<ClientboundPongResponsePacket>.Of(
            static (ref PacketWriter w, ClientboundPongResponsePacket p, PacketCodecContext _) => CommonPayloads.WritePingPayload(ref w, p.Payload),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundPongResponsePacket(CommonPayloads.ReadPingPayload(ref r)),
            WireShape.Of("long"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePongResponseStatus(PacketBindings bindings)
    {
        bindings.Packet(StatusPackets.Clientbound.PongResponse)
            .From(JavaProtocols.V1_8, StatusCodecs.Pong);
    }
}
