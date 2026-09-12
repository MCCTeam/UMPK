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
    /// <summary>Play-phase pong response (770/776).</summary>
    public static readonly PacketCodec<ClientboundPlayPongResponsePacket> PongResponseV1_20_2 =
        PacketCodec<ClientboundPlayPongResponsePacket>.Of(
            static (ref PacketWriter w, ClientboundPlayPongResponsePacket p, PacketCodecContext _) => CommonPayloads.WritePingPayload(ref w, p.Time),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundPlayPongResponsePacket(CommonPayloads.ReadPingPayload(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePongResponsePlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.PongResponse)
            .From(JavaEras.ConfigurationPhase, UiMiscCodecs.PongResponseV1_20_2);
    }
}
