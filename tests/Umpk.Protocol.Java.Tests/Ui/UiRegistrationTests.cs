using Umpk;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Registration-wiring tests: driving <see cref="PacketRegistrar.Register"/> the way the generated descriptor code does resolves each UI-family packet to an implemented codec (not a marker) for the era it belongs to, and the 776 team codec differs from the 770 one.</summary>
public class UiRegistrationTests
{
    private static BoundPacketCodec Bind(ProtocolPhase phase, PacketFlow flow, int wireId, string identifier, string codecKey)
    {
        // Resolution is by protocol number, so build at a protocol that carries this era key.
        var version = new GameVersion(GameEdition.Java, "test", CodecKeyProtocols.Of(codecKey));
        var builder = new ProtocolDescriptorBuilder(version, new ProtocolFeatures());
        PacketRegistrar.Register(builder, phase, flow, wireId, identifier);
        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.GetRegistry(phase, flow).TryGetInbound(wireId, out BoundPacketCodec entry));
        return entry;
    }

    [Theory]
    // Modern clientbound (V1_21_5 / V26_1).
    [InlineData(PacketFlow.Clientbound, "minecraft:set_objective", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_score", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:reset_score", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_display_objective", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_player_team", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info_update", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info_remove", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:tab_list", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_title_text", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_subtitle_text", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_action_bar_text", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_titles_animation", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:clear_titles", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:boss_event", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:system_chat", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:disguised_chat", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:delete_chat", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:custom_chat_completions", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:custom_report_details", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:server_data", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_abilities", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:resource_pack_push", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:resource_pack_pop", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:update_advancements", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:update_advancements", "V26_2")]
    [InlineData(PacketFlow.Clientbound, "minecraft:select_advancements_tab", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:command_suggestions", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:open_sign_editor", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:open_book", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:cooldown", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:ping", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:pong_response", "V1_21_5")]
    [InlineData(PacketFlow.Clientbound, "minecraft:server_links", "V1_21_5")]
    // 26.x clientbound.
    [InlineData(PacketFlow.Clientbound, "minecraft:show_dialog", "V26_1")]
    [InlineData(PacketFlow.Clientbound, "minecraft:clear_dialog", "V26_1")]
    [InlineData(PacketFlow.Clientbound, "minecraft:waypoint", "V26_1")]
    // Modern serverbound.
    [InlineData(PacketFlow.Serverbound, "minecraft:command_suggestion", "V1_21_5")]
    [InlineData(PacketFlow.Serverbound, "minecraft:player_abilities", "V1_21_5")]
    [InlineData(PacketFlow.Serverbound, "minecraft:resource_pack", "V1_21_5")]
    [InlineData(PacketFlow.Serverbound, "minecraft:seen_advancements", "V1_21_5")]
    [InlineData(PacketFlow.Serverbound, "minecraft:ping_request", "V1_21_5")]
    [InlineData(PacketFlow.Serverbound, "minecraft:pong", "V1_21_5")]
    [InlineData(PacketFlow.Serverbound, "minecraft:custom_click_action", "V26_1")]
    [InlineData(PacketFlow.Serverbound, "minecraft:edit_book", "V1_21_5")]
    // Legacy 1.8.
    [InlineData(PacketFlow.Clientbound, "minecraft:scoreboard_objective", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_score", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:display_scoreboard", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:set_player_team", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_8")]
    // The legacy single player_info packet is the tab-list wire form for the whole 47-760 band, so it must resolve an implemented codec on every era key in that band, not only on 1.8. It was codec-bound on V1_8 alone and markered from V1_9 onward, which is why the client tab list was empty on every version from 1.9 through 1.19.2; 1.19.3 replaced the packet with the split player-info pair.
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_9")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_9_4")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_12_2")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_13")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_14")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_16")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_17")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_18")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_19")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_info", "V1_19_1")]
    [InlineData(PacketFlow.Clientbound, "minecraft:player_list_header_footer", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:title", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:abilities", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:tab_complete", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:resource_pack", "V1_8")]
    [InlineData(PacketFlow.Clientbound, "minecraft:open_sign_editor", "V1_8")]
    [InlineData(PacketFlow.Serverbound, "minecraft:abilities", "V1_8")]
    [InlineData(PacketFlow.Serverbound, "minecraft:tab_complete", "V1_8")]
    [InlineData(PacketFlow.Serverbound, "minecraft:resource_pack_response", "V1_8")]
    public void UiPacket_ResolvesImplementedCodec(PacketFlow flow, string identifier, string codecKey)
    {
        BoundPacketCodec entry = Bind(ProtocolPhase.Play, flow, 0, identifier, codecKey);
        Assert.True(entry.IsImplemented, $"{identifier} ({codecKey}) should resolve an implemented codec, not a marker.");
    }

    [Fact]
    public void SetPlayerTeam_776_UsesDistinctCodecFrom_770()
    {
        BoundPacketCodec v770 = Bind(ProtocolPhase.Play, PacketFlow.Clientbound, 0, "minecraft:set_player_team", "V1_21_5");
        BoundPacketCodec v776 = Bind(ProtocolPhase.Play, PacketFlow.Clientbound, 0, "minecraft:set_player_team", "V26_2");
        Assert.True(v770.IsImplemented);
        Assert.True(v776.IsImplemented);
    }

    [Fact]
    public void UpdateAdvancements_776_UsesDistinctCodecFrom_770()
    {
        // Implemented on every component era from 1.21.5 up. 775 was the last hold-out: its DisplayInfo icon is an ItemStackTemplate (holder id first, VarInt count, no empty sentinel) and UMPK only modeled the count-first stack, so it was left a verbatim marker rather than mis-framed. ItemStackCodecs now models the template, so 775 binds too; ItemStackTemplateBindingTests pins the framing, and this pin only records that all three eras resolve a codec.
        BoundPacketCodec v770 = Bind(ProtocolPhase.Play, PacketFlow.Clientbound, 0, "minecraft:update_advancements", "V1_21_5");
        BoundPacketCodec v776 = Bind(ProtocolPhase.Play, PacketFlow.Clientbound, 0, "minecraft:update_advancements", "V26_2");
        BoundPacketCodec v775 = Bind(ProtocolPhase.Play, PacketFlow.Clientbound, 0, "minecraft:update_advancements", "V26_1");
        Assert.True(v770.IsImplemented);
        Assert.True(v776.IsImplemented);
        Assert.True(v775.IsImplemented);
    }
}
