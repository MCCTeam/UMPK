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

public static partial class TitleCodecs
{
    /// <summary>Set subtitle text (770/776).</summary>
    public static readonly PacketCodec<ClientboundSetSubtitleTextPacket> SetSubtitleTextV1_21_5 =
        PacketCodec<ClientboundSetSubtitleTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetSubtitleTextPacket p, PacketCodecContext _) => WriteModernComponent(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetSubtitleTextPacket(ReadModernComponent(ref r)));

    /// <summary>755-764 set-subtitle-text (JSON string component).</summary>
    public static PacketCodec<ClientboundSetSubtitleTextPacket> SetSubtitleTextV1_17 { get; } =
        PacketCodec<ClientboundSetSubtitleTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetSubtitleTextPacket p, PacketCodecContext _) => WriteJson(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetSubtitleTextPacket(ReadJson(ref r)));

    /// <summary>765-769 set-subtitle-text (NBT, legacy interactions).</summary>
    public static PacketCodec<ClientboundSetSubtitleTextPacket> SetSubtitleTextV1_20_3 { get; } =
        PacketCodec<ClientboundSetSubtitleTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetSubtitleTextPacket p, PacketCodecContext _) => WriteNbtText(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetSubtitleTextPacket(ReadNbtText(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetSubtitleText(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.SetSubtitleText)
            .From(JavaEras.Caves, TitleCodecs.SetSubtitleTextV1_17)
            .From(JavaEras.ComponentNbtTransport, TitleCodecs.SetSubtitleTextV1_20_3)
            .From(JavaEras.ModernComponents, TitleCodecs.SetSubtitleTextV1_21_5);
    }
}
