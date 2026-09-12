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
    /// <summary>Set title animation times (770/776).</summary>
    public static readonly PacketCodec<ClientboundSetTitlesAnimationPacket> SetTitlesAnimationV1_17 =
        PacketCodec<ClientboundSetTitlesAnimationPacket>.Of(
            static (ref PacketWriter w, ClientboundSetTitlesAnimationPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.FadeIn);
                w.WriteInt(p.Stay);
                w.WriteInt(p.FadeOut);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetTitlesAnimationPacket(r.ReadInt(), r.ReadInt(), r.ReadInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetTitlesAnimation(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.SetTitlesAnimation)
            .From(JavaEras.Caves, TitleCodecs.SetTitlesAnimationV1_17);
    }
}
