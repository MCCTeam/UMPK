using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Commands;
using Umpk.Client.Movement;
using Umpk.Client.Plugins;
using Umpk.Client.Tests.Support;
using Umpk.Commands;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="UmpkClient.ObservePackets"/> and <see cref="ClientPluginContext.ObservePackets"/>: a handle on the raw frame feed that a plugin gets automatically released for, on top of the raw <see cref="UmpkClient.PacketFrameObserved"/> event this wraps. Delivered through the non-escaping <see cref="PacketFrame"/> ref-struct view (see <c>PacketFrameTests</c> for the type itself).</summary>
public sealed class PacketObservationScopeTests
{
    private static readonly TimeSpan Budget = PluginSessionHarness.Budget;

    [Fact]
    public async Task ObservePackets_DeliversBothDirections()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server);
        await PluginSessionHarness.JoinAsync(client, server, ct);

        var seenInbound = new List<(PacketFlow Flow, int WireId, byte[] Payload)>();
        var seenOutbound = new List<(PacketFlow Flow, int WireId, byte[] Payload)>();
        using IDisposable subscription = client.ObservePackets((in PacketFrame frame) =>
        {
            byte[] copy = frame.CopyPayload();
            if (frame.IsClientbound)
                lock (seenInbound)
                    seenInbound.Add((frame.Flow, frame.WireId, copy));

            else
                lock (seenOutbound)
                    seenOutbound.Add((frame.Flow, frame.WireId, copy));

        });

        // Inbound: the server sends a keep-alive.
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(0x42), ct);

        // Outbound: the client sends a chat message.
        await client.Actions.Chat.SendAsync("hello", ct);

        await WaitForAsync(() => seenInbound.Count > 0 && seenOutbound.Count > 0, ct);

        Assert.Contains(seenInbound, f => f.Flow == PacketFlow.Clientbound);
        Assert.Contains(seenOutbound, f => f.Flow == PacketFlow.Serverbound);
    }

    /// <summary>The direct, PluginHost-level proof, in the same style <c>PluginLifecycleTests</c> uses for the other four resource classes: constructs a real <see cref="PluginHost"/> against a REAL, connected client (needed for a real frame to observe), calls <see cref="PluginHost.Detach"/> directly, and asserts the handle stops firing for a frame sent afterward. The session itself stays alive throughout: detaching one host must not need the connection to go away.</summary>
    [Fact]
    public async Task ObservePackets_HandleReleasesOnDetach()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server);
        await PluginSessionHarness.JoinAsync(client, server, ct);

        var eventBus = new Umpk.Client.Events.EventBus(NullLogger.Instance, TimeSpan.Zero, 8);
        var leases = new MovementLeaseManager();
        var commands = new CommandService<ClientCommandSource>();
        var wire = new Internal.WireIndex(PluginSessionHarness.Version);
        var channels = new PluginChannelManager(new NoopSink(), wire, PluginSessionHarness.Version.Version.Protocol, NullLogger.Instance);

        var host = new PluginHost("direct", client, eventBus, client.Actions, commands, leases, channels, NullLogger.Instance);

        int frameCount = 0;
        host.Context.ObservePackets((in PacketFrame f) => Interlocked.Increment(ref frameCount));

        int before = frameCount;
        for (int attempt = 0; attempt < 100 && frameCount <= before; attempt++)
        {
            await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(attempt), ct);
            await Task.Delay(25, ct);
        }

        Assert.True(frameCount > before, "the handle never fired; it is not live.");

        host.Detach();

        int afterDetach = frameCount;
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(999), ct);
        await Task.Delay(150, ct);

        Assert.Equal(afterDetach, frameCount);
        Assert.Equal(ClientStatus.Playing, client.Status);
    }

    [Fact]
    public async Task ObservePackets_HandleReleasesOnRemoveAsync()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var plugin = new ObservingPlugin();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server, b => b.AddPlugin(plugin));
        await PluginSessionHarness.JoinAsync(client, server, ct);

        await AwaitObservedAsync(server, plugin, ct);

        Assert.True(await client.Plugins.RemoveAsync(plugin.Id, ct));

        int before = plugin.FrameCount;
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(2), ct);
        await Task.Delay(150, ct);

        Assert.Equal(before, plugin.FrameCount);
    }

    [Fact]
    public async Task ObservePackets_ThrowingHandlerIsIsolatedFromTheReadLoop()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server);
        await PluginSessionHarness.JoinAsync(client, server, ct);

        int handlerCalls = 0;
        using IDisposable subscription = client.ObservePackets((in PacketFrame frame) =>
        {
            Interlocked.Increment(ref handlerCalls);
            throw new InvalidOperationException("deliberate: a plugin's handler misbehaves");
        });

        var decoded = new List<Umpk.Client.Events.PacketReceived>();
        using IDisposable eventSub = client.Events.Subscribe<Umpk.Client.Events.PacketReceived>(e =>
        {
            lock (decoded)
                decoded.Add(e);

        });

        for (int i = 0; i < 5; i++)
            await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(i), ct);

        await WaitForAsync(() =>
        {
            lock (decoded)
                return decoded.Count >= 5;

        }, ct);

        Assert.True(handlerCalls >= 5, "the throwing handler was not even called for every frame.");
        Assert.Equal(ClientStatus.Playing, client.Status);
    }

    private static async Task AwaitObservedAsync(FakeJavaServer server, ObservingPlugin plugin, CancellationToken ct)
    {
        int before = plugin.FrameCount;
        for (int attempt = 0; attempt < 100 && plugin.FrameCount <= before; attempt++)
        {
            await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(attempt), ct);
            await Task.Delay(25, ct);
        }

        Assert.True(plugin.FrameCount > before, "the plugin's ObservePackets handler never fired; it is not live.");
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(25, ct);

        Assert.True(condition(), "condition was never satisfied within the budget.");
    }

    private sealed class ObservingPlugin : IClientPlugin
    {
        public string Id => "observer";

        public ClientPluginContext? Context { get; private set; }

        public int FrameCount;

        public void Attach(ClientPluginContext context)
        {
            Context = context;
            context.ObservePackets((in PacketFrame frame) => Interlocked.Increment(ref FrameCount));
        }
    }

    private sealed class NoopSink : Internal.IPacketSink
    {
        public ValueTask SendAsync(object packet, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
