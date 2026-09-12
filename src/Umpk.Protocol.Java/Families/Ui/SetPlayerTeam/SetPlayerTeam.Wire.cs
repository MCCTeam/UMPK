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
    /// <summary>Set player team, 477-764 layout (1.14-1.20.2): JSON-string components, and the name-tag visibility and collision rule as <c>readUtf(40)</c> STRINGS.</summary>
    public static readonly PacketCodec<ClientboundSetPlayerTeamPacket> SetPlayerTeamV1_14 = Team(TeamWire.V1_14);

    /// <summary>Set player team, 765-769 layout (1.20.3-1.21.4): the components became network NBT at 1.20.3 (<c>readComponentTrusted</c>) but the two rules are still strings; the interaction shapes and the VarInt rule ids both arrive at 1.21.5.</summary>
    public static readonly PacketCodec<ClientboundSetPlayerTeamPacket> SetPlayerTeamV1_20_3 = Team(TeamWire.V1_20_3);

    /// <summary>Set player team, 770 layout (1.21.5): modern components and VarInt rule ids.</summary>
    public static readonly PacketCodec<ClientboundSetPlayerTeamPacket> SetPlayerTeamV1_21_5 = Team(TeamWire.V1_21_5);

    /// <summary>Set player team, 776 layout (26.2: reordered parameters + optional TeamColor).</summary>
    public static readonly PacketCodec<ClientboundSetPlayerTeamPacket> SetPlayerTeamV26_2 = Team(TeamWire.V26_2);

    /// <summary>Set player team, 107-340 (1.9-1.12.2): the 47 layout with a COLLISION-RULE string inserted after the name-tag-visibility string. Everything else is unchanged from 1.8, so the only wire delta in this whole band is that one extra field.</summary>
    /// <remarks>The collision-rule wire strings are <c>always</c>, <c>never</c>, <c>pushOtherTeams</c>, and <c>pushOwnTeam</c>, matching this library's <see cref="CollisionRule"/>. Binding the 47 codec here would leave the collision string unread and fault the frame; binding the 1.14+ codec would read network-NBT components out of plain strings.</remarks>
    public static readonly PacketCodec<ClientboundSetPlayerTeamPacket> SetPlayerTeamV1_9 =
        PacketCodec<ClientboundSetPlayerTeamPacket>.Of(
            static (ref PacketWriter w, ClientboundSetPlayerTeamPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Name, 16);
                w.WriteByte((byte)p.Method);
                if (p.Method is TeamMethod.Add or TeamMethod.Change)
                {
                    TeamParameters prm = p.Parameters ?? throw new ProtocolViolationException("Add/change team requires parameters.");
                    w.WriteString(prm.DisplayName.ToPlainText(), 32);
                    w.WriteString(prm.Prefix.ToPlainText(), 16);
                    w.WriteString(prm.Suffix.ToPlainText(), 16);
                    w.WriteByte(prm.Options);
                    w.WriteString(NameTagVisibilityWire(prm.NameTagVisibility), 32);
                    w.WriteString(CollisionRuleWire(prm.CollisionRule), 32);
                    w.WriteByte(unchecked((byte)(sbyte)(prm.Color ?? -1)));
                }

                if (p.Method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers)
                    w.WriteList(p.Players, static (ref PacketWriter sw, string s) => sw.WriteString(s, 40));

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(16);
                var method = (TeamMethod)r.ReadByte();
                TeamParameters? prm = null;
                if (method is TeamMethod.Add or TeamMethod.Change)
                {
                    Component display = Component.Text(r.ReadString(32));
                    Component prefix = Component.Text(r.ReadString(16));
                    Component suffix = Component.Text(r.ReadString(16));
                    byte options = r.ReadByte();
                    NameTagVisibility vis = NameTagVisibilityFromWire(r.ReadString(32));
                    CollisionRule collision = CollisionRuleFromWire(r.ReadString(32));
                    int color = r.ReadSByte();
                    prm = new TeamParameters(display, prefix, suffix, vis, collision, color < 0 ? null : color, options);
                }

                string[] players = method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers
                    ? r.ReadList(static (ref PacketReader sr) => sr.ReadString(40))
                    : [];
                return new ClientboundSetPlayerTeamPacket(name, method, prm, players);
            });

    /// <summary>Set player team, 393-404 (1.13-1.13.2): the flattening turned the three team texts into JSON-STRING components, moved the prefix and suffix to the END of the parameter block, and replaced the signed color byte with a VarInt <c>ChatFormatting</c> ordinal.</summary>
    /// <remarks>This uses a different field order and encoding from 107-340, so neither neighbouring codec can stand in: the 1.9 form would read the component JSON as the display string and then take the options byte out of the middle of the prefix. The visibility and collision fields stay wire STRINGS here; they only become VarInt enums at 1.21.5.</remarks>
    public static readonly PacketCodec<ClientboundSetPlayerTeamPacket> SetPlayerTeamV1_13 =
        PacketCodec<ClientboundSetPlayerTeamPacket>.Of(
            static (ref PacketWriter w, ClientboundSetPlayerTeamPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Name, 16);
                w.WriteByte((byte)p.Method);
                if (p.Method is TeamMethod.Add or TeamMethod.Change)
                {
                    TeamParameters prm = p.Parameters ?? throw new ProtocolViolationException("Add/change team requires parameters.");
                    WriteLegacyComponent(ref w, prm.DisplayName);
                    w.WriteByte(prm.Options);
                    w.WriteString(NameTagVisibilityWire(prm.NameTagVisibility), 40);
                    w.WriteString(CollisionRuleWire(prm.CollisionRule), 40);
                    w.WriteVarInt(prm.Color ?? throw new ProtocolViolationException("A 1.13 team color is a mandatory ChatFormatting ordinal."));
                    WriteLegacyComponent(ref w, prm.Prefix);
                    WriteLegacyComponent(ref w, prm.Suffix);
                }

                if (p.Method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers)
                    w.WriteList(p.Players, static (ref PacketWriter sw, string s) => sw.WriteString(s, 40));

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(16);
                var method = (TeamMethod)r.ReadByte();
                TeamParameters? prm = null;
                if (method is TeamMethod.Add or TeamMethod.Change)
                {
                    Component display = ReadLegacyComponent(ref r);
                    byte options = r.ReadByte();
                    NameTagVisibility vis = NameTagVisibilityFromWire(r.ReadString(40));
                    CollisionRule collision = CollisionRuleFromWire(r.ReadString(40));
                    int color = r.ReadVarInt();
                    Component prefix = ReadLegacyComponent(ref r);
                    Component suffix = ReadLegacyComponent(ref r);
                    prm = new TeamParameters(display, prefix, suffix, vis, collision, color, options);
                }

                string[] players = method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers
                    ? r.ReadList(static (ref PacketReader sr) => sr.ReadString(40))
                    : [];
                return new ClientboundSetPlayerTeamPacket(name, method, prm, players);
            });

    /// <summary>Set player team, 47 layout (strings for visibility, byte color).</summary>
    public static readonly PacketCodec<ClientboundSetPlayerTeamPacket> SetPlayerTeamV1_8 =
        PacketCodec<ClientboundSetPlayerTeamPacket>.Of(
            static (ref PacketWriter w, ClientboundSetPlayerTeamPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Name, 16);
                w.WriteByte((byte)p.Method);
                if (p.Method is TeamMethod.Add or TeamMethod.Change)
                {
                    TeamParameters prm = p.Parameters ?? throw new ProtocolViolationException("Add/change team requires parameters.");
                    w.WriteString(prm.DisplayName.ToPlainText(), 32);
                    w.WriteString(prm.Prefix.ToPlainText(), 16);
                    w.WriteString(prm.Suffix.ToPlainText(), 16);
                    w.WriteByte(prm.Options);
                    w.WriteString(NameTagVisibilityWire(prm.NameTagVisibility), 32);
                    w.WriteByte(unchecked((byte)(sbyte)(prm.Color ?? -1)));
                }

                if (p.Method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers)
                    w.WriteList(p.Players, static (ref PacketWriter sw, string s) => sw.WriteString(s, 40));

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(16);
                var method = (TeamMethod)r.ReadByte();
                TeamParameters? prm = null;
                if (method is TeamMethod.Add or TeamMethod.Change)
                {
                    Component display = Component.Text(r.ReadString(32));
                    Component prefix = Component.Text(r.ReadString(16));
                    Component suffix = Component.Text(r.ReadString(16));
                    byte options = r.ReadByte();
                    NameTagVisibility vis = NameTagVisibilityFromWire(r.ReadString(32));
                    int color = r.ReadSByte();
                    prm = new TeamParameters(display, prefix, suffix, vis, CollisionRule.Always, color < 0 ? null : color, options);
                }

                string[] players = method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers
                    ? r.ReadList(static (ref PacketReader sr) => sr.ReadString(40))
                    : [];
                return new ClientboundSetPlayerTeamPacket(name, method, prm, players);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetPlayerTeam(PacketBindings bindings)
    {
        // Binding the 1.21.5 codec before protocol 770 is session-fatal: it reads a VarInt where the wire carries a length-prefixed name-tag-visibility string, desynchronizing the remaining parameter block. There are three boundaries: the components are a JSON string through 764 and network NBT from 765, the rule strings become VarInt ids and the interactions modernize at 770, and 26.2 reorders the block and makes the color optional.
        bindings.Packet(UiPackets.Clientbound.SetPlayerTeam)
            .From(JavaProtocols.V1_8, ScoreboardCodecs.SetPlayerTeamV1_8)
            .From(JavaProtocols.V1_9, ScoreboardCodecs.SetPlayerTeamV1_9)
            .From(JavaProtocols.V1_13, ScoreboardCodecs.SetPlayerTeamV1_13)
            .From(JavaProtocols.V1_14, ScoreboardCodecs.SetPlayerTeamV1_14)
            .From(JavaProtocols.V1_20_3, ScoreboardCodecs.SetPlayerTeamV1_20_3)
            .From(JavaProtocols.V1_21_5, ScoreboardCodecs.SetPlayerTeamV1_21_5)
            .From(JavaProtocols.V26_2, ScoreboardCodecs.SetPlayerTeamV26_2);
    }
}
