using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Seeded round-trip tests for chat auxiliaries, abilities, resource pack, command suggestions, ping/pong, cooldown, sign editor, open book, server links, advancements tab, and the 26.x dialog/waypoint codecs.</summary>
public class MiscUiCodecTests
{
    [Fact]
    public void SystemChat_RoundTrips()
    {
        var packet = new ClientboundSystemChatPacket(Component.Text("hello"), true);
        var decoded = CodecRoundTrip.Cycle(ChatDisplayCodecs.SystemChatV1_21_5, packet);
        Assert.Equal("hello", decoded.Content.ToPlainText());
        Assert.True(decoded.Overlay);
    }

    [Fact]
    public void DisguisedChat_RoundTrips_WithAndWithoutTarget()
    {
        var withTarget = new ClientboundDisguisedChatPacket(Component.Text("m"), 1, Component.Text("Sender"), Component.Text("Target"));
        var d1 = CodecRoundTrip.Cycle(ChatDisplayCodecs.DisguisedChatV1_21_5, withTarget);
        Assert.Equal(1, d1.ChatTypeId);
        Assert.Equal("Target", d1.TargetName!.ToPlainText());

        var noTarget = new ClientboundDisguisedChatPacket(Component.Text("m"), 2, Component.Text("Sender"), null);
        Assert.Null(CodecRoundTrip.Cycle(ChatDisplayCodecs.DisguisedChatV1_21_5, noTarget).TargetName);
    }

    [Fact]
    public void DeleteChat_RoundTrips_CachedIdAndFullSignature()
    {
        var cached = new ClientboundDeleteChatPacket(7, null);
        var dc = CodecRoundTrip.Cycle(ChatDisplayCodecs.DeleteChatV1_19_1, cached);
        Assert.Equal(7, dc.Id);
        Assert.Null(dc.FullSignature);

        var sig = new byte[256];
        new Random(3).NextBytes(sig);
        var full = new ClientboundDeleteChatPacket(-1, sig);
        var df = CodecRoundTrip.Cycle(ChatDisplayCodecs.DeleteChatV1_19_1, full);
        Assert.Equal(-1, df.Id);
        Assert.Equal(sig, df.FullSignature);
    }

    [Theory]
    [InlineData(ChatCompletionsAction.Add)]
    [InlineData(ChatCompletionsAction.Remove)]
    [InlineData(ChatCompletionsAction.Set)]
    public void CustomChatCompletions_RoundTrips(ChatCompletionsAction action)
    {
        var packet = new ClientboundCustomChatCompletionsPacket(action, ["a", "bb", "ccc"]);
        var decoded = CodecRoundTrip.Cycle(ChatDisplayCodecs.CustomChatCompletionsV1_19_1, packet);
        Assert.Equal(action, decoded.Action);
        Assert.Equal(3, decoded.Entries.Count);
    }

    [Fact]
    public void ServerData_RoundTrips_WithAndWithoutIcon()
    {
        var withIcon = new ClientboundServerDataPacket(Component.Text("MOTD"), [1, 2, 3, 4]);
        var di = CodecRoundTrip.Cycle(ChatDisplayCodecs.ServerDataV1_21_5, withIcon);
        Assert.Equal("MOTD", di.Motd.ToPlainText());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, di.IconBytes);

        var noIcon = new ClientboundServerDataPacket(Component.Text("MOTD"), null);
        Assert.Null(CodecRoundTrip.Cycle(ChatDisplayCodecs.ServerDataV1_21_5, noIcon).IconBytes);
    }

    [Fact]
    public void CustomReportDetails_RoundTrips_EmptyAndPopulated()
    {
        var empty = new ClientboundCustomReportDetailsPacket([]);
        Assert.Empty(CodecRoundTrip.Cycle(ChatDisplayCodecs.CustomReportDetailsV1_21, empty).Details);

        var populated = new ClientboundCustomReportDetailsPacket(
        [
            new KeyValuePair<string, string>("world", "overworld"),
            new KeyValuePair<string, string>("tick", "12345"),
        ]);
        var decoded = CodecRoundTrip.Cycle(ChatDisplayCodecs.CustomReportDetailsV1_21, populated);
        Assert.Equal(2, decoded.Details.Count);
        Assert.Equal("world", decoded.Details[0].Key);
        Assert.Equal("overworld", decoded.Details[0].Value);
        Assert.Equal("tick", decoded.Details[1].Key);
        Assert.Equal("12345", decoded.Details[1].Value);
    }

    [Fact]
    public void PlayerAbilities_RoundTrip_AllWireLayouts()
    {
        var cb = new ClientboundPlayerAbilitiesPacket(0x05, 0.05f, 0.1f);
        Assert.Equal(cb, CodecRoundTrip.Cycle(UiMiscCodecs.PlayerAbilitiesV1_8, cb));

        var sbModern = new ServerboundPlayerAbilitiesPacket(0x02);
        Assert.Equal(sbModern, CodecRoundTrip.Cycle(UiMiscCodecs.ServerPlayerAbilitiesV1_14, sbModern));

        var sbLegacy = new ServerboundLegacyPlayerAbilitiesPacket(0x0F, 0.05f, 0.1f);
        Assert.Equal(sbLegacy, CodecRoundTrip.Cycle(UiMiscCodecs.ServerLegacyAbilitiesV1_8, sbLegacy));
    }

    [Fact]
    public void ResourcePack_RoundTrips()
    {
        var push = new ClientboundResourcePackPushPacket(Guid.NewGuid(), "http://x", "abcdef", true, Component.Text("prompt"));
        var dp = CodecRoundTrip.Cycle(ResourcePackCodecs.ResourcePackPushV1_21_5, push);
        Assert.Equal("http://x", dp.Url);
        Assert.True(dp.Required);
        Assert.NotNull(dp.Prompt);

        var pushNoPrompt = push with { Prompt = null };
        Assert.Null(CodecRoundTrip.Cycle(ResourcePackCodecs.ResourcePackPushV1_21_5, pushNoPrompt).Prompt);

        var pop = new ClientboundResourcePackPopPacket(Guid.NewGuid());
        Assert.NotNull(CodecRoundTrip.Cycle(ResourcePackCodecs.ResourcePackPopV1_20_3, pop).Id);
        Assert.Null(CodecRoundTrip.Cycle(ResourcePackCodecs.ResourcePackPopV1_20_3, new ClientboundResourcePackPopPacket(null)).Id);

        var legacy = new ClientboundLegacyResourcePackPacket("http://y", "hash");
        Assert.Equal(legacy, CodecRoundTrip.Cycle(ResourcePackCodecs.LegacyResourcePackV1_8, legacy));
    }

    [Theory]
    [InlineData(ResourcePackAction.SuccessfullyLoaded)]
    [InlineData(ResourcePackAction.Declined)]
    [InlineData(ResourcePackAction.Discarded)]
    public void ResourcePackResponse_RoundTrips(ResourcePackAction action)
    {
        var modern = new ServerboundResourcePackPacket(Guid.NewGuid(), action);
        Assert.Equal(action, CodecRoundTrip.Cycle(ResourcePackCodecs.ServerResourcePackV1_20_3, modern).Action);

        var legacy = new ServerboundLegacyResourcePackPacket("hash", ResourcePackAction.Accepted);
        Assert.Equal(legacy, CodecRoundTrip.Cycle(ResourcePackCodecs.ServerLegacyResourcePackV1_8, legacy));
    }

    [Fact]
    public void SelectAdvancementsTab_And_SeenAdvancements_RoundTrip()
    {
        Assert.Equal(Identifier.Minecraft("story/root"),
            CodecRoundTrip.Cycle(AdvancementCodecs.SelectAdvancementsTabV1_12, new ClientboundSelectAdvancementsTabPacket(Identifier.Minecraft("story/root"))).Tab);
        Assert.Null(CodecRoundTrip.Cycle(AdvancementCodecs.SelectAdvancementsTabV1_12, new ClientboundSelectAdvancementsTabPacket(null)).Tab);

        var opened = new ServerboundSeenAdvancementsPacket(SeenAdvancementsAction.OpenedTab, Identifier.Minecraft("nether/root"));
        Assert.Equal(Identifier.Minecraft("nether/root"), CodecRoundTrip.Cycle(AdvancementCodecs.SeenAdvancementsV1_12, opened).Tab);
        var closed = new ServerboundSeenAdvancementsPacket(SeenAdvancementsAction.ClosedScreen, null);
        Assert.Null(CodecRoundTrip.Cycle(AdvancementCodecs.SeenAdvancementsV1_12, closed).Tab);
    }

    [Fact]
    public void CommandSuggestions_RoundTrips_WithAndWithoutTooltip()
    {
        var packet = new ClientboundCommandSuggestionsPacket(9, 2, 5,
        [
            new CommandSuggestion("give", Component.Text("Give an item")),
            new CommandSuggestion("gamemode", null),
        ]);
        var decoded = CodecRoundTrip.Cycle(CommandSuggestionCodecs.CommandSuggestionsV1_20_3, packet);
        Assert.Equal(9, decoded.TransactionId);
        Assert.Equal(2, decoded.Suggestions.Count);
        Assert.NotNull(decoded.Suggestions[0].Tooltip);
        Assert.Null(decoded.Suggestions[1].Tooltip);
    }

    [Fact]
    public void ServerCommandSuggestion_RoundTrips()
    {
        var packet = new ServerboundCommandSuggestionPacket(4, "/gi");
        Assert.Equal(packet, CodecRoundTrip.Cycle(CommandSuggestionCodecs.ServerCommandSuggestionV1_13, packet));
    }

    [Fact]
    public void EditBook_RoundTrips_WithAndWithoutTitle_AndNoPages()
    {
        var signed = new ServerboundEditBookPacket(3, ["page one", "page two"], "My Book");
        var ds = CodecRoundTrip.Cycle(UiMiscCodecs.EditBookV1_17, signed);
        Assert.Equal(3, ds.Slot);
        Assert.Equal(2, ds.Pages.Count);
        Assert.Equal("page two", ds.Pages[1]);
        Assert.Equal("My Book", ds.Title);

        var draft = new ServerboundEditBookPacket(0, [], null);
        var dd = CodecRoundTrip.Cycle(UiMiscCodecs.EditBookV1_17, draft);
        Assert.Empty(dd.Pages);
        Assert.Null(dd.Title);
    }

    [Fact]
    public void LegacyTabComplete_RoundTrips_BothDirections()
    {
        var cb = new ClientboundLegacyTabCompletePacket(["give", "gamemode"]);
        Assert.Equal(2, CodecRoundTrip.Cycle(CommandSuggestionCodecs.LegacyTabCompleteV1_8, cb).Matches.Count);

        var sbNoPos = new ServerboundLegacyTabCompletePacket("/gi", null, AssumeCommand: null);
        Assert.Null(CodecRoundTrip.Cycle(CommandSuggestionCodecs.ServerLegacyTabCompleteV1_8, sbNoPos).LookedAtBlock);
        var sbPos = new ServerboundLegacyTabCompletePacket("/gi", new BlockPos(1, 2, 3), AssumeCommand: null);
        Assert.Equal(new BlockPos(1, 2, 3), CodecRoundTrip.Cycle(CommandSuggestionCodecs.ServerLegacyTabCompleteV1_8, sbPos).LookedAtBlock);
    }

    [Fact]
    public void OpenSignEditor_RoundTrips_BothWireLayouts()
    {
        var modern = new ClientboundOpenSignEditorPacket(new BlockPos(10, 64, -20), false);
        var dm = CodecRoundTrip.Cycle(UiMiscCodecs.OpenSignEditorV1_14, modern);
        Assert.Equal(new BlockPos(10, 64, -20), dm.Pos);
        Assert.False(dm.IsFrontText);

        var legacy = new ClientboundOpenSignEditorPacket(new BlockPos(5, 70, 5), true);
        Assert.Equal(new BlockPos(5, 70, 5), CodecRoundTrip.Cycle(UiMiscCodecs.OpenSignEditorV1_8, legacy).Pos);
    }

    [Fact]
    public void OpenBook_Cooldown_Ping_Pong_RoundTrip()
    {
        Assert.Equal(1, CodecRoundTrip.Cycle(UiMiscCodecs.OpenBookV1_14, new ClientboundOpenBookPacket(1)).Hand);

        var cd = new ClientboundCooldownPacket(Identifier.Minecraft("ender_pearl"), 100, ItemId: null);
        Assert.Equal(cd, CodecRoundTrip.Cycle(UiMiscCodecs.CooldownV1_21_2, cd));

        Assert.Equal(1234, CodecRoundTrip.Cycle(UiMiscCodecs.PingV1_17, new ClientboundPlayPingPacket(1234)).Id);
        Assert.Equal(99L, CodecRoundTrip.Cycle(UiMiscCodecs.PongResponseV1_20_2, new ClientboundPlayPongResponsePacket(99L)).Time);
        Assert.Equal(77, CodecRoundTrip.Cycle(UiMiscCodecs.ServerPongV1_17, new ServerboundPlayPongPacket(77)).Id);
        Assert.Equal(55L, CodecRoundTrip.Cycle(UiMiscCodecs.ServerPingRequestV1_20_2, new ServerboundPlayPingRequestPacket(55L)).Time);
    }

    [Fact]
    public void ServerLinks_RoundTrips_KnownAndCustom()
    {
        var packet = new ClientboundServerLinksPacket(
        [
            new ServerLinkEntry(0, null, "http://known"),
            new ServerLinkEntry(null, Component.Text("Custom"), "http://custom"),
        ]);
        var decoded = CodecRoundTrip.Cycle(UiMiscCodecs.ServerLinksV1_21_5, packet);
        Assert.Equal(2, decoded.Links.Count);
        Assert.Equal(0, decoded.Links[0].KnownTypeId);
        Assert.Null(decoded.Links[0].Label);
        Assert.Null(decoded.Links[1].KnownTypeId);
        Assert.Equal("Custom", decoded.Links[1].Label!.ToPlainText());
    }

    [Fact]
    public void ShowDialog_RoundTrips_RegistryAndInline()
    {
        var registry = new ClientboundShowDialogPacket(5, null);
        var dr = CodecRoundTrip.Cycle(UiMiscCodecs.ShowDialogV1_21_6, registry);
        Assert.Equal(5, dr.RegistryId);
        Assert.Null(dr.InlineDialog);

        var body = new NbtCompound();
        body.PutString("type", "minecraft:notice");
        var inline = new ClientboundShowDialogPacket(null, body);
        var di = CodecRoundTrip.Cycle(UiMiscCodecs.ShowDialogV1_21_6, inline);
        Assert.Null(di.RegistryId);
        Assert.NotNull(di.InlineDialog);
    }

    [Fact]
    public void ClearDialog_RoundTrips()
    {
        var packet = new ClientboundClearDialogPacket();
        var decoded = CodecRoundTrip.Cycle(UiMiscCodecs.ClearDialogV1_21_6, packet);
        Assert.NotNull(decoded);
    }

    [Theory]
    [InlineData(WaypointKind.Empty)]
    [InlineData(WaypointKind.Vec3i)]
    [InlineData(WaypointKind.Chunk)]
    [InlineData(WaypointKind.Azimuth)]
    public void Waypoint_RoundTrips_AllKinds(WaypointKind kind)
    {
        var packet = new ClientboundWaypointPacket(
            WaypointOperation.Track, IdentifierIsUuid: true, Guid.NewGuid(), null,
            Identifier.Minecraft("default"), IconColor: 0x336699,
            kind, X: 10, Y: 64, Z: -3, Azimuth: 1.57f);
        var decoded = CodecRoundTrip.Cycle(UiMiscCodecs.WaypointV1_21_6, packet);
        Assert.Equal(kind, decoded.Kind);
        Assert.Equal(0x336699, decoded.IconColor);
        switch (kind)
        {
            case WaypointKind.Vec3i:
                Assert.Equal((10, 64, -3), (decoded.X, decoded.Y, decoded.Z));
                break;
            case WaypointKind.Chunk:
                Assert.Equal((10, -3), (decoded.X, decoded.Z));
                break;
            case WaypointKind.Azimuth:
                Assert.Equal(1.57f, decoded.Azimuth);
                break;
            default:
                break;
        }
    }

    [Fact]
    public void Waypoint_StringIdentifier_And_NoColor_RoundTrip()
    {
        var packet = new ClientboundWaypointPacket(
            WaypointOperation.Untrack, IdentifierIsUuid: false, Guid.Empty, "beacon-1",
            Identifier.Minecraft("bowtie"), IconColor: null,
            WaypointKind.Empty, 0, 0, 0, 0f);
        var decoded = CodecRoundTrip.Cycle(UiMiscCodecs.WaypointV1_21_6, packet);
        Assert.False(decoded.IdentifierIsUuid);
        Assert.Equal("beacon-1", decoded.IdentifierString);
        Assert.Null(decoded.IconColor);
    }

    [Fact]
    public void CustomClickAction_RoundTrips_WithAndWithoutPayload()
    {
        var payload = new NbtCompound();
        payload.PutInt("value", 42);
        var withPayload = new ServerboundCustomClickActionPacket(Identifier.Minecraft("my_action"), payload);
        Assert.NotNull(CodecRoundTrip.Cycle(UiMiscCodecs.CustomClickActionV1_21_6, withPayload).Payload);

        var noPayload = new ServerboundCustomClickActionPacket(Identifier.Minecraft("my_action"), null);
        Assert.Null(CodecRoundTrip.Cycle(UiMiscCodecs.CustomClickActionV1_21_6, noPayload).Payload);
    }
}
