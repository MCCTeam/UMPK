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
    /// <summary>Set action bar text (770/776).</summary>
    public static readonly PacketCodec<ClientboundSetActionBarTextPacket> SetActionBarTextV1_21_5 =
        PacketCodec<ClientboundSetActionBarTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetActionBarTextPacket p, PacketCodecContext _) => WriteModernComponent(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetActionBarTextPacket(ReadModernComponent(ref r)));

    /// <summary>755-764 set-action-bar-text (JSON string component).</summary>
    public static PacketCodec<ClientboundSetActionBarTextPacket> SetActionBarTextV1_17 { get; } =
        PacketCodec<ClientboundSetActionBarTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetActionBarTextPacket p, PacketCodecContext _) => WriteJson(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetActionBarTextPacket(ReadJson(ref r)));

    /// <summary>765-769 set-action-bar-text (NBT, legacy interactions).</summary>
    public static PacketCodec<ClientboundSetActionBarTextPacket> SetActionBarTextV1_20_3 { get; } =
        PacketCodec<ClientboundSetActionBarTextPacket>.Of(
            static (ref PacketWriter w, ClientboundSetActionBarTextPacket p, PacketCodecContext _) => WriteNbtText(ref w, p.Text),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetActionBarTextPacket(ReadNbtText(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetActionBarText(PacketBindings bindings)
    {
        // The three title text packets arrive at 1.17 (755) and share one timeline. Two boundaries and only two: components move from a JSON string to network NBT at 1.20.3 (765), and the click/hover interaction shapes move at 1.21.5 (770). The JSON, network-NBT legacy-interaction, and network-NBT modern-interaction forms therefore require separate bands.
        bindings.Packet(UiPackets.Clientbound.SetActionBarText)
            .From(JavaEras.Caves, TitleCodecs.SetActionBarTextV1_17)
            .From(JavaEras.ComponentNbtTransport, TitleCodecs.SetActionBarTextV1_20_3)
            .From(JavaEras.ModernComponents, TitleCodecs.SetActionBarTextV1_21_5);
    }
}
