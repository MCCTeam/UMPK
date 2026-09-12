using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class StatusCodecs
{
    /// <summary>Status response JSON blob.</summary>
    public static readonly PacketCodec<ClientboundStatusResponsePacket> Response =
        PacketCodec<ClientboundStatusResponsePacket>.Of(
            static (ref PacketWriter w, ClientboundStatusResponsePacket p, PacketCodecContext _) => w.WriteString(p.Json),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundStatusResponsePacket(r.ReadString()),
            WireShape.Of("string"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareStatusResponse(PacketBindings bindings)
    {
        bindings.Packet(StatusPackets.Clientbound.StatusResponse)
            .From(JavaProtocols.V1_8, StatusCodecs.Response);
    }
}
