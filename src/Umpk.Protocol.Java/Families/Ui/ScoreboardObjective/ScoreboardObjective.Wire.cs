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
    /// <summary>Legacy 1.8 scoreboard objective (47).</summary>
    public static readonly PacketCodec<ClientboundLegacyObjectivePacket> LegacyObjectiveV1_8 =
        PacketCodec<ClientboundLegacyObjectivePacket>.Of(
            static (ref PacketWriter w, ClientboundLegacyObjectivePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.ObjectiveName, 16);
                w.WriteByte((byte)p.Mode);
                if (p.Mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                {
                    w.WriteString(p.Value ?? throw new ProtocolViolationException("Add/change objective requires a value string."), 32);
                    w.WriteString(p.RenderType ?? throw new ProtocolViolationException("Add/change objective requires a render-type string."), 16);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(16);
                var mode = (ScoreboardObjectiveMode)r.ReadByte();
                if (mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                    return new ClientboundLegacyObjectivePacket(name, mode, r.ReadString(32), r.ReadString(16));

                return new ClientboundLegacyObjectivePacket(name, mode, null, null);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareScoreboardObjective(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.LegacyObjective)
            .From(JavaProtocols.V1_8, ScoreboardCodecs.LegacyObjectiveV1_8);
    }
}
