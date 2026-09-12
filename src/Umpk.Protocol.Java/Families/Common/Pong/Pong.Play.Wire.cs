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
    /// <summary>Play-phase pong (770/776 serverbound).</summary>
    public static readonly PacketCodec<ServerboundPlayPongPacket> ServerPongV1_17 =
        PacketCodec<ServerboundPlayPongPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayPongPacket p, PacketCodecContext _) => CommonPayloads.WritePingId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundPlayPongPacket(CommonPayloads.ReadPingId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePongPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Serverbound.Pong)
            .From(JavaEras.Caves, UiMiscCodecs.ServerPongV1_17);
    }
}
