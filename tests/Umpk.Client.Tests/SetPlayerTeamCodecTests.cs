using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Scoreboard;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Verifies the session-fatal <c>set_player_team</c> misbinding. The 1.21.5 codec was bound from 1.14, so on 477-769 it read a VarInt where the wire carries a length-prefixed name-tag visibility STRING. That desynchronizes the rest of the parameter block and the fault surfaces several fields later on the prefix component as "Unknown NBT tag type N", with N varying by team name. A team that merely EXISTS on the server killed the client at join.</summary>
/// <remarks>
/// <para>On 1.14.4, 1.16.5, and 1.20.4 the parameter block contains component, options byte, name-tag visibility string, collision-rule string, color, prefix, and suffix. 1.21.5 keeps that order but replaces the two rule strings with VarInt ids 0-3.</para>
/// <para>No recorded corpus frame carries a team (the recording servers had none, which is exactly why this survived the byte-exact corpus gate), so every frame here is a literal byte array built from the vanilla write order and pushed through the real per-protocol descriptor and the real applier chain.</para>
/// </remarks>
public sealed class SetPlayerTeamCodecTests
{
    private const string TeamName = "redteam";
    private const string Visibility = "hideForOtherTeams";
    private const string Collision = "pushOwnTeam";
    private const int RedColorId = 12;
    private const byte Options = 0x03;

    // One constraint on these literals, a property of shared machinery rather than of this packet: they must avoid '<', '>' and '&', which the JSON writer escapes to \u003c style sequences, so a byte-exact re-encode assertion on the JSON band would be comparing against the unescaped form.
    //
    // A leading '[' or '{' is valid in an NBT string component and must not be re-parsed as JSON. The era is known at every call site, so the NBT string branch remains literal. The bracket-shaped prefix exercises that distinction.
    private const string Prefix = "[R] ";
    private const string Suffix = " END";

    /// <summary>The full JSON component band.</summary>
    [Theory]
    [InlineData(477)]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(754)]
    [InlineData(758)]
    [InlineData(760)]
    [InlineData(763)]
    [InlineData(764)]
    public async Task JsonComponentBand_DecodesTheTeamAndLandsItOnTheScoreboard(int protocol)
    {
        Team team = await ApplyAsync(protocol, Frame(TeamComponentEra.Json));
        AssertTeam(team);
    }

    /// <summary>765-769: components became network NBT at 1.20.3, the rule strings did not move.</summary>
    [Theory]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(769)]
    public async Task NbtComponentBand_DecodesTheTeamAndLandsItOnTheScoreboard(int protocol)
    {
        Team team = await ApplyAsync(protocol, Frame(TeamComponentEra.Nbt));
        AssertTeam(team);
    }

    /// <summary>770+: the rules are VarInt ids and the interaction shapes are modern.</summary>
    [Theory]
    [InlineData(770)]
    [InlineData(773)]
    [InlineData(775)]
    public async Task VarIntRuleBand_DecodesTheTeamAndLandsItOnTheScoreboard(int protocol)
    {
        Team team = await ApplyAsync(protocol, Frame(TeamComponentEra.VarIntRules));
        AssertTeam(team);
    }

    /// <summary>The exact live failure, pinned. Feeding a real 1.16.5-shaped frame to the 1.21.5 codec must not be silently accepted because that path produces an "Unknown NBT tag type N" session kill.</summary>
    [Fact]
    public void ModernCodec_RejectsThePreVarIntRuleFrame()
    {
        BoundPacketCodec modern = Bound(770);
        Assert.ThrowsAny<Exception>(() => modern.Decode(Frame(TeamComponentEra.Json), Context(770)));
        Assert.ThrowsAny<Exception>(() => modern.Decode(Frame(TeamComponentEra.Nbt), Context(770)));
    }

    /// <summary>And the reverse: the pre-1.21.5 codecs must not accept the VarInt-rule frame.</summary>
    [Theory]
    [InlineData(754)]
    [InlineData(765)]
    public void PreVarIntRuleCodecs_RejectTheModernFrame(int protocol) =>
        Assert.ThrowsAny<Exception>(() => Bound(protocol).Decode(Frame(TeamComponentEra.VarIntRules), Context(protocol)));

    /// <summary>The JSON and NBT bands are distinguishable too: a JSON-component frame fed to the 765 codec hits an NBT reader on a length-prefixed JSON string.</summary>
    [Fact]
    public void NbtBandCodec_RejectsTheJsonComponentFrame() =>
        Assert.ThrowsAny<Exception>(() => Bound(765).Decode(Frame(TeamComponentEra.Json), Context(765)));

    /// <summary>The 1.14 codec must reject the NBT frame for the same reason in the other direction: the NBT root tag byte is read as a string length prefix.</summary>
    [Fact]
    public void JsonBandCodec_RejectsTheNbtComponentFrame() =>
        Assert.ThrowsAny<Exception>(() => Bound(754).Decode(Frame(TeamComponentEra.Nbt), Context(754)));

    /// <summary>
    /// Every era re-encodes its own frame byte-for-byte, proving that the codec reproduces the wire order.
    /// <para>The JSON band uses the OBJECT component form: pre-1.20.3 writes a literal as <c>{"text":"..."}</c> and has no bare-string branch at all. <c>ComponentJsonLiteralForm</c> keys this spelling to the 1.20.3 boundary, so object-form frames round-trip byte for byte on the JSON band.</para>
    /// </summary>
    [Theory]
    [InlineData(754, TeamComponentEra.Json)]
    [InlineData(764, TeamComponentEra.Json)]
    [InlineData(765, TeamComponentEra.Nbt)]
    [InlineData(769, TeamComponentEra.Nbt)]
    [InlineData(770, TeamComponentEra.VarIntRules)]
    [InlineData(775, TeamComponentEra.VarIntRules)]
    public void EachWireLayout_ReEncodesItsOwnFrameByteExactly(int protocol, TeamComponentEra era)
    {
        byte[] frame = Frame(era, jsonObjectForm: true);
        BoundPacketCodec bound = Bound(protocol);
        object decoded = bound.Decode(frame, Context(protocol));

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, decoded, Context(protocol));
        Assert.Equal(frame, buffer.WrittenSpan.ToArray());
    }

    private static void AssertTeam(Team team)
    {
        Assert.Equal(TeamName, team.Name);
        Assert.Equal("Red Team", team.DisplayName.ToPlainText());
        Assert.Equal(Prefix, team.Prefix.ToPlainText());
        Assert.Equal(Suffix, team.Suffix.ToPlainText());
        Assert.Equal(NameTagVisibility.HideForOtherTeams, team.NameTagVisibility);
        Assert.Equal(CollisionRule.PushOwnTeam, team.CollisionRule);
        Assert.Equal(RedColorId, team.Color);
        Assert.Contains("Steve", team.Members);
        Assert.Contains("Alex", team.Members);
    }

    private static async Task<Team> ApplyAsync(int protocol, byte[] frame)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        object packet = Bound(protocol).Decode(frame, Context(protocol));
        var decoded = Assert.IsType<ClientboundSetPlayerTeamPacket>(packet);
        Assert.Equal(TeamName, decoded.Name);

        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        await harness.ApplyAsync(packet);

        Assert.True(harness.State.Scoreboard.TryGetTeam(TeamName, out Team? team));
        Assert.NotNull(team);
        return team!;
    }

    private static BoundPacketCodec Bound(int protocol) =>
        BoundDescriptorCodec.Clientbound(protocol, "set_player_team");

    private static PacketCodecContext Context(int protocol) =>
        new(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);

    /// <summary>The three distinguishable parameter-block encodings.</summary>
    public enum TeamComponentEra
    {
        /// <summary>477-764: JSON-string components, rule STRINGS.</summary>
        Json,

        /// <summary>765-769: network-NBT components, rule STRINGS.</summary>
        Nbt,

        /// <summary>770+: network-NBT components, rule VarInt ids.</summary>
        VarIntRules,
    }

    /// <summary>Builds a literal create-team frame in vanilla's write order: team name, method byte, then the parameter block (display name, options, name-tag visibility, collision rule, color, prefix, suffix), then the member list.</summary>
    /// <param name="era">Which component and rule encoding to emit.</param>
    /// <param name="jsonObjectForm">Whether the JSON band writes the object literal <c>{"text":"..."}</c> a vanilla server actually sends, or the collapsed bare JSON string the shared serializer re-emits.</param>
    private static byte[] Frame(TeamComponentEra era, bool jsonObjectForm = true)
    {
        var w = new FrameWriter();
        w.Str(TeamName);
        w.U8((byte)TeamMethod.Add);

        Component(w, era, "Red Team", jsonObjectForm);
        w.U8(Options);

        if (era == TeamComponentEra.VarIntRules)
        {
            w.VarInt((int)NameTagVisibility.HideForOtherTeams);
            w.VarInt((int)CollisionRule.PushOwnTeam);
        }
        else
        {
            w.Str(Visibility);
            w.Str(Collision);
        }

        w.VarInt(RedColorId);
        Component(w, era, Prefix, jsonObjectForm);
        Component(w, era, Suffix, jsonObjectForm);

        w.VarInt(2);
        w.Str("Steve");
        w.Str("Alex");
        return w.ToArray();
    }

    private static void Component(FrameWriter w, TeamComponentEra era, string text, bool jsonObjectForm)
    {
        if (era == TeamComponentEra.Json)
        {
            w.Str(jsonObjectForm ? "{\"text\":\"" + text + "\"}" : "\"" + text + "\"");
            return;
        }

        // Network NBT with a bare root tag: TAG_String (0x08), then a big-endian ushort length. A pure literal collapses to a bare string tag in both directions (ComponentNbt.To/ReadComponent).
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
        w.U8(0x08);
        w.U8((byte)(utf8.Length >> 8));
        w.U8((byte)utf8.Length);
        w.Raw(utf8);
    }

    private sealed class FrameWriter
    {
        private readonly List<byte> _bytes = [];

        public void U8(int value) => _bytes.Add((byte)value);

        public void VarInt(int value)
        {
            uint v = (uint)value;
            while ((v & ~0x7Fu) != 0)
            {
                _bytes.Add((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }

            _bytes.Add((byte)v);
        }

        public void Str(string value)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            VarInt(utf8.Length);
            _bytes.AddRange(utf8);
        }

        public void Raw(byte[] value) => _bytes.AddRange(value);

        public byte[] ToArray() => [.. _bytes];
    }
}
