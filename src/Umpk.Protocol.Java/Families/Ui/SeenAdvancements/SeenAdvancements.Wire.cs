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
    /// <summary>Seen advancements (770/776).</summary>
    public static readonly PacketCodec<ServerboundSeenAdvancementsPacket> SeenAdvancementsV1_12 =
        PacketCodec<ServerboundSeenAdvancementsPacket>.Of(
            static (ref PacketWriter w, ServerboundSeenAdvancementsPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt((int)p.Action);
                if (p.Action == SeenAdvancementsAction.OpenedTab)
                    WriteIdentifier(ref w, p.Tab ?? throw new ProtocolViolationException("Opened-tab requires a tab id."));

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                var action = (SeenAdvancementsAction)r.ReadVarInt();
                Identifier? tab = action == SeenAdvancementsAction.OpenedTab ? ReadIdentifier(ref r) : null;
                return new ServerboundSeenAdvancementsPacket(action, tab);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSeenAdvancements(PacketBindings bindings)
    {
        // Constant wire form since 1.12: the VarInt action ordinal, plus the tab identifier only for opened-tab.
        bindings.Packet(UiPackets.Serverbound.SeenAdvancements)
            .From(JavaProtocols.V1_12, AdvancementCodecs.SeenAdvancementsV1_12);
    }
}
