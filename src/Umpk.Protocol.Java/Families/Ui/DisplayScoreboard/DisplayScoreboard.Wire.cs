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

public static partial class ScoreboardCodecs
{
    /// <summary>Legacy 1.8 display scoreboard (47).</summary>
    public static readonly PacketCodec<ClientboundLegacyDisplayObjectivePacket> LegacyDisplayObjectiveV1_8 =
        PacketCodec<ClientboundLegacyDisplayObjectivePacket>.Of(
            static (ref PacketWriter w, ClientboundLegacyDisplayObjectivePacket p, PacketCodecContext _) =>
            {
                w.WriteByte(p.Slot);
                w.WriteString(p.ObjectiveName, 16);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundLegacyDisplayObjectivePacket(r.ReadByte(), r.ReadString(16)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareDisplayScoreboard(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.LegacyDisplayObjective)
            .From(JavaProtocols.V1_8, ScoreboardCodecs.LegacyDisplayObjectiveV1_8);
    }
}
