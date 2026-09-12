using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayCommonCodecs
{
    /// <summary>Bundle delimiter: an empty body, so the frame is the wire id and nothing else.</summary>
    public static readonly PacketCodec<ClientboundBundleDelimiterPacket> BundleDelimiter =
        PacketCodec<ClientboundBundleDelimiterPacket>.Of(
            static (ref PacketWriter _, ClientboundBundleDelimiterPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ClientboundBundleDelimiterPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBundleDelimiter(PacketBindings bindings)
    {
        // Bundle delimiter (1.19.4+). The frame has no payload; decoding it opens or closes bundle accumulation and enables the bundle size and terminal-packet checks.
        bindings.Packet(PlayPackets.Clientbound.BundleDelimiter)
            .From(JavaProtocols.V1_19_4, PlayCommonCodecs.BundleDelimiter);
    }
}
