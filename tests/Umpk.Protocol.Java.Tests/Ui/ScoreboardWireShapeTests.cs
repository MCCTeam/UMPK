using System.Buffers;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Pins literal frames and incompatible layouts for boss-bar and scoreboard packets.</summary>
public sealed class ScoreboardWireShapeTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void BossEvent_LiteralAddFrame_CarriesJsonTitle(int protocol)
    {
        byte[] frame = Cat(
            Uuid(SampleUuid),
            VarInt(0),                                  // ADD
            Str("{\"text\":\"Wither\"}"),
            F32(0.75f),
            VarInt(2),                                  // colour
            VarInt(1),                                  // overlay
            [0x05]);                                    // flags
        var p = (ClientboundBossEventPacket)Clientbound(protocol, "boss_event").DecodeFrame(frame);
        Assert.Equal(SampleUuid, p.Id);
        Assert.Equal(BossEventOperation.Add, p.Operation);
        Assert.Equal("Wither", p.Title!.ToPlainText());
        Assert.Equal(0.75f, p.Progress);
        Assert.Equal(2, (int)p.Color);
        Assert.Equal(1, (int)p.Overlay);
        Assert.Equal(5, (int)p.Flags);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void SetDisplayObjective_LiteralFrame_IsAByteSlot(int protocol)
    {
        byte[] frame = Cat([1], Str("kills"));
        var p = (ClientboundSetDisplayObjectivePacket)Clientbound(protocol, "set_display_objective").DecodeFrame(frame);
        Assert.Equal(1, p.Slot);
        Assert.Equal("kills", p.ObjectiveName);
        Assert.Equal(frame, Clientbound(protocol, "set_display_objective").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    public void SetObjective_PlainTextFrame_IsPlainStrings(int protocol)
    {
        byte[] frame = Cat(Str("kills"), [0], Str("Kill Count"), Str("hearts"));
        var p = (ClientboundSetObjectivePacket)Clientbound(protocol, "set_objective").DecodeFrame(frame);
        Assert.Equal("kills", p.ObjectiveName);
        Assert.Equal(ScoreboardObjectiveMode.Add, p.Mode);
        Assert.Equal("Kill Count", p.DisplayName!.ToPlainText());
        Assert.Equal(ObjectiveRenderType.Hearts, p.RenderType);
        Assert.Equal(frame, Clientbound(protocol, "set_objective").Encode(p));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    public void SetObjective_ComponentFrame_IsComponentAndEnum(int protocol)
    {
        byte[] frame = Cat(Str("kills"), [2], Str("{\"text\":\"Kill Count\"}"), VarInt(1));
        var p = (ClientboundSetObjectivePacket)Clientbound(protocol, "set_objective").DecodeFrame(frame);
        Assert.Equal(ScoreboardObjectiveMode.Change, p.Mode);
        Assert.Equal("Kill Count", p.DisplayName!.ToPlainText());
        Assert.Equal(ObjectiveRenderType.Hearts, p.RenderType);
        Assert.Null(p.NumberFormat);

        // A JSON component does not re-encode byte-identically: vanilla writes the object form and this library's serializer emits the canonical bare-string form for a plain text component. The round trip is therefore asserted semantically, on a re-decode of the re-encoded frame.
        var again = (ClientboundSetObjectivePacket)Clientbound(protocol, "set_objective")
            .DecodeFrame(Clientbound(protocol, "set_objective").Encode(p));
        Assert.Equal(p, again);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    public void SetPlayerTeam_PlainTextFrame_HasTheCollisionRule(int protocol)
    {
        byte[] frame = Cat(
            Str("red"), [0],
            Str("Red Team"), Str("["), Str("]"),
            [0x03],
            Str("hideForOtherTeams"),
            Str("pushOwnTeam"),
            [0x0C],
            VarInt(2), Str("alice"), Str("bob"));
        var p = (ClientboundSetPlayerTeamPacket)Clientbound(protocol, "set_player_team").DecodeFrame(frame);
        Assert.Equal("red", p.Name);
        Assert.Equal(TeamMethod.Add, p.Method);
        Assert.Equal("Red Team", p.Parameters!.DisplayName.ToPlainText());
        Assert.Equal("[", p.Parameters.Prefix.ToPlainText());
        Assert.Equal(NameTagVisibility.HideForOtherTeams, p.Parameters.NameTagVisibility);
        Assert.Equal(CollisionRule.PushOwnTeam, p.Parameters.CollisionRule);
        Assert.Equal(12, p.Parameters.Color);
        Assert.Equal(new[] { "alice", "bob" }, p.Players);
        Assert.Equal(frame, Clientbound(protocol, "set_player_team").Encode(p));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    public void SetPlayerTeam_ComponentFrame_ReordersAndComponentises(int protocol)
    {
        byte[] frame = Cat(
            Str("blue"), [0],
            Str("{\"text\":\"Blue Team\"}"),
            [0x01],
            Str("never"),
            Str("pushOtherTeams"),
            VarInt(9),
            Str("{\"text\":\"<\"}"),
            Str("{\"text\":\">\"}"),
            VarInt(1), Str("carol"));
        var p = (ClientboundSetPlayerTeamPacket)Clientbound(protocol, "set_player_team").DecodeFrame(frame);
        Assert.Equal("blue", p.Name);
        Assert.Equal("Blue Team", p.Parameters!.DisplayName.ToPlainText());
        Assert.Equal("<", p.Parameters.Prefix.ToPlainText());
        Assert.Equal(">", p.Parameters.Suffix.ToPlainText());
        Assert.Equal(NameTagVisibility.Never, p.Parameters.NameTagVisibility);
        Assert.Equal(CollisionRule.PushOtherTeams, p.Parameters.CollisionRule);
        Assert.Equal(9, p.Parameters.Color);
        Assert.Equal(1, p.Parameters.Options);
        Assert.Equal(new[] { "carol" }, p.Players);

        // JSON components re-encode in canonical form (see the objective test), so the round trip is asserted on a re-decode rather than on the bytes.
        var again = (ClientboundSetPlayerTeamPacket)Clientbound(protocol, "set_player_team")
            .DecodeFrame(Clientbound(protocol, "set_player_team").Encode(p));
        Assert.Equal(p.Parameters, again.Parameters);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void SetScore_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(Str("alice"), VarInt(0), Str("kills"), VarInt(42));
        var p = (ClientboundLegacySetScorePacket)Clientbound(protocol, "set_score").DecodeFrame(frame);
        Assert.Equal("alice", p.Owner);
        Assert.Equal("kills", p.ObjectiveName);
        Assert.Equal(42, p.Value);
        Assert.Equal(frame, Clientbound(protocol, "set_score").Encode(p));
    }

    [Fact]
    public void SetPlayerTeam_AlternateWireShapes_AreRejected()
    {
        byte[] v1_9 = Cat(
            Str("red"), [0], Str("Red Team"), Str("["), Str("]"), [0x03],
            Str("hideForOtherTeams"), Str("pushOwnTeam"), [0x0C], VarInt(1), Str("alice"));
        byte[] v1_13 = Cat(
            Str("blue"), [0], Str("{\"text\":\"Blue Team\"}"), [0x01],
            Str("never"), Str("pushOtherTeams"), VarInt(9),
            Str("{\"text\":\"<\"}"), Str("{\"text\":\">\"}"), VarInt(1), Str("carol"));

        Rejects(Clientbound(393, "set_player_team"), v1_9, "1.13 reads the display name as a component");
        Rejects(Clientbound(340, "set_player_team"), v1_13, "1.12.2 has a different field order and a byte colour");
        Rejects(Clientbound(47, "set_player_team"), v1_9, "1.8 has no collision-rule string");
    }

    [Fact]
    public void SetObjective_AlternateWireShapes_AreRejected()
    {
        byte[] v1_9 = Cat(Str("kills"), [0], Str("Kill Count"), Str("hearts"));
        byte[] v1_13 = Cat(Str("kills"), [0], Str("{\"text\":\"Kill Count\"}"), VarInt(1));

        Rejects(Clientbound(393, "set_objective"), v1_9, "1.13 reads the display name as a component and the render type as an enum");
        Rejects(Clientbound(340, "set_objective"), v1_13, "1.12.2 reads the render type as a string");
        Rejects(Clientbound(477, "set_objective"), v1_9, "1.14 reads the display name as a component");
    }

    [Fact]
    public void SetDisplayObjective_RejectsVariableSlotFrame()
    {
        // A slot above 127 is the only region where a byte and a VarInt differ; vanilla never sends one, which is precisely why the width difference is invisible in traffic and must be pinned here.
        byte[] byteSlot = Cat([0xC8], Str("kills"));
        Rejects(Clientbound(770, "set_display_objective"), byteSlot, "1.21.5 reads the slot as a VarInt");

        var legacy = (ClientboundSetDisplayObjectivePacket)Clientbound(404, "set_display_objective").DecodeFrame(byteSlot);
        Assert.Equal(0xC8, legacy.Slot);
    }
}
