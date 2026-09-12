using Umpk.Game.Registries;
using Umpk.Text;

namespace Umpk.Client.Internal;

/// <summary>The client half of vanilla's chat-type decoration. From 1.19 (protocol 759) the server stopped sending a composed chat line and started sending the bare message body plus the sender name and a chat-type id; composing <c>&lt;sender&gt; body</c> became the CLIENT's job.</summary>
/// <remarks>
/// <para>The client does exactly two steps for player and disguised chat:</para>
/// First, <see cref="SignedChatVerification.DisplayContent"/> selects unsigned decorated content when present, otherwise literal signed content. The bound chat type then applies its translation key, resolved parameters, and style.
/// <para>The decoration is a datapack-driven registry lookup on the packet's chat-type id, and that registry is now retained: <see cref="ChatTypes"/> decodes it from whichever of the three wire shapes the version uses and <c>ConnectionApplier</c> installs it into <see cref="RegistryAccess.ChatTypes"/>, which is what <see cref="Decorate"/> reads.</para>
/// <para>When the id does not resolve - no registry installed yet, or an id the server never declared - player chat falls back to the default <c>minecraft:chat</c> decoration, which is <c>the built-in sender decoration</c> with parameters <c>[SENDER, CONTENT]</c> and an empty style, unchanged from 1.19 through 26.2. Disguised chat gets no fallback: it carries a command-issued type far more often than not, so guessing the player-chat key there would invent a prefix the server never asked for. The raw <c>ChatMessageReceived.ChatTypeId</c> and <c>TargetName</c> are published either way.</para>
/// <para>The fallback strings are Minecraft's <c>en_us</c> values for the seven built-in keys. This library ships no translation table, and <c>Component.ToPlainText</c> renders an unresolved key as <c>Fallback ?? Key</c> with the positional arguments substituted, so without them a 1.19+ chat line would read the literal text <c>chat.type.text</c>. The values are stable across the supported chat-type eras.</para>
/// </remarks>
internal static class ChatTypeDecoration
{
    /// <summary>Vanilla's translation key for the default <c>minecraft:chat</c> chat type.</summary>
    public const string DefaultChatKey = "chat.type.text";

    /// <summary>Vanilla's <c>en_us</c> template for <see cref="DefaultChatKey"/>.</summary>
    private const string DefaultChatFallback = "<%s> %s";

    /// <summary>The first protocol whose bound chat type is written as a HOLDER rather than a bare registry id: 1.21.</summary>
    /// <remarks>Protocols through 766 encode the raw registry id. Protocol 767 onward encodes <c>id + 1</c> and reserves zero for an inline chat type. Treating the later value as a raw id shifts the table by one entry.</remarks>
    private const int FirstHolderEncodedChatTypeProtocol = 767;

    /// <summary>Converts the chat-type value as it appears on the wire into a registry id, or -1 when it names no registry entry.</summary>
    /// <remarks>On 767+ a wire value of 0 is vanilla's <c>DIRECT_HOLDER_ID</c>: the chat type is encoded INLINE after it rather than referenced by id. UMPK's chat codecs read a bare VarInt and would already have mis-framed such a packet. Vanilla servers send a referenced holder here, so zero is reported as unresolvable rather than modelled.</remarks>
    public static int ToRegistryId(int wireValue, int protocol)
    {
        if (protocol < FirstHolderEncodedChatTypeProtocol)
            return wireValue;

        return wireValue <= 0 ? -1 : wireValue - 1;
    }

    /// <summary>Composes the line a client shows for one message body, using the server's chat-type registry when the id resolves through it.</summary>
    /// <param name="registry">The session's chat-type registry, or null when none was installed.</param>
    /// <param name="chatTypeId">The packet's chat-type network id.</param>
    /// <param name="content">The decoded message body.</param>
    /// <param name="senderName">The sender-name component the SENDER parameter substitutes.</param>
    /// <param name="targetName">The target/team-name component the TARGET parameter substitutes.</param>
    /// <param name="fallbackToPlayerChat">Whether an unresolved id falls back to the default <c>chat.type.text</c> decoration. True for signed player chat, false for disguised chat.</param>
    public static Component Decorate(
        Registry<ChatTypeDefinition>? registry,
        int chatTypeId,
        Component content,
        Component? senderName,
        Component? targetName,
        bool fallbackToPlayerChat)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (registry is not null && registry.TryGet(chatTypeId, out RegistryEntry<ChatTypeDefinition> entry))
        {
            // A resolved type with no decoration shows the body bare (vanilla's decorate behavior with an absent decoration). That is an answer, not a miss, so no fallback applies.
            return entry.Value.Chat is { } decoration
                ? Compose(decoration, content, senderName, targetName)
                : content;
        }

        return fallbackToPlayerChat ? DecoratePlayerChat(content, senderName) : content;
    }

    /// <summary>Wraps a decoded message body in the default chat-type decoration. Returns <paramref name="content"/> unchanged when there is no sender to name, so a decoration is never invented out of nothing.</summary>
    public static Component DecoratePlayerChat(Component content, Component? senderName)
    {
        ArgumentNullException.ThrowIfNull(content);
        return senderName is null
            ? content
            : new Component(new TranslatableContent(DefaultChatKey, DefaultChatFallback, [senderName, content]));
    }

    private static Component Compose(
        ChatDecorationDefinition decoration, Component content, Component? senderName, Component? targetName)
    {
        var args = new Component[decoration.Parameters.Count];
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = decoration.Parameters[i] switch
            {
                // Vanilla's chat-parameter selection coalesces an absent name to CommonComponents.EMPTY rather than skipping the slot, so an unset sender or target still occupies its argument.
                ChatDecorationParameter.Sender => senderName ?? Component.Text(string.Empty),
                ChatDecorationParameter.Target => targetName ?? Component.Text(string.Empty),
                _ => content,
            };
        }

        var translatable = new TranslatableContent(
            decoration.TranslationKey, VanillaFallback(decoration.TranslationKey), args);
        return new Component(translatable, decoration.Style);
    }

    /// <summary>Vanilla's <c>en_us</c> template for a built-in chat-type key, or null for a datapack key this client has no text for (which renders as the key itself, exactly as an unresolved key does in vanilla's own client).</summary>
    private static string? VanillaFallback(string key) => key switch
    {
        DefaultChatKey => DefaultChatFallback,
        "chat.type.announcement" => "[%s] %s",
        "chat.type.emote" => "* %s %s",
        "chat.type.team.text" => "%s <%s> %s",
        "chat.type.team.sent" => "-> %s <%s> %s",
        "commands.message.display.incoming" => "%s whispers to you: %s",
        "commands.message.display.outgoing" => "You whisper to %s: %s",
        _ => null,
    };
}
