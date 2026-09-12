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
    /// <summary>Play-phase ping (770/776).</summary>
    public static readonly PacketCodec<ClientboundPlayPingPacket> PingV1_17 =
        PacketCodec<ClientboundPlayPingPacket>.Of(
            static (ref PacketWriter w, ClientboundPlayPingPacket p, PacketCodecContext _) => CommonPayloads.WritePingId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundPlayPingPacket(CommonPayloads.ReadPingId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePingPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.Ping)
            .From(JavaEras.Caves, UiMiscCodecs.PingV1_17);
    }
}
