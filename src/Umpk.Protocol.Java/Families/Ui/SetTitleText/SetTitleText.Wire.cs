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
    /// <summary>Set title text (770/776).</summary>
    public static readonly PacketCodec<ClientboundSetTitleTextPacket> SetTitleTextV1_21_5 =
        PacketCodec<ClientboundSetTitleTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetTitleTextPacket p, PacketCodecContext _) => WriteModernComponent(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetTitleTextPacket(ReadModernComponent(ref r)));

    /// <summary>755-764 set-title-text with a JSON string component.</summary>
    public static PacketCodec<ClientboundSetTitleTextPacket> SetTitleTextV1_17 { get; } =
        PacketCodec<ClientboundSetTitleTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetTitleTextPacket p, PacketCodecContext _) => WriteJson(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetTitleTextPacket(ReadJson(ref r)));

    /// <summary>765-769 set-title-text (NBT, legacy interactions).</summary>
    public static PacketCodec<ClientboundSetTitleTextPacket> SetTitleTextV1_20_3 { get; } =
        PacketCodec<ClientboundSetTitleTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetTitleTextPacket p, PacketCodecContext _) => WriteNbtText(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetTitleTextPacket(ReadNbtText(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetTitleText(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.SetTitleText)
            .From(JavaEras.Caves, TitleCodecs.SetTitleTextV1_17)
            .From(JavaEras.ComponentNbtTransport, TitleCodecs.SetTitleTextV1_20_3)
            .From(JavaEras.ModernComponents, TitleCodecs.SetTitleTextV1_21_5);
    }
}
