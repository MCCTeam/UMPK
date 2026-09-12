using System.Text;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Scoreboard;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>
/// Two consequences of the per-protocol component tables landing.
/// <list type="number">
/// <item><c>update_advancements</c> on 768, 769, 774, and 775 must decode its component item-stack icon
/// with the protocol's component-id table and item-stack template.</item>
/// <item>The score number format's FIXED arm carries a component, and the component interaction dialect
/// moves at 1.21.5, so 765-769 must write <c>clickEvent</c> where 770+ write <c>click_event</c>.</item>
/// </list>
/// </summary>
public class AdvancementAndNumberFormatCodecTests
{
    private const string UpdateAdvancements = "minecraft:update_advancements";
    private const string SetObjective = "minecraft:set_objective";
    private const string SetScore = "minecraft:set_score";

    // update_advancements: which protocols carry a codec, and which icon id space each uses.

    /// <summary>The three protocols left as markers purely because the icon needed their own component-id ordering. Each now resolves a real codec through the registrar.</summary>
    [Theory]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(771)]
    [InlineData(773)]
    [InlineData(774)]
    public void UpdateAdvancements_IsBoundOnTheOnceMarkedProtocols(int protocol) =>
        Assert.True(BoundCodec.IsImplementedAt(protocol, ProtocolPhase.Play, PacketFlow.Clientbound, UpdateAdvancements));

    /// <summary>Protocol 776 changes the icon to an item-holder-first template with no empty sentinel, replacing the count-first stack. Both bindings now use the template; <c>ItemStackTemplateBindingTests</c> pins its framing.</summary>
    /// <param name="protocol">The 26.x protocol.</param>
    [Theory]
    [InlineData(775)]
    [InlineData(776)]
    public void UpdateAdvancements_IsBoundOn26xNowTheTemplateStackIsModeled(int protocol) =>
        Assert.True(BoundCodec.IsImplementedAt(protocol, ProtocolPhase.Play, PacketFlow.Clientbound, UpdateAdvancements));

    /// <summary>The icon really does travel through the protocol's own component table: the wire id byte for <c>minecraft:map_id</c> inside a DisplayInfo icon is 36 on 768/769, 37 on 770-773 and 44 on 774. A round trip cannot see this (encode and decode share the table), so the assertion is on the byte.</summary>
    [Theory]
    [InlineData(768, 36)]
    [InlineData(769, 36)]
    [InlineData(770, 37)]
    [InlineData(773, 37)]
    [InlineData(774, 44)]
    public void UpdateAdvancements_IconUsesTheProtocolsComponentIds(int protocol, int expectedWireId)
    {
        byte[] frame = BoundCodec.At(protocol, PacketFlow.Clientbound, UpdateAdvancements).Encode(MapIconPacket);

        Assert.Contains((byte)expectedWireId, IconComponentIds(frame, expectedWireId));
    }

    /// <summary>768/769 end at the progress map; 770+ append <c>showAdvancements</c>. This length assertion distinguishes frames that otherwise decode under each other's codec.</summary>
    [Fact]
    public void UpdateAdvancements_ShowAdvancementsBoolStartsAt770()
    {
        int len768 = BoundCodec.At(768, PacketFlow.Clientbound, UpdateAdvancements).Encode(MapIconPacket).Length;
        int len769 = BoundCodec.At(769, PacketFlow.Clientbound, UpdateAdvancements).Encode(MapIconPacket).Length;
        int len770 = BoundCodec.At(770, PacketFlow.Clientbound, UpdateAdvancements).Encode(MapIconPacket).Length;

        Assert.Equal(len768, len769);
        Assert.Equal(len768 + 1, len770);
    }

    // The probe: one advancement whose DisplayInfo icon is a filled map carrying minecraft:map_id. That component's wire id is the era divergence, and a filled map is precisely the kind of icon a live server sends.
    private static ClientboundUpdateAdvancementsPacket MapIconPacket { get; } = new(
        Reset: false,
        Added:
        [
            new AdvancementEntry(
                Identifier.Minecraft("story/root"),
                new AdvancementNode(
                    Parent: null,
                    Display: new AdvancementDisplayInfo(
                        Component.Text("t"),
                        Component.Text("d"),
                        new ItemStack(
                            ItemTestRegistries.Item(ItemTestRegistries.FilledMap),
                            1,
                            DataComponentMap.Empty.With(DataComponents.MapId, new MapIdComponent(7))),
                        AdvancementFrameType.Task,
                        Background: null,
                        ShowToast: true,
                        Hidden: false,
                        X: 0f,
                        Y: 0f),
                    Criteria: [],
                    Requirements: [],
                    SendsTelemetryEvent: false)),
        ],
        Removed: [],
        Progress: [],
        ShowAdvancements: true);

    // The icon's component patch is the only place a bare component wire id appears in this frame, and the id is immediately followed by the map id payload byte 7. Collect every position that matches that pair so the assertion is about the icon and not an incidental byte.
    private static IReadOnlyList<byte> IconComponentIds(byte[] frame, int expected)
    {
        var found = new List<byte>();
        for (int i = 0; i + 1 < frame.Length; i++)
            if (frame[i] == expected && frame[i + 1] == 7)
                found.Add(frame[i]);

        return found;
    }

    // A decorated-pot icon must decode rather than costing the WHOLE packet.

    /// <summary>Live-observed on 766: <c>advancement grant &lt;player&gt; everything</c> sends <c>update_advancements</c> with an advancement whose DisplayInfo icon carries <c>minecraft:pot_decorations</c>. That component was listed-but-untyped on 766/767/768/769 (the <c>ItemComponentTable.BuildPreV1_21_5</c> family - <c>PotDecorationsComponentTests</c> has the isolated component-codec coverage), so decoding the icon raised <see cref="UnmodeledItemComponentException"/> mid-packet and the packet was dropped (<c>UnmodeledComponentSessionSurvivalTests</c> proves the CONNECTION nonetheless survives - this test is about the packet's own data surviving, which the connection recovery does not restore). This is the exact reported packet/path, not just the component in isolation.</summary>
    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public void UpdateAdvancements_DecoratedPotIcon_DecodesRatherThanCostingThePacket(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, UpdateAdvancements);
        byte[] frame = bound.Encode(PotIconPacket);

        var decoded = Assert.IsType<ClientboundUpdateAdvancementsPacket>(bound.DecodeFrame(frame));

        AdvancementDisplayInfo display = Assert.Single(decoded.Added).Value.Display!;
        Assert.True(display.Icon.Components.TryGet(DataComponents.PotDecorations, out PotDecorationsComponent? pot));
        Assert.Equal([11, 12, 13, 14], pot!.SherdItemIds);

        // Frame-exact both ways, same as every other packet-survives assertion in this file.
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The same <c>advancement grant &lt;player&gt; everything</c> run that exercises pot decorations also reaches <c>minecraft:banner_patterns</c> (wire id 48 on 766).</summary>
    /// <remarks>
    /// The affected advancements are <c>adventure/voluntary_exile</c> and <c>adventure/hero_of_the_village</c>. Their display icon unconditionally sets <c>banner_patterns</c> with EIGHT layers, plus <c>hide_additional_tooltip</c> and <c>ITEM_NAME</c>. So the ominous banner icon always carries a populated <c>banner_patterns</c> patch, exactly like the decorated pot always carries <c>pot_decorations</c>.
    /// <para>This models the whole icon, not just the one component, because the other two components on that stack are what a partially-typed table would trip over next.</para>
    /// </remarks>
    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(774)]
    [InlineData(776)]
    public void UpdateAdvancements_OminousBannerIcon_DecodesRatherThanCostingThePacket(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, UpdateAdvancements);
        byte[] frame = bound.Encode(OminousBannerIconPacket(protocol));

        var decoded = Assert.IsType<ClientboundUpdateAdvancementsPacket>(bound.DecodeFrame(frame));

        AdvancementDisplayInfo display = Assert.Single(decoded.Added).Value.Display!;
        Assert.True(display.Icon.Components.TryGet(DataComponents.BannerPatterns, out BannerPatternsComponent? banner));
        Assert.Equal(8, banner!.Layers.Count);
        Assert.Equal("cyan", banner.Layers[0].Color);
        Assert.Equal("black", banner.Layers[7].Color);
        Assert.True(display.Icon.Components.TryGet(DataComponents.ItemName, out ItemNameComponent? _));

        // Frame-exact both ways, same as every other packet-survives assertion in this file.
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The ominous banner icon as vanilla builds it: eight pattern layers with their dye colors, the hide-additional-tooltip marker, and the translated item name. <c>hide_additional_tooltip</c> only exists on 766/767, so it is added only there - the era table would otherwise have no wire id for it and encoding would fault for a reason unrelated to what this test is about.</summary>
    private static ClientboundUpdateAdvancementsPacket OminousBannerIconPacket(int protocol)
    {
        // The ominous banner's eight layers, in wire order, with their canonical dye colors. The pattern holder ids are synthetic here because this layer cannot resolve the banner-pattern registry; the COLORS are vanilla's.
        BannerPatternLayer[] layers =
        [
            new(new Identifier("umpk", "banner_pattern_1"), "cyan"),          // rhombus_middle
            new(new Identifier("umpk", "banner_pattern_2"), "light_gray"),    // stripe_bottom
            new(new Identifier("umpk", "banner_pattern_3"), "gray"),          // stripe_center
            new(new Identifier("umpk", "banner_pattern_4"), "light_gray"),    // border
            new(new Identifier("umpk", "banner_pattern_5"), "black"),         // stripe_middle
            new(new Identifier("umpk", "banner_pattern_6"), "light_gray"),    // half_horizontal
            new(new Identifier("umpk", "banner_pattern_7"), "light_gray"),    // circle_middle
            new(new Identifier("umpk", "banner_pattern_4"), "black"),         // border again
        ];

        DataComponentMap components = DataComponentMap.Empty
            .With(DataComponents.BannerPatterns, new BannerPatternsComponent(layers))
            .With(DataComponents.ItemName, new ItemNameComponent(Component.Text("Ominous Banner")));

        if (protocol < 768)
            components = components.With(DataComponents.HideAdditionalTooltip, UnitMarkerComponent.Instance);

        return new ClientboundUpdateAdvancementsPacket(
            Reset: false,
            Added:
            [
                new AdvancementEntry(
                    Identifier.Minecraft("adventure/voluntary_exile"),
                    new AdvancementNode(
                        Parent: null,
                        Display: new AdvancementDisplayInfo(
                            Component.Text("Voluntary Exile"),
                            Component.Text("Kill a raid captain."),
                            new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 1, components),
                            AdvancementFrameType.Task,
                            Background: null,
                            ShowToast: true,
                            Hidden: false,
                            X: 0f,
                            Y: 0f),
                        Criteria: [],
                        Requirements: [],
                        SendsTelemetryEvent: false)),
            ],
            Removed: [],
            Progress: [],
            ShowAdvancements: true);
    }

    // The probe: one advancement whose DisplayInfo icon is a decorated pot carrying real sherds via minecraft:pot_decorations - the exact icon shape observed live on 766.
    private static ClientboundUpdateAdvancementsPacket PotIconPacket { get; } = new(
        Reset: false,
        Added:
        [
            new AdvancementEntry(
                Identifier.Minecraft("husbandry/decorated_pot"),
                new AdvancementNode(
                    Parent: null,
                    Display: new AdvancementDisplayInfo(
                        Component.Text("t"),
                        Component.Text("d"),
                        new ItemStack(
                            ItemTestRegistries.Item(ItemTestRegistries.Stone),
                            1,
                            DataComponentMap.Empty.With(
                                DataComponents.PotDecorations, new PotDecorationsComponent([11, 12, 13, 14]))),
                        AdvancementFrameType.Task,
                        Background: null,
                        ShowToast: true,
                        Hidden: false,
                        X: 0f,
                        Y: 0f),
                    Criteria: [],
                    Requirements: [],
                    SendsTelemetryEvent: false)),
        ],
        Removed: [],
        Progress: [],
        ShowAdvancements: true);

    // Score number format: the FIXED arm is a component, so it follows the interaction dialect.

    private static ScoreNumberFormat FixedWithClick { get; } = new(
        ScoreNumberFormatKind.Fixed,
        null,
        new Component(
            new TextContent("n"),
            new Style { ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid/u20") }));

    private static bool Contains(byte[] frame, string marker) =>
        frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker).AsSpan()) >= 0;

    /// <summary>set_objective carries both an NBT display name and the optional number format; both are components and both move dialect at 770. The whole 765-776 band shared one modern-dialect codec, so five protocols wrote <c>click_event</c> where vanilla writes <c>clickEvent</c>.</summary>
    [Theory]
    [InlineData(765, false)]
    [InlineData(767, false)]
    [InlineData(768, false)]
    [InlineData(769, false)]
    [InlineData(770, true)]
    [InlineData(774, true)]
    [InlineData(776, true)]
    public void SetObjective_NumberFormatUsesTheProtocolsDialect(int protocol, bool modern)
    {
        var packet = new ClientboundSetObjectivePacket(
            "obj", ScoreboardObjectiveMode.Add, Component.Text("o"), ObjectiveRenderType.Integer, FixedWithClick);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetObjective);
        byte[] frame = bound.Encode(packet);

        Assert.Equal(modern, Contains(frame, "click_event"));
        Assert.Equal(!modern, Contains(frame, "clickEvent"));

        // Frame-exact through the live entry point in both directions.
        Assert.Equal(frame, bound.Encode(bound.DecodeFrame(frame)));
    }

    /// <summary>set_score carries the same pair and splits at the same protocol.</summary>
    [Theory]
    [InlineData(765, false)]
    [InlineData(769, false)]
    [InlineData(770, true)]
    [InlineData(776, true)]
    public void SetScore_NumberFormatUsesTheProtocolsDialect(int protocol, bool modern)
    {
        var packet = new ClientboundSetScorePacket("owner", "obj", 3, Component.Text("s"), FixedWithClick);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetScore);
        byte[] frame = bound.Encode(packet);

        Assert.Equal(modern, Contains(frame, "click_event"));
        Assert.Equal(!modern, Contains(frame, "clickEvent"));
        Assert.Equal(frame, bound.Encode(bound.DecodeFrame(frame)));
    }

    /// <summary>Why only an ENCODED-BYTES assertion can pin this, spelled out as a test. The two eras produce DIFFERENT frames for the same packet, but UMPK's component reader accepts BOTH spellings, so decoding a modern frame with the legacy codec succeeds and yields the identical value. A decode-side or round-trip assertion is therefore structurally blind here, and the theories above assert the wire markers instead.</summary>
    [Fact]
    public void SetObjective_WireLayoutsDifferOnEncodeOnly()
    {
        var packet = new ClientboundSetObjectivePacket(
            "obj", ScoreboardObjectiveMode.Add, Component.Text("o"), ObjectiveRenderType.Integer, FixedWithClick);

        BoundPacketCodec legacy = BoundCodec.At(769, PacketFlow.Clientbound, SetObjective);
        BoundPacketCodec modern = BoundCodec.At(770, PacketFlow.Clientbound, SetObjective);

        byte[] legacyFrame = legacy.Encode(packet);
        byte[] modernFrame = modern.Encode(packet);
        Assert.NotEqual(legacyFrame, modernFrame);
        Assert.True(Contains(legacyFrame, "clickEvent"));
        Assert.True(Contains(modernFrame, "click_event"));

        // The reader is lenient in both directions: the cross-era decode succeeds and produces the same click event, so only the encoded frame distinguishes the eras.
        var viaOwn = (ClientboundSetObjectivePacket)modern.DecodeFrame(modernFrame);
        var viaCross = (ClientboundSetObjectivePacket)legacy.DecodeFrame(modernFrame);
        Assert.NotNull(viaOwn.NumberFormat!.FixedContent!.Style.ClickEvent);
        Assert.Equal(viaOwn.NumberFormat.FixedContent.Style.ClickEvent, viaCross.NumberFormat!.FixedContent!.Style.ClickEvent);
    }

    /// <summary>open_screen widened its container id to a VarInt at 1.21.2 but the title's dialect only modernizes at 1.21.5, so 768/769 need the modern header with the legacy dialect. Binding the 1.21.5 codec there wrote <c>click_event</c> for every interactive container title.</summary>
    [Theory]
    [InlineData(768, false)]
    [InlineData(769, false)]
    [InlineData(770, true)]
    [InlineData(776, true)]
    public void OpenScreen_TitleUsesTheProtocolsDialect(int protocol, bool modern)
    {
        var title = new Component(
            new TextContent("chest"),
            new Style { ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid/u20") });
        var packet = new ClientboundOpenScreenPacket(1, 2, title, null, 0, null);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:open_screen");
        byte[] frame = bound.Encode(packet);

        Assert.Equal(modern, Contains(frame, "click_event"));
        Assert.Equal(!modern, Contains(frame, "clickEvent"));
        Assert.Equal(frame, bound.Encode(bound.DecodeFrame(frame)));
    }
}
