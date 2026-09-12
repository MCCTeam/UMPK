using System.Buffers;
using Umpk.Client.Commands;
using Umpk.Client.Events;
using Umpk.Client.Movement;
using Umpk.Client.Plugins;
using Umpk.Client.Tests.Support;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="ClientPluginCollection"/>: runtime plugin membership on a live <see cref="UmpkClient"/>. Before this, the only entry point for a plugin was <see cref="UmpkClientBuilder.AddPlugin"/> before <see cref="UmpkClientBuilder.Build"/>, so nothing could add or remove a plugin once a client existed, let alone while a session was live. <see cref="PluginLifecycleTests"/> stays untouched and green: it still proves <see cref="PluginHost.Detach"/> itself releases every resource class, which this suite does not re-prove. What is new here is membership and timing: attach/detach at the right moment relative to the session loop, and isolation between plugins sharing one collection.</summary>
public sealed class ClientPluginCollectionTests
{
    private static readonly TimeSpan Budget = PluginSessionHarness.Budget;

    [Fact]
    public async Task AddAsync_BeforeConnect_AttachesAtSessionStart()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server);
        var plugin = new RecordingPlugin("pre-connect");

        ClientPluginRegistration registration = await client.Plugins.AddAsync(plugin, ct);

        // No live session yet: recorded as a member, but not attached.
        Assert.True(client.Plugins.Contains(plugin.Id));
        Assert.False(registration.IsAttached);
        Assert.Null(registration.Context);
        Assert.Equal(0, plugin.AttachCount);

        await PluginSessionHarness.JoinAsync(client, server, ct);

        Assert.Equal(1, plugin.AttachCount);
        Assert.True(registration.IsAttached);
        Assert.NotNull(registration.Context);
        Assert.Same(plugin.Context, registration.Context);
    }

    [Fact]
    public async Task AddAsync_DuringSession_AttachesOnTheLoop()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var scheduler = new ChannelSessionScheduler();
        try
        {
            await using FakeJavaServer server = FakeJavaServer.Create();
            await using UmpkClient client = PluginSessionHarness.BuildClient(server, b => b.UseScheduler(scheduler));
            await PluginSessionHarness.JoinAsync(client, server, ct);

            bool? wasOnLoop = null;
            var plugin = new RecordingPlugin("runtime")
            {
                OnAttach = _ => wasOnLoop = scheduler.IsCurrent,
            };

            ClientPluginRegistration registration = await client.Plugins.AddAsync(plugin, ct);

            Assert.True(registration.IsAttached);
            Assert.Equal(1, plugin.AttachCount);
            Assert.True(wasOnLoop, "Attach did not observe itself running on the session loop.");
        }
        finally
        {
            await scheduler.DisposeAsync();
        }
    }

    [Fact]
    public async Task AddAsync_DuringSession_CompletesOnlyAfterAttachReturned()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server);
        await PluginSessionHarness.JoinAsync(client, server, ct);

        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var plugin = new RecordingPlugin("blocking")
        {
            OnAttach = _ =>
            {
                started.Set();
                Assert.True(release.Wait(Budget), "The test never released the blocked Attach call.");
            },
        };

        Task<ClientPluginRegistration> addTask = client.Plugins.AddAsync(plugin, ct);

        Assert.True(started.Wait(Budget), "Attach never started.");
        await Task.Delay(50, ct); // Give a wrongly-early completion a chance to show up.
        Assert.False(addTask.IsCompleted, "AddAsync completed before Attach returned.");

        release.Set();
        ClientPluginRegistration registration = await addTask.WaitAsync(Budget, ct);

        Assert.Equal(1, plugin.AttachCount);
        Assert.True(registration.IsAttached);
    }

    [Fact]
    public async Task RemoveAsync_MidSession_ReleasesEveryResourceClassOfThatPluginOnly()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TwoPluginSession session = await TwoPluginSession.StartAsync(ct);
        await using UmpkClient client = session.Client;
        await using FakeJavaServer server = session.Server;
        ResourcefulPlugin a = session.A;
        ResourcefulPlugin b = session.B;

        Assert.True(a.HasLease); // A attached first and grabbed the exclusive lease.

        Assert.True(await client.Plugins.RemoveAsync(a.Id, ct));

        // subscriptions: A no longer observes packets.
        int before = a.PacketEvents;
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(1), ct);
        await Task.Delay(150, ct);
        Assert.Equal(before, a.PacketEvents);

        // the lease: released, so it can be acquired again.
        IMovementLease? reacquired = b.Context!.TryAcquireMovement("after-remove");
        Assert.NotNull(reacquired);
        reacquired.Dispose();

        // command scope: A's command no longer resolves.
        var source = new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask);
        Assert.False((await client.Commands.ExecuteAsync(a.Id, source)).Success);

        // plugin channel: A's channel no longer routes.
        a.ChannelFired = false;
        await session.SendChannelFrameAsync(a.Channel, [1], ct);
        await Task.Delay(150, ct);
        Assert.False(a.ChannelFired);

        // scheduled per-tick work: A's OnTick no longer runs.
        int ticksBefore = a.Ticks;
        session.Ticks.Advance();
        await Task.Delay(150, ct);
        Assert.Equal(ticksBefore, a.Ticks);

        // The Detached token fired for the removed plugin.
        Assert.True(a.DetachedFired);
    }

    [Fact]
    public async Task RemoveAsync_MidSession_LeavesTheOtherPluginsLive()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TwoPluginSession session = await TwoPluginSession.StartAsync(ct);
        await using UmpkClient client = session.Client;
        await using FakeJavaServer server = session.Server;
        ResourcefulPlugin a = session.A;
        ResourcefulPlugin b = session.B;

        Assert.True(await client.Plugins.RemoveAsync(a.Id, ct));

        // B still observes packets.
        int before = b.PacketEvents;
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(2), ct);
        await Task.Delay(150, ct);
        Assert.True(b.PacketEvents > before);

        // B's command still resolves.
        var source = new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask);
        Assert.True((await client.Commands.ExecuteAsync(b.Id, source)).Success);

        // B's plugin channel still routes.
        b.ChannelFired = false;
        await session.SendChannelFrameAsync(b.Channel, [1], ct);
        await Task.Delay(150, ct);
        Assert.True(b.ChannelFired);

        // B's scheduled tick work still runs.
        int ticksBefore = b.Ticks;
        session.Ticks.Advance();
        await Task.Delay(150, ct);
        Assert.True(b.Ticks > ticksBefore);

        Assert.True(client.Plugins.Contains(b.Id));
        Assert.False(client.Plugins.Contains(a.Id));
    }

    [Fact]
    public async Task RemoveAsync_MidSession_DoesNotCancelAnotherPluginsDetachedToken()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TwoPluginSession session = await TwoPluginSession.StartAsync(ct);
        await using UmpkClient client = session.Client;
        await using FakeJavaServer server = session.Server;
        ResourcefulPlugin a = session.A;
        ResourcefulPlugin b = session.B;

        Assert.False(b.Context!.Detached.IsCancellationRequested);

        Assert.True(await client.Plugins.RemoveAsync(a.Id, ct));

        Assert.True(a.DetachedFired);
        Assert.False(b.DetachedFired);
        Assert.False(b.Context!.Detached.IsCancellationRequested);
    }

    [Fact]
    public async Task RemovedPlugin_DoesNotReattachOnTheNextSession()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server1 = FakeJavaServer.Create();
        await using FakeJavaServer server2 = FakeJavaServer.Create();
        var plugin = new RecordingPlugin("reconnecting");

        await using UmpkClient client = PluginSessionHarness.BuildReconnectingClient(
            [server1.ClientPipe, server2.ClientPipe], b => b.AddPlugin(plugin));

        await PluginSessionHarness.JoinAsync(client, server1, ct);
        Assert.Equal(1, plugin.AttachCount);

        Assert.True(await client.Plugins.RemoveAsync(plugin.Id, ct));
        Assert.False(client.Plugins.Contains(plugin.Id));

        await client.DisconnectAsync(ct);
        Assert.Equal(ClientStatus.Disconnected, client.Status);

        await PluginSessionHarness.JoinAsync(client, server2, ct);

        Assert.Equal(1, plugin.AttachCount);
        Assert.False(client.Plugins.Contains(plugin.Id));
    }

    [Fact]
    public async Task AddAsync_WithADuplicateId_Throws()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server);

        await client.Plugins.AddAsync(new RecordingPlugin("dup"), ct);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Plugins.AddAsync(new RecordingPlugin("dup"), ct));

        // Build() routes builder plugins through the same membership path, so a duplicate id supplied to the builder now throws at Build() instead of silently registering two hosts under one id.
        Assert.Throws<ArgumentException>(() =>
            new UmpkClientBuilder()
                .UseVersion(PluginSessionHarness.Version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "Dup"))
                .AddPlugin(new RecordingPlugin("same"))
                .AddPlugin(new RecordingPlugin("same"))
                .Build());
    }

    [Fact]
    public async Task Ticking_WhileAPluginIsAddedAndRemoved_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var ticks = new ManualTickSource();
        await using UmpkClient client = PluginSessionHarness.BuildClient(server, b => b.UseTickSource(ticks));
        await PluginSessionHarness.JoinAsync(client, server, ct);

        for (int i = 0; i < 15; i++)
        {
            var plugin = new RecordingPlugin($"tick-{i}");
            await client.Plugins.AddAsync(plugin, ct);
            ticks.Advance();
            await Task.Delay(30, ct);
            Assert.True(await client.Plugins.RemoveAsync(plugin.Id, ct));
            ticks.Advance();
            await Task.Delay(30, ct);
        }

        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.Empty(client.Plugins.Ids);
    }

    /// <summary>A minimal plugin that counts attaches and optionally runs a callback from inside Attach.</summary>
    private sealed class RecordingPlugin(string id) : IClientPlugin
    {
        public string Id { get; } = id;

        public int AttachCount { get; private set; }

        public ClientPluginContext? Context { get; private set; }

        public Action<ClientPluginContext>? OnAttach { get; set; }

        public void Attach(ClientPluginContext context)
        {
            Context = context;
            AttachCount++;
            OnAttach?.Invoke(context);
        }
    }

    /// <summary>Grabs one of every resource class <see cref="PluginHost.Detach"/> is responsible for releasing.</summary>
    private sealed class ResourcefulPlugin(string id, Identifier channel) : IClientPlugin
    {
        private IDisposable? _channelRegistration;

        public string Id { get; } = id;

        public Identifier Channel { get; } = channel;

        public ClientPluginContext? Context { get; private set; }

        public int PacketEvents;

        public int Ticks;

        public bool ChannelFired;

        public bool DetachedFired;

        public bool HasLease { get; private set; }

        public void Attach(ClientPluginContext context)
        {
            Context = context;
            context.Events.Subscribe<PacketReceived>(_ => Interlocked.Increment(ref PacketEvents));
            context.Scheduler.OnTick(() => Interlocked.Increment(ref Ticks));
            HasLease = context.TryAcquireMovement(Id) is not null;
            _channelRegistration = context.RegisterPluginChannel(Channel, _ => ChannelFired = true);
            context.Commands.Register(b => b.Literal(Id, l => l.Executes(_ => ValueTask.FromResult(1))));
            context.Detached.Register(() => DetachedFired = true);
        }
    }

    /// <summary>A live two-plugin session (A attaches first, B second), for the isolation tests.</summary>
    private sealed class TwoPluginSession
    {
        private readonly Internal.WireIndex _wire = new(PluginSessionHarness.Version);

        private TwoPluginSession(UmpkClient client, FakeJavaServer server, ManualTickSource ticks, ResourcefulPlugin a, ResourcefulPlugin b)
        {
            Client = client;
            Server = server;
            Ticks = ticks;
            A = a;
            B = b;
        }

        public UmpkClient Client { get; }

        public FakeJavaServer Server { get; }

        public ManualTickSource Ticks { get; }

        public ResourcefulPlugin A { get; }

        public ResourcefulPlugin B { get; }

        public static async Task<TwoPluginSession> StartAsync(CancellationToken ct)
        {
            FakeJavaServer server = FakeJavaServer.Create();
            var ticks = new ManualTickSource();
            var a = new ResourcefulPlugin("plugin-a", Identifier.Minecraft("a_channel"));
            var b = new ResourcefulPlugin("plugin-b", Identifier.Minecraft("b_channel"));

            UmpkClient client = PluginSessionHarness.BuildClient(
                server, builder => builder.UseTickSource(ticks).AddPlugin(a).AddPlugin(b));

            await PluginSessionHarness.JoinAsync(client, server, ct);
            return new TwoPluginSession(client, server, ticks, a, b);
        }

        public Task SendChannelFrameAsync(Identifier channel, byte[] data, CancellationToken ct)
        {
            int wireId = _wire.ClientboundPlay(Identifier.Minecraft("custom_payload"));
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            writer.WriteString(channel.ToString());
            writer.WriteBytes(data);
            return Server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct).AsTask();
        }
    }
}
