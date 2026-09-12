using Umpk.Text;
using Xunit;

namespace Umpk.Data.Lang.Tests;

/// <summary>Keeps the client's built-in chat templates aligned with every shipped language table. The client uses these dependency-free fallbacks before a chat-type registry is available or for unknown ids.</summary>
public sealed class ChatTypeFallbackDriftTests
{
    // 1.19 (759) through the newest shipped table (776): every protocol whose chat pipeline can reach this decoration at all.
    private static readonly int[] Protocols =
    [
        759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776,
    ];

    private static void AssertLiteralOnEveryProtocol(string key, string expected)
    {
        foreach (int protocol in Protocols)
        {
            ITranslationSource table = VanillaTranslations.ForProtocol(protocol);
            Assert.True(table.TryResolve(key, out string? actual), $"protocol {protocol} has no '{key}'");
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ChatTypeText_MatchesTheClientFallback_OnEveryProtocol() =>
        AssertLiteralOnEveryProtocol("chat.type.text", "<%s> %s");

    [Fact]
    public void ChatTypeAnnouncement_MatchesTheClientFallback_OnEveryProtocol() =>
        AssertLiteralOnEveryProtocol("chat.type.announcement", "[%s] %s");

    [Fact]
    public void ChatTypeEmote_MatchesTheClientFallback_OnEveryProtocol() =>
        AssertLiteralOnEveryProtocol("chat.type.emote", "* %s %s");

    [Fact]
    public void ChatTypeTeamText_MatchesTheClientFallback_OnEveryProtocol() =>
        AssertLiteralOnEveryProtocol("chat.type.team.text", "%s <%s> %s");

    [Fact]
    public void ChatTypeTeamSent_MatchesTheClientFallback_OnEveryProtocol() =>
        AssertLiteralOnEveryProtocol("chat.type.team.sent", "-> %s <%s> %s");

    [Fact]
    public void CommandsMessageDisplayIncoming_MatchesTheClientFallback_OnEveryProtocol() =>
        AssertLiteralOnEveryProtocol("commands.message.display.incoming", "%s whispers to you: %s");

    [Fact]
    public void CommandsMessageDisplayOutgoing_MatchesTheClientFallback_OnEveryProtocol() =>
        AssertLiteralOnEveryProtocol("commands.message.display.outgoing", "You whisper to %s: %s");
}
