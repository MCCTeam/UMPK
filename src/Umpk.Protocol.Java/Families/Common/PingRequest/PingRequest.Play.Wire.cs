using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class UiMiscCodecs
{
    /// <summary>Play-phase ping request (770/776 serverbound).</summary>
    public static readonly PacketCodec<ServerboundPlayPingRequestPacket> ServerPingRequestV1_20_2 =
        PacketCodec<ServerboundPlayPingRequestPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayPingRequestPacket p, PacketCodecContext _) => CommonPayloads.WritePingPayload(ref w, p.Time),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundPlayPingRequestPacket(CommonPayloads.ReadPingPayload(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePingRequestPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Serverbound.PingRequest)
            .From(JavaEras.ConfigurationPhase, UiMiscCodecs.ServerPingRequestV1_20_2);
    }
}
