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

public static partial class AdvancementCodecs
{
    /// <summary>Select advancements tab (770/776).</summary>
    public static readonly PacketCodec<ClientboundSelectAdvancementsTabPacket> SelectAdvancementsTabV1_12 =
        PacketCodec<ClientboundSelectAdvancementsTabPacket>.Of(
            static (ref PacketWriter w, ClientboundSelectAdvancementsTabPacket p, PacketCodecContext _) =>
                w.WriteOptionalStruct(p.Tab, WriteIdentifier),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSelectAdvancementsTabPacket(r.ReadBool() ? ReadIdentifier(ref r) : null));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSelectAdvancementsTab(PacketBindings bindings)
    {
        // Constant wire form since advancements arrived at 1.12: an optional tab identifier.
        bindings.Packet(UiPackets.Clientbound.SelectAdvancementsTab)
            .From(JavaProtocols.V1_12, AdvancementCodecs.SelectAdvancementsTabV1_12);
    }
}
