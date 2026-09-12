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
    /// <summary>Set score with the legacy interaction dialect. Bound below 770 for the same reason as <see cref="SetObjectiveV1_20_3"/>: the optional display name and the fixed number format are both components and both spell their interactions <c>clickEvent</c>/<c>hoverEvent</c> there.</summary>
    public static readonly PacketCodec<ClientboundSetScorePacket> SetScoreV1_20_3 =
        MakeSetScoreModern(ComponentWireEra.Legacy);

    /// <summary>Modern set-score (770+).</summary>
    public static readonly PacketCodec<ClientboundSetScorePacket> SetScoreV1_21_5 =
        MakeSetScoreModern(ComponentWireEra.Modern);

    private static PacketCodec<ClientboundSetScorePacket> MakeSetScoreModern(ComponentWireEra era) =>
        PacketCodec<ClientboundSetScorePacket>.Of(
            (ref PacketWriter w, ClientboundSetScorePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Owner);
                w.WriteString(p.ObjectiveName);
                w.WriteVarInt(p.Value);
                w.WriteOptional(p.DisplayName, (ref PacketWriter sw, Component c) => sw.WriteComponent(c, era, ModernNbt));
                WriteOptionalNumberFormat(ref w, p.NumberFormat, era);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string owner = r.ReadString();
                string objective = r.ReadString();
                int value = r.ReadVarInt();
                Component? display = r.ReadOptional((ref PacketReader sr) => sr.ReadComponent(era, ModernNbt));
                ScoreNumberFormat? nf = ReadOptionalNumberFormat(ref r, era);
                return new ClientboundSetScorePacket(owner, objective, value, display, nf);
            });

    /// <summary>Legacy 1.8 update-score (47).</summary>
    public static readonly PacketCodec<ClientboundLegacySetScorePacket> LegacySetScoreV1_8 =
        PacketCodec<ClientboundLegacySetScorePacket>.Of(
            static (ref PacketWriter w, ClientboundLegacySetScorePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Owner, 40);
                w.WriteVarInt((int)p.Action);
                w.WriteString(p.ObjectiveName, 16);
                if (p.Action != LegacyScoreAction.Remove)
                    w.WriteVarInt(p.Value);

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string owner = r.ReadString(40);
                var action = (LegacyScoreAction)r.ReadVarInt();
                string objective = r.ReadString(16);
                int value = action != LegacyScoreAction.Remove ? r.ReadVarInt() : 0;
                return new ClientboundLegacySetScorePacket(owner, action, objective, value);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetScore(PacketBindings bindings)
    {
        // owner(40) + VarInt action + objective(16) + a VarInt value for every action but REMOVE. That form runs from 47 all the way to 764, NOT to 476: the action-based packet survives the flattening untouched and the record only arrives at 1.20.3, which is also the release that moved the REMOVE action out into its own reset_score packet. The later record carries owner, objective name, VarInt score, nullable display component, and nullable number format. Treating the legacy action VarInt as the modern objective-name length misaligns the frame, so the legacy and modern records require separate bindings. The record's optional display name and its number format are both components, and their interaction dialect moves at 770 like every other component carrier, so the band splits again there.
        bindings.Packet(UiPackets.Clientbound.LegacySetScore)
            .From(JavaProtocols.V1_8, ScoreboardCodecs.LegacySetScoreV1_8)
            .FromAs(JavaProtocols.V1_20_3, UiPackets.Clientbound.SetScore, ScoreboardCodecs.SetScoreV1_20_3)
            .FromAs(JavaProtocols.V1_21_5, UiPackets.Clientbound.SetScore, ScoreboardCodecs.SetScoreV1_21_5);
    }
}
