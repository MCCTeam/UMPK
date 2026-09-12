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

public static partial class ResourcePackCodecs
{
    /// <summary>Resource pack pop (770/776).</summary>
    public static readonly PacketCodec<ClientboundResourcePackPopPacket> ResourcePackPopV1_20_3 =
        PacketCodec<ClientboundResourcePackPopPacket>.Of(
            static (ref PacketWriter w, ClientboundResourcePackPopPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundResourcePackPopPacket(CommonPayloads.ReadResourcePackId(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResourcePackPopPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.ResourcePackPop)
            .From(JavaEras.ComponentNbtTransport, ResourcePackCodecs.ResourcePackPopV1_20_3);
    }
}
