using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Commands;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="ChatActions.SendAsync"/>: the one entry point that routes a chat line the way typing it does. A leading <c>/</c> takes the command path on every supported era; anything else is sent as chat. No trim, no <c>//</c> stripping: that escape is product UX layered on top by a host, not this method's job.</summary>
public sealed class ChatSendRoutingTests
{
    [Fact]
    public async Task LeadingSlash_RoutesToCommandPath_Protocol47PreservesTheSlashOnTheChatFrame()
    {
        (ChatActions chat, RecordingSink sink) = Build(47);

        await chat.SendAsync("/say hi");

        // Below 1.19 there is no separate command wire: vanilla's own handleChat routes a leading slash into the dispatcher, so the slashed text IS the command frame.
        var sent = Assert.IsType<ServerboundLegacyChatPacket>(Assert.Single(sink.Packets));
        Assert.Equal("/say hi", sent.Message);
    }

    [Fact]
    public async Task LeadingSlash_RoutesToCommandPath_Protocol776SendsChatCommand()
    {
        (ChatActions chat, RecordingSink sink) = Build(776);

        await chat.SendAsync("/say hi");

        var sent = Assert.IsType<ServerboundChatCommandPacket>(Assert.Single(sink.Packets));
        Assert.Equal("say hi", sent.Command);
    }

    [Fact]
    public async Task PlainText_RoutesToChatPath_Protocol47()
    {
        (ChatActions chat, RecordingSink sink) = Build(47);

        await chat.SendAsync("hello world");

        var sent = Assert.IsType<ServerboundLegacyChatPacket>(Assert.Single(sink.Packets));
        Assert.Equal("hello world", sent.Message);
    }

    [Fact]
    public async Task PlainText_RoutesToChatPath_Protocol776()
    {
        (ChatActions chat, RecordingSink sink) = Build(776);

        await chat.SendAsync("hello world");

        // No certificates in this harness, so the send falls back to the unsigned shape; the point under test is that it is NOT a command frame.
        var sent = Assert.IsType<ServerboundSignedChatPacket>(Assert.Single(sink.Packets));
        Assert.Equal("hello world", sent.Message);
        Assert.Null(sent.Signature);
    }

    [Fact]
    public async Task DoubleSlash_IsNotStripped_SoItIsACommandNamedSlashFoo()
    {
        (ChatActions chat, RecordingSink sink) = Build(776);

        // "//foo": SendAsync itself does no trimming or escape handling, so the second slash is part of the command text SendCommandAsync sends, not a chat message with an escaped leading slash.
        await chat.SendAsync("//foo");

        var sent = Assert.IsType<ServerboundChatCommandPacket>(Assert.Single(sink.Packets));
        Assert.Equal("/foo", sent.Command);
    }

    [Fact]
    public async Task Send_RejectsNull()
    {
        (ChatActions chat, RecordingSink _) = Build(776);

        await Assert.ThrowsAsync<ArgumentNullException>(() => chat.SendAsync(null!));
    }

    private static (ChatActions Chat, RecordingSink Sink) Build(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
        var state = new ClientState(new ClientFeatures().Normalized());
        var recorder = new RecordingSink();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions { ChatCooldown = TimeSpan.Zero },
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };

        var chat = new ChatActions(
            recorder,
            services,
            new CommandCompletionService(recorder),
            new CommandService<ClientCommandSource>(),
            () => null!,
            static (send, ct) => send(null, ct));
        return (chat, recorder);
    }
}
