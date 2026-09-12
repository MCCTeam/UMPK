using System.Text;
using Umpk.Game.Players;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Binding-level pins for the component click/hover INTERACTION era. A component's interaction shapes change at 1.21.5 (770), independently of the JSON-to-network-NBT transport move at 1.20.3 (765).</summary>
/// <remarks>
/// In 1.21.4, a click event stores its value under <c>value</c>, and style stores optional events under <c>clickEvent</c> and <c>hoverEvent</c>. In 1.21.5, click events dispatch on <c>action</c>, while style uses <c>click_event</c> and <c>hover_event</c>. Hover events move to the dispatched form at the same boundary.
/// <para>These assertions go through <see cref="BoundCodec"/> rather than naming a codec because they verify the binding table. Each codec can be correct independently while the timeline selects it at the wrong boundary; a test that constructs a codec directly cannot observe that mismatch.</para>
/// <para>Every payload here carries BOTH a click event and a hover event with a non-trivial body. Plain text serializes identically under both eras - that is the whole reason a two-version misbinding survived captures, round-trip tests and live sessions.</para>
/// </remarks>
public class ComponentInteractionBindingTests
{
    private static readonly Guid EntityId = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    private static readonly Guid PackId = new("00112233-4455-6677-8899-aabbccddeeff");

    /// <summary>The probe component: an open_url click plus a show_entity hover. Those two are the maximally era-sensitive pair. The click's payload field is <c>value</c> under the flat form and <c>url</c> under the dispatched form; the hover nests its body under <c>contents</c> with a <c>type</c> key under the legacy form and inlines it beside <c>action</c> with an <c>uuid</c> key under the modern one.</summary>
    private static Component Probe { get; } = new(
        new TextContent("u4f"),
        new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid/u4f"),
            HoverEvent = new HoverShowEntity("minecraft:pig", EntityId, Component.Text("Pig")),
        });

    // Markers that appear literally in both wire transports: NBT compound keys are raw UTF-8 with a length prefix, and the JSON form spells the same names. So one marker set covers 763 and 776.
    private static readonly string[] LegacyMarkers = ["clickEvent", "hoverEvent", "contents"];
    private static readonly string[] ModernMarkers = ["click_event", "hover_event", "uuid"];

    private static bool Contains(byte[] frame, string marker) =>
        frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker).AsSpan()) >= 0;

    private static void AssertLegacyEra(byte[] frame, string what)
    {
        foreach (string m in LegacyMarkers)
            Assert.True(Contains(frame, m), $"{what}: expected the legacy marker '{m}' on the wire.");

        foreach (string m in ModernMarkers)
            Assert.False(Contains(frame, m), $"{what}: modern marker '{m}' must not appear on a legacy-era frame.");

    }

    private static void AssertModernEra(byte[] frame, string what)
    {
        foreach (string m in ModernMarkers)
            Assert.True(Contains(frame, m), $"{what}: expected the modern marker '{m}' on the wire.");

        foreach (string m in LegacyMarkers)
            Assert.False(Contains(frame, m), $"{what}: legacy marker '{m}' must not appear on a modern-era frame.");

    }

    private static void AssertEra(int protocol, PacketFlow flow, string identifier, object packet)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, flow, identifier);
        byte[] frame = bound.Encode(packet);
        string what = $"{identifier} @ {protocol}";
        if (protocol >= 770)
            AssertModernEra(frame, what);

        else
            AssertLegacyEra(frame, what);

        // The frame must also survive the live entry point frame-exactly: decode it and re-encode it through the same bound codec and the bytes must be identical. (Byte equality rather than packet equality: a couple of these records hold arrays and compare by reference.)
        Assert.Equal(frame, bound.Encode(bound.DecodeFrame(frame)));
    }

    // Protocol 770 begins modern interactions; adjacent legacy protocols and the latest protocol pin both sides of the boundary.
    private const int P1_20 = 763;      // JSON string transport, legacy interactions
    private const int P1_20_3 = 765;    // network NBT, legacy interactions
    private const int P1_21 = 767;      // network NBT, legacy interactions
    private const int P1_21_2 = 768;    // network NBT, legacy interactions
    private const int P1_21_4 = 769;    // network NBT, legacy interactions (WAS bound modern)
    private const int P1_21_5 = 770;    // network NBT, MODERN interactions
    private const int P26_2 = 776;      // network NBT, modern interactions

    [Theory]
    [InlineData(P1_20)]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void SetTitleText_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(protocol, PacketFlow.Clientbound, "minecraft:set_title_text", new ClientboundSetTitleTextPacket(Probe));

    [Theory]
    [InlineData(P1_20)]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void SetSubtitleText_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(protocol, PacketFlow.Clientbound, "minecraft:set_subtitle_text", new ClientboundSetSubtitleTextPacket(Probe));

    [Theory]
    [InlineData(P1_20)]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void SetActionBarText_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(protocol, PacketFlow.Clientbound, "minecraft:set_action_bar_text", new ClientboundSetActionBarTextPacket(Probe));

    [Theory]
    [InlineData(P1_20)]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void TabList_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(protocol, PacketFlow.Clientbound, "minecraft:tab_list", new ClientboundTabListPacket(Probe, Probe));

    [Theory]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void ServerData_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(protocol, PacketFlow.Clientbound, "minecraft:server_data", new ClientboundServerDataPacket(Probe, null));

    [Theory]
    [InlineData(P1_20)]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void SystemChat_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(protocol, PacketFlow.Clientbound, "minecraft:system_chat", new ClientboundSystemChatPacket(Probe, Overlay: false));

    [Theory]
    [InlineData(P1_20)]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void DisguisedChat_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(protocol, PacketFlow.Clientbound, "minecraft:disguised_chat", new ClientboundDisguisedChatPacket(Probe, 3, Probe, Probe));

    [Theory]
    [InlineData(477)]           // 1.14, the first protocol the boss-bar body is modelled on
    [InlineData(P1_20)]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void BossEvent_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(
            protocol,
            PacketFlow.Clientbound,
            "minecraft:boss_event",
            new ClientboundBossEventPacket(
                EntityId, BossEventOperation.Add, Probe, 0.5f, BossBarColor.Red, BossBarOverlay.Notched6, BossBarFlags.DarkenScreen));

    [Theory]
    [InlineData(761)]           // 1.19.3, where the split player-info pair arrives
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void PlayerInfoUpdate_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(
            protocol,
            PacketFlow.Clientbound,
            "minecraft:player_info_update",
            new ClientboundPlayerInfoUpdatePacket(
                PlayerInfoActions.UpdateDisplayName,
                [new PlayerInfoEntry(EntityId, null, null, false, null, GameMode.Undefined, false, 0, Probe, 0, false)]));

    [Theory]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void ResourcePackPush_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(
            protocol,
            PacketFlow.Clientbound,
            "minecraft:resource_pack_push",
            new ClientboundResourcePackPushPacket(PackId, "https://example.invalid/pack.zip", "0123456789abcdef0123456789abcdef01234567", true, Probe));

    [Theory]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void ServerLinks_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(
            protocol,
            PacketFlow.Clientbound,
            "minecraft:server_links",
            new ClientboundServerLinksPacket([new ServerLinkEntry(null, Probe, "https://example.invalid/rules")]));

    /// <summary>The scoreboard pair needs a binding-side pin. An objective display name is a component and carries the era on the wire exactly like every other family in this file. The objective packet starts at 765 because the display name is a JSON string before that (a different codec entirely, pinned by <c>ScoreboardCodecTests</c>), and set_score starts at 765 because that is where the record form replaces the action form (pinned by <c>ScoreUpdateCodecTests</c>).</summary>
    /// <param name="protocol">The protocol.</param>
    [Theory]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void SetObjective_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(
            protocol,
            PacketFlow.Clientbound,
            "minecraft:set_objective",
            new ClientboundSetObjectivePacket(
                "obj", ScoreboardObjectiveMode.Add, Probe,
                Umpk.Game.Scoreboard.ObjectiveRenderType.Integer, null));

    /// <summary>The other half of the same refutation; see <see cref="SetObjective_UsesEraOfProtocol"/>.</summary>
    /// <param name="protocol">The protocol.</param>
    [Theory]
    [InlineData(P1_20_3)]
    [InlineData(P1_21)]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    [InlineData(P26_2)]
    public void SetScore_UsesWireLayoutOfProtocol(int protocol) =>
        AssertEra(
            protocol,
            PacketFlow.Clientbound,
            "minecraft:set_score",
            new ClientboundSetScorePacket("owner", "obj", 7, Probe, null));

    // Sensitivity: the two era forms are NOT interchangeable.

    /// <summary>The 769 frame and the 770 frame for the same packet must differ, and each must contain exactly the field names the vanilla client of that version looks for.</summary>
    /// <remarks>The damage from this misbinding is ASYMMETRIC, and stating that precisely is the point of this test. UMPK's component reader is deliberately era-lenient: <c>ComponentNbt.ReadStyle</c> accepts <c>click_event</c> OR <c>clickEvent</c>, and <c>ReadClickEvent</c> accepts the dispatched value key OR <c>value</c>. So INBOUND frames from a 1.21.4 server were still understood even while the binding was two versions out. What was broken is every direction where UMPK is the WRITER - client-to-server, the server role, and any proxy that decodes and re-encodes: UMPK emitted <c>click_event</c> / <c>hover_event</c> to a peer whose Style codec is <c>clickEvent</c> / <c>hoverEvent</c>, so the peer found no such field and dropped the interaction silently. The assertions below are therefore about what is ON THE WIRE, not about what our own reader recovers.</remarks>
    [Fact]
    public void Legacy769AndModern770FramesAreNotInterchangeable()
    {
        var packet = new ClientboundSetTitleTextPacket(Probe);
        byte[] legacy = BoundCodec.At(P1_21_4, PacketFlow.Clientbound, "minecraft:set_title_text").Encode(packet);
        byte[] modern = BoundCodec.At(P1_21_5, PacketFlow.Clientbound, "minecraft:set_title_text").Encode(packet);

        Assert.NotEqual(legacy, modern);

        // What a vanilla 1.21.4 Style codec looks up, present only on the 769 frame.
        Assert.True(Contains(legacy, "clickEvent"));
        Assert.True(Contains(legacy, "hoverEvent"));
        Assert.False(Contains(modern, "clickEvent"));
        Assert.False(Contains(modern, "hoverEvent"));

        // What a vanilla 1.21.5 Style codec looks up, present only on the 770 frame.
        Assert.True(Contains(modern, "click_event"));
        Assert.True(Contains(modern, "hover_event"));
        Assert.False(Contains(legacy, "click_event"));
        Assert.False(Contains(legacy, "hover_event"));

        // And the click payload key moves with the era: flat "value" vs the dispatched "url".
        Assert.True(Contains(legacy, "value"));
        Assert.True(Contains(modern, "url"));
    }

    /// <summary>The era is a property of the protocol, not of the packet: at any one protocol every component-carrying family in this suite must agree. A per-family drift would mean the timeline was patched packet by packet rather than at the real boundary.</summary>
    [Theory]
    [InlineData(P1_21_2)]
    [InlineData(P1_21_4)]
    [InlineData(P1_21_5)]
    public void EveryFamilyAgreesOnTheWireLayoutAtAGivenProtocol(int protocol)
    {
        bool modern = protocol >= 770;
        (string Id, object Packet)[] families =
        [
            ("minecraft:set_title_text", new ClientboundSetTitleTextPacket(Probe)),
            ("minecraft:set_subtitle_text", new ClientboundSetSubtitleTextPacket(Probe)),
            ("minecraft:set_action_bar_text", new ClientboundSetActionBarTextPacket(Probe)),
            ("minecraft:tab_list", new ClientboundTabListPacket(Probe, Probe)),
            ("minecraft:server_data", new ClientboundServerDataPacket(Probe, null)),
            ("minecraft:system_chat", new ClientboundSystemChatPacket(Probe, Overlay: false)),
            ("minecraft:disguised_chat", new ClientboundDisguisedChatPacket(Probe, 3, Probe, Probe)),
        ];

        foreach ((string id, object packet) in families)
        {
            byte[] frame = BoundCodec.At(protocol, PacketFlow.Clientbound, id).Encode(packet);
            Assert.Equal(modern, Contains(frame, "click_event"));
            Assert.Equal(!modern, Contains(frame, "clickEvent"));
        }
    }

    /// <summary>Plain text is era-invariant: the 769 and 770 frames are byte-identical when nothing carries an interaction. This documents the blind spot rather than merely asserting it - any test whose payload is plain text cannot detect an era misbinding at all.</summary>
    [Fact]
    public void PlainTextIsIdenticalUnderBothWireLayouts()
    {
        var packet = new ClientboundSetTitleTextPacket(Component.Text("plain"));
        Assert.Equal(
            BoundCodec.At(P1_21_4, PacketFlow.Clientbound, "minecraft:set_title_text").Encode(packet),
            BoundCodec.At(P1_21_5, PacketFlow.Clientbound, "minecraft:set_title_text").Encode(packet));
    }
}
