using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>UI-family timelines: scoreboard, teams, player list, titles, boss bar, chat auxiliaries, abilities, resource packs, advancements, suggestions, and the dialog/waypoint packets. The 1.8 scoreboard, player-list, title, abilities, resource-pack and tab-complete packets carry legacy identities that modern versions replaced; text-carrying packets also swing between JSON-string and network-NBT component encodings across 1.20.2-1.20.3.</summary>
internal static class UiBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        AdvancementCodecs.DeclareSeenAdvancements(bindings);
        AdvancementCodecs.DeclareSelectAdvancementsTab(bindings);
        AdvancementCodecs.DeclareUpdateAdvancements(bindings);
        BossBarCodecs.DeclareBossEvent(bindings);
        ChatDisplayCodecs.DeclareCustomChatCompletions(bindings);
        ChatDisplayCodecs.DeclareCustomReportDetailsPlay(bindings);
        ChatDisplayCodecs.DeclareDeleteChat(bindings);
        ChatDisplayCodecs.DeclareServerData(bindings);
        ChatDisplayCodecs.DeclareSetDisplayChatPreview(bindings);
        CommandSuggestionCodecs.DeclareCommandSuggestion(bindings);
        CommandSuggestionCodecs.DeclareCommandSuggestions(bindings);
        CommandSuggestionCodecs.DeclareTabComplete(bindings);
        PlayerListCodecs.DeclarePlayerInfo(bindings);
        PlayerListCodecs.DeclarePlayerInfoRemove(bindings);
        PlayerListCodecs.DeclarePlayerInfoUpdate(bindings);
        PlayerListCodecs.DeclarePlayerListHeaderFooter(bindings);
        PlayerListCodecs.DeclareTabList(bindings);
        ResourcePackCodecs.DeclareResourcePackPlay(bindings);
        ResourcePackCodecs.DeclareResourcePackPopPlay(bindings);
        ResourcePackCodecs.DeclareResourcePackPushPlay(bindings);
        ResourcePackCodecs.DeclareResourcePackResponse(bindings);
        ScoreboardCodecs.DeclareDisplayScoreboard(bindings);
        ScoreboardCodecs.DeclareResetScore(bindings);
        ScoreboardCodecs.DeclareScoreboardObjective(bindings);
        ScoreboardCodecs.DeclareSetDisplayObjective(bindings);
        ScoreboardCodecs.DeclareSetObjective(bindings);
        ScoreboardCodecs.DeclareSetPlayerTeam(bindings);
        ScoreboardCodecs.DeclareSetScore(bindings);
        TitleCodecs.DeclareClearTitles(bindings);
        TitleCodecs.DeclareSetActionBarText(bindings);
        TitleCodecs.DeclareSetSubtitleText(bindings);
        TitleCodecs.DeclareSetTitleText(bindings);
        TitleCodecs.DeclareSetTitlesAnimation(bindings);
        TitleCodecs.DeclareTitle(bindings);
        UiMiscCodecs.DeclareAbilities(bindings);
        UiMiscCodecs.DeclareClearDialogPlay(bindings);
        UiMiscCodecs.DeclareCooldown(bindings);
        UiMiscCodecs.DeclareCustomClickActionPlay(bindings);
        UiMiscCodecs.DeclareEditBook(bindings);
        UiMiscCodecs.DeclareOpenBook(bindings);
        UiMiscCodecs.DeclareOpenSignEditor(bindings);
        UiMiscCodecs.DeclarePingPlay(bindings);
        UiMiscCodecs.DeclarePingRequestPlay(bindings);
        UiMiscCodecs.DeclarePlayerAbilities(bindings);
        UiMiscCodecs.DeclarePongPlay(bindings);
        UiMiscCodecs.DeclarePongResponsePlay(bindings);
        UiMiscCodecs.DeclareServerLinksPlay(bindings);
        UiMiscCodecs.DeclareShowDialogPlay(bindings);
        UiMiscCodecs.DeclareSignUpdate(bindings);
        UiMiscCodecs.DeclareWaypoint(bindings);
    }
}
