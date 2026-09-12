using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Commands;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Movement;
using Umpk.Client.Plugins;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Verifies the plugin teardown contract: on detach, subscriptions, movement leases, command scopes, and plugin channels are all released, and the Detached token fires. Exercises the real <see cref="PluginHost"/> against a built (unconnected) client.</summary>
public sealed class PluginLifecycleTests
{
    private static UmpkClient BuildClient() => new UmpkClientBuilder()
        .UseVersion(JavaVersions.V1_21_5)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .Build();

    [Fact]
    public async Task Detach_Releases_All_Five_Resource_Classes_And_Fires_Token()
    {
        await using UmpkClient client = BuildClient();
        var eventBus = new EventBus(NullLogger.Instance, TimeSpan.Zero, 8);
        var leases = new MovementLeaseManager();
        var commands = new CommandService<ClientCommandSource>();
        var wire = new WireIndex(JavaVersions.V1_21_5);
        var channels = new PluginChannelManager(new NoopSink(), wire, JavaVersions.V1_21_5.Version.Protocol, NullLogger.Instance);
        var actions = BuildActions(client);
        var source = new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask);

        var host = new PluginHost("test", client, eventBus, actions, commands, leases, channels, NullLogger.Instance);

        bool detachedFired = false;
        host.Context.Detached.Register(() => detachedFired = true);

        // subscriptions, leases, command scope, plugin channels, scheduled work.
        int events = 0;
        host.Context.Events.Subscribe<Ping>(_ => events++);

        IMovementLease? lease = host.Context.TryAcquireMovement("nav");
        Assert.NotNull(lease);

        host.Context.Commands.Register(b => b.Literal("hi", l => l.Executes(_ => ValueTask.FromResult(1))));

        bool channelFired = false;
        host.Context.RegisterPluginChannel(Identifier.Minecraft("brand"), _ => channelFired = true);
        int wireId = wire.ClientboundPlay(Identifier.Minecraft("custom_payload"));
        byte[] frame = ChannelFrame(Identifier.Minecraft("brand"), [1, 2, 3]);

        int ticks = 0;
        host.Context.Scheduler.OnTick(() => ticks++);

        // Everything is live before detach.
        Assert.True((await commands.ExecuteAsync("hi", source)).Success);
        Assert.True(channels.DispatchInbound(wireId, frame));
        Assert.True(channelFired);
        host.Scheduler.Ticks.Tick(_ => { });
        Assert.Equal(1, ticks);

        // Detach and assert every resource class released.
        host.Detach();
        Assert.True(detachedFired);

        // subscriptions auto-disposed.
        _ = eventBus.PublishAsync(new Ping());
        Assert.Equal(0, events);

        // the lease is released, so a new acquirer succeeds.
        Assert.NotNull(leases.TryAcquire("other"));

        // the command scope is disposed, so the command no longer resolves.
        Assert.False((await commands.ExecuteAsync("hi", source)).Success);

        // the plugin channel is unregistered, so inbound frames no longer route to the handler.
        channelFired = false;
        Assert.False(channels.DispatchInbound(wireId, frame));
        Assert.False(channelFired);

        // scheduled per-tick work is removed, so ticking no longer invokes it.
        host.Scheduler.Ticks.Tick(_ => { });
        Assert.Equal(1, ticks);
    }

    private static byte[] ChannelFrame(Identifier channel, byte[] data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteString(channel.ToString());
        writer.WriteBytes(data);
        return buffer.WrittenSpan.ToArray();
    }

    private static ClientActions BuildActions(UmpkClient client)
    {
        // Actions are only used as the context field here; a no-op sink is sufficient.
        var sink = new NoopSink();
        var services = BuildServices(client);
        var commands = new CommandService<ClientCommandSource>();
        var chat = new ChatActions(
            sink,
            services,
            new CommandCompletionService(sink),
            commands,
            () => new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask),
            static (send, ct) => send(null, ct));
        return new ClientActions(
            chat,
            new MovementActions(sink, services, () => null),
            new InteractionActions(sink, services, new SequenceTracker()),
            new InventoryActions(sink, services),
            new SessionActions(sink, services),
            new DialogActions(sink, services, chat),
            services.Capabilities);
    }

    private static ClientSessionServices BuildServices(UmpkClient client) => new()
    {
        Version = JavaVersions.V1_21_5,
        Options = new ClientOptions(),
        Policies = new ClientPolicies(),
        State = client.State,
        Wire = new WireIndex(JavaVersions.V1_21_5),
        Logger = NullLogger.Instance,
        Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
    };

    private sealed record Ping : IClientEvent;

    private sealed class NoopSink : IPacketSink
    {
        public ValueTask SendAsync(object packet, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
