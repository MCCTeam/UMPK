using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Seeded round-trip tests for the scoreboard and team codecs across 47/770/776.</summary>
public class ScoreboardCodecTests
{
    [Theory]
    [InlineData(ScoreboardObjectiveMode.Add)]
    [InlineData(ScoreboardObjectiveMode.Change)]
    [InlineData(ScoreboardObjectiveMode.Remove)]
    public void SetObjective_Modern_RoundTrips(ScoreboardObjectiveMode mode)
    {
        bool hasBody = mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change;
        var packet = new ClientboundSetObjectivePacket(
            "obj_1", mode,
            hasBody ? Component.Text("Kills") : null,
            ObjectiveRenderType.Hearts,
            hasBody ? new ScoreNumberFormat(ScoreNumberFormatKind.Fixed, null, Component.Text("*")) : null);

        var decoded = CodecRoundTrip.Cycle(ScoreboardCodecs.SetObjectiveV1_21_5, packet);
        Assert.Equal(packet.ObjectiveName, decoded.ObjectiveName);
        Assert.Equal(packet.Mode, decoded.Mode);
        if (hasBody)
        {
            Assert.Equal(ObjectiveRenderType.Hearts, decoded.RenderType);
            Assert.Equal(ScoreNumberFormatKind.Fixed, decoded.NumberFormat!.Kind);
        }
    }

    [Fact]
    public void SetObjective_Modern_BlankAndStyledNumberFormats_RoundTrip()
    {
        var blank = new ClientboundSetObjectivePacket("o", ScoreboardObjectiveMode.Add, Component.Text("x"),
            ObjectiveRenderType.Integer, new ScoreNumberFormat(ScoreNumberFormatKind.Blank, null, null));
        Assert.Equal(ScoreNumberFormatKind.Blank, CodecRoundTrip.Cycle(ScoreboardCodecs.SetObjectiveV1_21_5, blank).NumberFormat!.Kind);

        var style = new NbtCompound();
        style.PutString("color", "red");
        var styled = new ClientboundSetObjectivePacket("o", ScoreboardObjectiveMode.Add, Component.Text("x"),
            ObjectiveRenderType.Integer, new ScoreNumberFormat(ScoreNumberFormatKind.Styled, style, null));
        Assert.Equal(ScoreNumberFormatKind.Styled, CodecRoundTrip.Cycle(ScoreboardCodecs.SetObjectiveV1_21_5, styled).NumberFormat!.Kind);
    }

    [Fact]
    public void SetObjective_Modern_NoNumberFormat_RoundTrips()
    {
        var packet = new ClientboundSetObjectivePacket("o", ScoreboardObjectiveMode.Change, Component.Text("y"),
            ObjectiveRenderType.Integer, null);
        Assert.Null(CodecRoundTrip.Cycle(ScoreboardCodecs.SetObjectiveV1_21_5, packet).NumberFormat);
    }

    [Theory]
    [InlineData(ScoreboardObjectiveMode.Add)]
    [InlineData(ScoreboardObjectiveMode.Remove)]
    public void LegacyObjective_RoundTrips(ScoreboardObjectiveMode mode)
    {
        bool hasBody = mode != ScoreboardObjectiveMode.Remove;
        var packet = new ClientboundLegacyObjectivePacket("obj", mode, hasBody ? "Score" : null, hasBody ? "integer" : null);
        var decoded = CodecRoundTrip.Cycle(ScoreboardCodecs.LegacyObjectiveV1_8, packet);
        Assert.Equal(packet, decoded);
    }

    [Fact]
    public void SetScore_Modern_RoundTrips_WithAndWithoutOptionals()
    {
        var full = new ClientboundSetScorePacket("Player", "obj", 42, Component.Text("disp"),
            new ScoreNumberFormat(ScoreNumberFormatKind.Blank, null, null));
        var decodedFull = CodecRoundTrip.Cycle(ScoreboardCodecs.SetScoreV1_21_5, full);
        Assert.Equal(42, decodedFull.Value);
        Assert.NotNull(decodedFull.DisplayName);
        Assert.NotNull(decodedFull.NumberFormat);

        var bare = new ClientboundSetScorePacket("Player", "obj", -7, null, null);
        var decodedBare = CodecRoundTrip.Cycle(ScoreboardCodecs.SetScoreV1_21_5, bare);
        Assert.Equal(-7, decodedBare.Value);
        Assert.Null(decodedBare.DisplayName);
        Assert.Null(decodedBare.NumberFormat);
    }

    [Theory]
    [InlineData(LegacyScoreAction.Change, 15)]
    [InlineData(LegacyScoreAction.Remove, 0)]
    public void LegacySetScore_RoundTrips(LegacyScoreAction action, int value)
    {
        var packet = new ClientboundLegacySetScorePacket("Owner", action, "obj", value);
        var decoded = CodecRoundTrip.Cycle(ScoreboardCodecs.LegacySetScoreV1_8, packet);
        Assert.Equal(packet.Owner, decoded.Owner);
        Assert.Equal(action, decoded.Action);
        if (action != LegacyScoreAction.Remove)
            Assert.Equal(value, decoded.Value);

    }

    [Fact]
    public void ResetScore_RoundTrips_WithAndWithoutObjective()
    {
        Assert.Equal("Owner", CodecRoundTrip.Cycle(ScoreboardCodecs.ResetScoreV1_20_3, new ClientboundResetScorePacket("Owner", "obj")).Owner);
        Assert.Null(CodecRoundTrip.Cycle(ScoreboardCodecs.ResetScoreV1_20_3, new ClientboundResetScorePacket("Owner", null)).ObjectiveName);
        Assert.Equal("obj", CodecRoundTrip.Cycle(ScoreboardCodecs.ResetScoreV1_20_3, new ClientboundResetScorePacket("Owner", "obj")).ObjectiveName);
    }

    [Fact]
    public void SetDisplayObjective_RoundTrips()
    {
        var modern = new ClientboundSetDisplayObjectivePacket(1, "sidebar_obj");
        Assert.Equal(modern, CodecRoundTrip.Cycle(ScoreboardCodecs.SetDisplayObjectiveV1_20_2, modern));

        var legacy = new ClientboundLegacyDisplayObjectivePacket(1, "obj");
        Assert.Equal(legacy, CodecRoundTrip.Cycle(ScoreboardCodecs.LegacyDisplayObjectiveV1_8, legacy));
    }

    [Theory]
    [InlineData(TeamMethod.Add)]
    [InlineData(TeamMethod.Change)]
    [InlineData(TeamMethod.Remove)]
    [InlineData(TeamMethod.AddPlayers)]
    [InlineData(TeamMethod.RemovePlayers)]
    public void SetPlayerTeam_V1_21_5_RoundTrips(TeamMethod method)
    {
        bool hasParams = method is TeamMethod.Add or TeamMethod.Change;
        bool hasPlayers = method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers;
        var packet = new ClientboundSetPlayerTeamPacket(
            "team1", method,
            hasParams ? new TeamParameters(Component.Text("Red"), Component.Text("pre"), Component.Text("suf"),
                NameTagVisibility.HideForOtherTeams, CollisionRule.PushOwnTeam, 12, 0x03) : null,
            hasPlayers ? ["alice", "bob"] : []);

        var decoded = CodecRoundTrip.Cycle(ScoreboardCodecs.SetPlayerTeamV1_21_5, packet);
        Assert.Equal("team1", decoded.Name);
        Assert.Equal(method, decoded.Method);
        if (hasParams)
        {
            Assert.Equal(NameTagVisibility.HideForOtherTeams, decoded.Parameters!.NameTagVisibility);
            Assert.Equal(CollisionRule.PushOwnTeam, decoded.Parameters.CollisionRule);
            Assert.Equal(12, decoded.Parameters.Color);
            Assert.Equal(0x03, decoded.Parameters.Options);
        }

        if (hasPlayers)
            Assert.Equal(2, decoded.Players.Count);

    }

    [Fact]
    public void SetPlayerTeam_V26_2_RoundTrips_WithOptionalColor()
    {
        var withColor = new ClientboundSetPlayerTeamPacket("t", TeamMethod.Add,
            new TeamParameters(Component.Text("D"), Component.Text("P"), Component.Text("S"),
                NameTagVisibility.Always, CollisionRule.Never, 14, 0x01), ["x"]);
        var decodedColor = CodecRoundTrip.Cycle(ScoreboardCodecs.SetPlayerTeamV26_2, withColor);
        Assert.Equal(14, decodedColor.Parameters!.Color);

        var noColor = withColor with { Parameters = withColor.Parameters! with { Color = null } };
        var decodedNoColor = CodecRoundTrip.Cycle(ScoreboardCodecs.SetPlayerTeamV26_2, noColor);
        Assert.Null(decodedNoColor.Parameters!.Color);
    }

    [Fact]
    public void SetPlayerTeam_V26_2_EncodesDifferentBytesThan_V1_21_5()
    {
        // The 26.2 layout reorders parameters and makes color optional, so the wire bytes differ even
        // for a value both eras can represent (color present).
        var packet = new ClientboundSetPlayerTeamPacket("t", TeamMethod.Add,
            new TeamParameters(Component.Text("D"), Component.Text("P"), Component.Text("S"),
                NameTagVisibility.Never, CollisionRule.Always, 5, 0x02), []);
        byte[] v1215 = CodecRoundTrip.Encode(ScoreboardCodecs.SetPlayerTeamV1_21_5, packet);
        byte[] v262 = CodecRoundTrip.Encode(ScoreboardCodecs.SetPlayerTeamV26_2, packet);
        Assert.NotEqual(v1215, v262);
    }

    [Fact]
    public void SetPlayerTeam_V1_8_RoundTrips()
    {
        var packet = new ClientboundSetPlayerTeamPacket("t", TeamMethod.Add,
            new TeamParameters(Component.Text("Display"), Component.Text("pre"), Component.Text("suf"),
                NameTagVisibility.HideForOwnTeam, CollisionRule.Always, 9, 0x03), ["p1", "p2"]);
        var decoded = CodecRoundTrip.Cycle(ScoreboardCodecs.SetPlayerTeamV1_8, packet);
        Assert.Equal("Display", decoded.Parameters!.DisplayName.ToPlainText());
        Assert.Equal(NameTagVisibility.HideForOwnTeam, decoded.Parameters.NameTagVisibility);
        Assert.Equal(9, decoded.Parameters.Color);
        Assert.Equal(2, decoded.Players.Count);
    }

    [Fact]
    public void SetPlayerTeam_V1_8_NegativeColor_MapsToNull()
    {
        var packet = new ClientboundSetPlayerTeamPacket("t", TeamMethod.Add,
            new TeamParameters(Component.Text("D"), Component.Empty, Component.Empty,
                NameTagVisibility.Always, CollisionRule.Always, null, 0), []);
        var decoded = CodecRoundTrip.Cycle(ScoreboardCodecs.SetPlayerTeamV1_8, packet);
        Assert.Null(decoded.Parameters!.Color);
    }
}
