using Umpk.Client.Internal;
using Umpk.Game.Registries;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><c>ChatTypeDecoration</c>'s internal fallback map returns vanilla's own <c>en_us</c> literal for each of the seven built-in chat-type keys. <c>Umpk.Client.Tests</c> is a friend assembly of <c>Umpk.Client</c> (<c>InternalsVisibleTo</c>), which is what lets this test reach the internal <c>ChatTypeDecoration</c> type at all; the fallback switch itself stays <c>private</c> (this table is the zero-dependency degradation path a session falls back to before any registry, and Phase C's <c>Umpk.Data.Lang</c> package is deliberately not referenced from <c>Umpk.Client</c>, so nothing here should tempt a future edit to wire the two together), so the fallback is read back the same way a real session does: resolve each key through <c>Decorate</c> against a minimal registry and inspect the resulting <see cref="TranslatableContent"/>'s <see cref="TranslatableContent.Fallback"/>.</summary>
public sealed class ChatTypeFallbackTests
{
    [Fact]
    public void VanillaFallback_MatchesTheShippedVanillaLiteralForEveryBuiltInChatTypeKey()
    {
        (string Key, string Expected)[] cases =
        [
            ("chat.type.text", "<%s> %s"),
            ("chat.type.announcement", "[%s] %s"),
            ("chat.type.emote", "* %s %s"),
            ("chat.type.team.text", "%s <%s> %s"),
            ("chat.type.team.sent", "-> %s <%s> %s"),
            ("commands.message.display.incoming", "%s whispers to you: %s"),
            ("commands.message.display.outgoing", "You whisper to %s: %s"),
        ];

        var networkIds = new int[cases.Length];
        var keys = new Identifier[cases.Length];
        var values = new ChatTypeDefinition[cases.Length];
        for (int i = 0; i < cases.Length; i++)
        {
            networkIds[i] = i;
            keys[i] = new Identifier("minecraft", cases[i].Key);
            values[i] = new ChatTypeDefinition(new ChatDecorationDefinition(
                cases[i].Key,
                [ChatDecorationParameter.Sender, ChatDecorationParameter.Content],
                Style.Empty));
        }

        Registry<ChatTypeDefinition> registry = Registry.FromEntries(
            new Identifier("minecraft", "chat_type"), networkIds, keys, values);

        Component sender = Component.Text("Steve");
        Component body = Component.Text("hello");

        for (int id = 0; id < cases.Length; id++)
        {
            (string key, string expected) = cases[id];
            Component result = ChatTypeDecoration.Decorate(registry, id, body, sender, targetName: null, fallbackToPlayerChat: false);
            TranslatableContent translatable = Assert.IsType<TranslatableContent>(result.Content);
            Assert.Equal(key, translatable.Key);
            Assert.Equal(expected, translatable.Fallback);
        }
    }
}
