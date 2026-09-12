using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Scoreboard;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>On protocols 765-776 a component uses network NBT, and a literal collapses to a bare TAG_String. A string that starts with <c>{</c> or <c>[</c> must remain literal instead of being parsed as JSON.</summary>
/// <remarks>
/// <para>In the network-NBT form, a string component remains literal even when it starts with <c>{</c> or <c>[</c>. Earlier wire layouts encode components as JSON instead.</para>
/// <para>Every frame here is a literal byte array in the required wire order for its era, pushed through the real per-protocol descriptor and the real applier chain, so nothing about the assertion depends on UMPK's own encoder agreeing with itself. The tests cover both <c>set_player_team</c> and <c>tab_list</c> because they share the component reader.</para>
/// </remarks>
public sealed class BracketPrefixComponentTests
{
    private const string TeamName = "staff";
    private const int RedColorId = 12;
    private const byte Options = 0x03;

    /// <summary>The values that are hostile to a "does it look like JSON" guess. The first is the realistic one; the rest are the shapes that made the guess unsalvageable, including a value that IS well-formed JSON but is meant literally and an empty string.</summary>
    public static TheoryData<string> HostileValues =>
    [
        "[Admin] ",
        "[",
        "{",
        "{\"not\":\"json\"",
        "[]",
        "{}",
        "{\"text\":\"x\"}",
        "  [spaced] ",
        "",
    ];

    /// <summary>Three protocols, one per team parameter-block layout.</summary>
    public static TheoryData<int> Protocols => [765, 770, 776];

    /// <summary>A bracket-prefixed rank tag remains literal on protocols 765, 770, and 776.</summary>
    [Theory]
    [InlineData(765)]
    [InlineData(770)]
    [InlineData(776)]
    public async Task Team_BracketPrefix_ArrivesIntact(int protocol)
    {
        Team team = await ApplyTeamAsync(protocol, "[Admin] ", " [!]");
        Assert.Equal("[Admin] ", team.Prefix.ToPlainText());
        Assert.Equal(" [!]", team.Suffix.ToPlainText());
        Assert.Equal("[Staff]", team.DisplayName.ToPlainText());
    }

    /// <summary>Every hostile value, on every era, through the team packet.</summary>
    [Theory]
    [MemberData(nameof(HostileValues))]
    public async Task Team_HostileValues_ArriveIntactOnEveryWireLayout(string value)
    {
        foreach (int protocol in new[] { 765, 770, 776 })
        {
            Team team = await ApplyTeamAsync(protocol, value, value);
            Assert.Equal(value, team.Prefix.ToPlainText());
            Assert.Equal(value, team.Suffix.ToPlainText());
        }
    }

    /// <summary>A second component-carrying packet, because the fault was in shared machinery rather than in the team codec: the tab-list header and footer are two bare components on 765-776.</summary>
    [Theory]
    [InlineData(765)]
    [InlineData(770)]
    [InlineData(776)]
    public async Task TabList_BracketHeaderAndFooter_ArriveIntact(int protocol)
    {
        ApplierHarness harness = await ApplyTabListAsync(protocol, "[Admin] ", "[]");
        Assert.Equal("[Admin] ", harness.State.TabList.Header!.ToPlainText());
        Assert.Equal("[]", harness.State.TabList.Footer!.ToPlainText());
    }

    /// <summary>Every hostile value, on every era, through the tab-list packet.</summary>
    [Theory]
    [MemberData(nameof(HostileValues))]
    public async Task TabList_HostileValues_ArriveIntactOnEveryWireLayout(string value)
    {
        foreach (int protocol in new[] { 765, 770, 776 })
        {
            ApplierHarness harness = await ApplyTabListAsync(protocol, value, value);
            Assert.Equal(value, harness.State.TabList.Header!.ToPlainText());
            Assert.Equal(value, harness.State.TabList.Footer!.ToPlainText());
        }
    }

    /// <summary>The literal survives a re-encode too, so a bracket-prefixed component is not merely readable but reproducible: it re-emits as the same bare TAG_String the server sent.</summary>
    [Theory]
    [InlineData(765)]
    [InlineData(770)]
    [InlineData(776)]
    public void TabList_BracketHeader_ReEncodesByteExactly(int protocol)
    {
        byte[] frame = TabListFrame("[Admin] ", "{}");
        BoundPacketCodec bound = BoundDescriptorCodec.Clientbound(protocol, "tab_list");
        object decoded = bound.Decode(frame, Context(protocol));

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, decoded, Context(protocol));
        Assert.Equal(frame, buffer.WrittenSpan.ToArray());
    }

    private static async Task<Team> ApplyTeamAsync(int protocol, string prefix, string suffix)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        object packet = BoundDescriptorCodec.Clientbound(protocol, "set_player_team")
            .Decode(TeamFrame(protocol, prefix, suffix), Context(protocol));
        var decoded = Assert.IsType<ClientboundSetPlayerTeamPacket>(packet);
        Assert.Equal(TeamName, decoded.Name);

        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        await harness.ApplyAsync(packet);

        Assert.True(harness.State.Scoreboard.TryGetTeam(TeamName, out Team? team));
        Assert.NotNull(team);
        return team!;
    }

    private static async Task<ApplierHarness> ApplyTabListAsync(int protocol, string header, string footer)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        object packet = BoundDescriptorCodec.Clientbound(protocol, "tab_list")
            .Decode(TabListFrame(header, footer), Context(protocol));
        Assert.IsType<ClientboundTabListPacket>(packet);

        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        await harness.ApplyAsync(packet);
        return harness;
    }

    private static PacketCodecContext Context(int protocol) =>
        new(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);

    /// <summary>A create-team frame in the vanilla write order for the era. 765 keeps the 1.14 field order with the two rules as strings; 770 swaps those two for VarInt ids; 776 reorders the block to display, prefix, suffix, visibility, collision, optional color, options.</summary>
    private static byte[] TeamFrame(int protocol, string prefix, string suffix)
    {
        var w = new FrameWriter();
        w.Str(TeamName);
        w.U8((byte)TeamMethod.Add);

        if (protocol >= 776)
        {
            w.Nbt("[Staff]");
            w.Nbt(prefix);
            w.Nbt(suffix);
            w.VarInt((int)NameTagVisibility.HideForOtherTeams);
            w.VarInt((int)CollisionRule.PushOwnTeam);
            w.U8(1);
            w.VarInt(RedColorId);
            w.U8(Options);
        }
        else
        {
            w.Nbt("[Staff]");
            w.U8(Options);
            if (protocol >= 770)
            {
                w.VarInt((int)NameTagVisibility.HideForOtherTeams);
                w.VarInt((int)CollisionRule.PushOwnTeam);
            }
            else
            {
                w.Str("hideForOtherTeams");
                w.Str("pushOwnTeam");
            }

            w.VarInt(RedColorId);
            w.Nbt(prefix);
            w.Nbt(suffix);
        }

        w.VarInt(1);
        w.Str("Steve");
        return w.ToArray();
    }

    /// <summary>Two bare network-NBT components; the shape is identical on 765, 770 and 776.</summary>
    private static byte[] TabListFrame(string header, string footer)
    {
        var w = new FrameWriter();
        w.Nbt(header);
        w.Nbt(footer);
        return w.ToArray();
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

        /// <summary>A literal component as a vanilla server sends it on 1.20.3+: an unnamed root TAG_String (0x08) then a big-endian ushort byte length then the UTF-8 bytes. No JSON is involved at any point, which is exactly why the value must come back verbatim.</summary>
        public void Nbt(string text)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
            U8(0x08);
            U8(utf8.Length >> 8);
            U8(utf8.Length);
            _bytes.AddRange(utf8);
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
