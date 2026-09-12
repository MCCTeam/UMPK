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
    /// <summary>Reset-score (770/776).</summary>
    public static readonly PacketCodec<ClientboundResetScorePacket> ResetScoreV1_20_3 =
        PacketCodec<ClientboundResetScorePacket>.Of(
            static (ref PacketWriter w, ClientboundResetScorePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Owner);
                w.WriteOptional(p.ObjectiveName, static (ref PacketWriter sw, string s) => sw.WriteString(s));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string owner = r.ReadString();
                string? objective = r.ReadOptional(static (ref PacketReader sr) => sr.ReadString());
                return new ClientboundResetScorePacket(owner, objective);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResetScore(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.ResetScore)
            .From(JavaEras.ComponentNbtTransport, ScoreboardCodecs.ResetScoreV1_20_3);
    }
}
