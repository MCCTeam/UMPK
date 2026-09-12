using System.Diagnostics.CodeAnalysis;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Chat-type decoration for inbound player chat, protocols 759-776.</summary>
/// <remarks>
/// <para>Through 1.18.2 the server composed the line itself (the server builds the <c>chat.type.text</c> translation with the display name and message, so a client only had to print what arrived. From 1.19 the server sends the bare body plus the sender name and a chat-type id, and the client decorates the message content with that bound type (the client consumes that result).</para>
/// <para>Only the inner half of that existed, so the whole 759-776 band rendered the body alone: a live run showed a 1.19.2 client printing <c>t3out1192</c> for the line a 1.16.5 client printed as <c>&lt;t3a_1165&gt; t3out1165</c>, and a team prefix that DID render on 754 vanished on 759+ for the same reason.</para>
/// </remarks>
public sealed class PlayerChatDecorationTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");

    private const string Body = "words on the wire";

    /// <summary>Every signed-chat codec era: v1, v2, and the three v3 members.</summary>
    public static TheoryData<int> SigningEra => [759, 760, 761, 765, 770, 776];

    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    private static ClientboundPlayerChatPacket Chat(Component senderName) =>
        new(
            Sender: Sender,
            Index: 0,
            Signature: null,
            SignedContent: Body,
            TimestampMillis: 1_700_000_000_000L,
            Salt: 0,
            UnsignedContent: null,
            ChatTypeId: 1,
            SenderName: senderName,
            TargetName: null);

    private static async Task<ChatMessageReceived> DeliverAsync(int protocol, ClientboundPlayerChatPacket packet)
    {
        var harness = new ApplierHarness(Version(protocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        // Through the bound descriptor codec, so the era's wire form reaches the applier.
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "player_chat", packet));
        Assert.NotNull(seen);
        return seen!;
    }

    [Theory]
    [MemberData(nameof(SigningEra))]
    public async Task PlayerChat_RendersTheSenderName_OnEverySigningWireLayout(int protocol)
    {
        ChatMessageReceived seen = await DeliverAsync(protocol, Chat(Component.Text("Notch")));

        Assert.Equal(ChatCategory.Player, seen.Category);
        Assert.Equal($"<Notch> {Body}", seen.Message.ToPlainText());
        Assert.Equal(Body, seen.Body.ToPlainText());
        Assert.Equal("Notch", seen.SenderName!.ToPlainText());
    }

    /// <summary>The decoration is a real translatable over the vanilla key and parameter order, not a pre-formatted string, so a host that supplies a translation table gets a correctly localized line and one that does not gets vanilla's own en_us template.</summary>
    [Theory]
    [MemberData(nameof(SigningEra))]
    public async Task TheDecoration_IsVanillasChatTypeTextTranslatable(int protocol)
    {
        ChatMessageReceived seen = await DeliverAsync(protocol, Chat(Component.Text("Notch")));

        var translatable = Assert.IsType<TranslatableContent>(seen.Message.Content);
        Assert.Equal("chat.type.text", translatable.Key);
        Assert.Equal(2, translatable.Args.Count);
        Assert.Equal("Notch", translatable.Args[0].ToPlainText());
        Assert.Equal(Body, translatable.Args[1].ToPlainText());

        // A host with its own table renders the localized form, not the fallback.
        Assert.Equal(
            $"Notch says: {Body}",
            seen.Message.ToPlainText(new StubTranslations("chat.type.text", "%s says: %s")));
    }

    /// <summary>The team-prefix case. A scoreboard team prefix rides on the sender-name component, so it renders only if the sender name is placed in the line at all. This is the cell that passed on 754 and failed on every protocol from 759 up.</summary>
    [Theory]
    [MemberData(nameof(SigningEra))]
    public async Task ATeamPrefixOnTheSenderName_Renders(int protocol)
    {
        Component prefixed = new(new TextContent("[Admin] "), Style.Empty, [Component.Text("t3a")]);
        ChatMessageReceived seen = await DeliverAsync(protocol, Chat(prefixed));

        Assert.Equal($"<[Admin] t3a> {Body}", seen.Message.ToPlainText());
    }

    /// <summary>The chat-type id and the target name reach the consumer, and the id published is the REGISTRY id rather than the raw wire value.</summary>
    /// <remarks>Through 1.20.6 the bound chat type is a bare registry id. From 1.21 it is <c>id + 1</c>, with 0 reserved for an inline chat type; 1.21.2 through 26.2 still do. Measured live on 1.21.8 (protocol 772): a plain chat line arrived as wire value 1, and vanilla renders that <c>&lt;name&gt; body</c>, i.e. <c>minecraft:chat</c> at registry id 0 of the alphabetically ordered table that server sent.</remarks>
    [Theory]
    [InlineData(759, 4)]
    [InlineData(760, 4)]
    [InlineData(761, 4)]
    [InlineData(765, 4)]
    [InlineData(770, 3)]
    [InlineData(776, 3)]
    public async Task TheChatTypeIdAndTargetName_ReachTheConsumer(int protocol, int expectedRegistryId)
    {
        ClientboundPlayerChatPacket packet = Chat(Component.Text("Notch")) with
        {
            ChatTypeId = 4,
            TargetName = Component.Text("Steve"),
        };
        ChatMessageReceived seen = await DeliverAsync(protocol, packet);

        Assert.Equal(expectedRegistryId, seen.ChatTypeId);
        Assert.Equal("Steve", seen.TargetName!.ToPlainText());
    }

    /// <summary>The control that guards against double decoration: pre-1.19 servers send an ALREADY composed line, so that band must pass through untouched. A decoration applied there would render <c>&lt;&gt; &lt;Notch&gt; body</c>.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(754)]
    [InlineData(758)]
    public async Task LegacyChat_IsNotDecoratedAgain(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        Component composed = Component.Translatable("chat.type.text", Component.Text("Notch"), Component.Text(Body));
        await harness.ApplyAsync(new ClientboundLegacyChatPacket(composed, 0));

        Assert.NotNull(seen);
        Assert.Equal(ChatCategory.Legacy, seen!.Category);
        Assert.Same(composed, seen.Message);
        Assert.Same(composed, seen.Body);
        Assert.Null(seen.SenderName);
    }

    /// <summary>Disguised chat with NO chat-type registry installed stays undecorated: it carries a command-issued chat type far more often than the player-chat one, so decorating it with <c>chat.type.text</c> would be a guess rather than a default. Its id and target are published either way. When the server's registry IS installed the id resolves and the line is decorated through it, which is what <c>ChatTypeRegistryTests.DisguisedChat_IsDecoratedThroughTheResolvedChatType</c> covers.</summary>
    [Theory]
    [InlineData(765, 2)]
    [InlineData(776, 1)]
    public async Task DisguisedChat_StaysUndecorated_ButPublishesItsChatType(int protocol, int expectedRegistryId)
    {
        var harness = new ApplierHarness(Version(protocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        Component message = Component.Text("[Server] announcement");
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol,
            "disguised_chat",
            new ClientboundDisguisedChatPacket(message, ChatTypeId: 2, Component.Text("Server"), TargetName: null)));

        Assert.NotNull(seen);
        Assert.Equal(ChatCategory.Disguised, seen!.Category);
        Assert.Equal("[Server] announcement", seen.Message.ToPlainText());
        Assert.Equal(expectedRegistryId, seen.ChatTypeId);
    }

    private sealed class StubTranslations(string key, string template) : ITranslationSource
    {
        public bool TryResolve(string translationKey, [NotNullWhen(true)] out string? value)
        {
            if (string.Equals(translationKey, key, StringComparison.Ordinal))
            {
                value = template;
                return true;
            }

            value = null;
            return false;
        }
    }
}
