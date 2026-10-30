using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

[Collection(LiveServerCollection.Name)]
public sealed class RemotePlayerVelocityLiveTests(ITestOutputHelper output)
{
    public static TheoryData<string, int> Versions => new()
    {
        { "1.20.2", 764 },
        { "1.21.5", 770 },
        { "26.3", 777 },
    };

    [NightlyTheory]
    [MemberData(nameof(Versions))]
    public async Task NativeKnockbackIsImmediateForSelfAndSmoothedForTheRemotePlayer(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = budget.Token;
        await using LocalServer server = await LocalServer.StartAsync(IntegrationConfig.ServerDir(release), JavaRuntimes.ForProtocol(protocol), TimeSpan.FromMinutes(3), ct, port: 38920, rconPort: 38921);
        var ticks = new ObservedTickSource();
        await using UmpkClient observer = Build(version, "MotionWatch", ticks);
        await using UmpkClient peer = Build(version, "MotionPeer", ticks: null);
        var endpoint = new ServerEndpoint("127.0.0.1", (ushort)server.ServerPort);
        try
        {
            await observer.ConnectAsync(endpoint, ct);
            await peer.ConnectAsync(endpoint, ct);
            await WaitUntilAsync(() => peer.State.Self.HasSpawned && observer.State.Entities.Get(peer.State.Self.EntityId) is not null, ct);
            int remoteId = peer.State.Self.EntityId;
            await server.SendConsoleAsync("gamemode survival MotionPeer", ct);
            await server.SendConsoleAsync("tp MotionWatch 0.5 150 0.5", ct);
            await server.SendConsoleAsync("tp MotionPeer 1.5 150 0.5", ct);
            var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable marker = observer.Events.Subscribe<ChatMessageReceived>(message =>
            {
                if (message.Message.ToPlainText() == "motion-prepared")
                    prepared.TrySetResult();
            });
            await server.SendConsoleAsync("tellraw MotionWatch {\"text\":\"motion-prepared\"}", ct);
            await prepared.Task.WaitAsync(ct);
            Assert.Equal(Vec3d.Zero, observer.State.Entities.Get(remoteId)!.Velocity);

            var selfMotion = new TaskCompletionSource<Vec3d>(TaskCreationOptions.RunContinuationsAsynchronously);
            var remoteMotion = new TaskCompletionSource<Vec3d>(TaskCreationOptions.RunContinuationsAsynchronously);
            List<ClientboundSetEntityMotionPacket> remotePackets = [];
            int observations = 0;
            using IDisposable selfSubscription = peer.Events.Subscribe<PacketReceived>(message =>
            {
                if (message.Packet is ClientboundSetEntityMotionPacket motion && motion.EntityId == remoteId && peer.State.Self.Velocity != Vec3d.Zero)
                    selfMotion.TrySetResult(peer.State.Self.Velocity);
            });
            using IDisposable remoteSubscription = observer.Events.Subscribe<PacketReceived>(message =>
            {
                if (message.Packet is ClientboundSetEntityMotionPacket motion && motion.EntityId == remoteId
                    && (motion.VelocityX != 0 || motion.VelocityY != 0 || motion.VelocityZ != 0 || motion.ModernVelocityRaw?.Any(value => value != 0) == true))
                {
                    remotePackets.Add(motion);
                    if (Interlocked.Increment(ref observations) == 2)
                        remoteMotion.TrySetResult(observer.State.Entities.Get(remoteId)!.Velocity);
                }
            });
            using var damageDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            damageDeadline.CancelAfter(TimeSpan.FromSeconds(30));
            // ServerPlayer temporarily rejects damage after login. Require an actual native motion response before advancing the observer's clock.
            while (!selfMotion.Task.IsCompleted)
            {
                damageDeadline.Token.ThrowIfCancellationRequested();
                await server.SendConsoleAsync("damage MotionPeer 1 minecraft:mob_attack by MotionWatch", damageDeadline.Token);
                await Task.WhenAny(selfMotion.Task, Task.Delay(250, damageDeadline.Token));
            }
            Vec3d target = await selfMotion.Task.WaitAsync(damageDeadline.Token);
            Assert.NotEqual(Vec3d.Zero, target);
            Assert.Equal(Vec3d.Zero, await remoteMotion.Task.WaitAsync(damageDeadline.Token));
            // ServerEntity sends the changed tracker velocity and the damage broadcast. Both must arrive before the controlled client ticks.
            Assert.Equal(remotePackets[0].VelocityX, remotePackets[1].VelocityX);
            Assert.Equal(remotePackets[0].VelocityY, remotePackets[1].VelocityY);
            Assert.Equal(remotePackets[0].VelocityZ, remotePackets[1].VelocityZ);
            Assert.Equal(remotePackets[0].ModernVelocityRaw, remotePackets[1].ModernVelocityRaw);
            Vec3d expected = Vec3d.Zero;
            foreach (int remaining in new[] { 3, 2, 1 })
            {
                await ticks.FireAsync(ct);
                expected = new Vec3d(expected.X + (target.X - expected.X) / remaining, expected.Y + (target.Y - expected.Y) / remaining, expected.Z + (target.Z - expected.Z) / remaining);
                Vec3d actual = await observer.InvokeAsync(client => client.State.Entities.Get(remoteId)!.Velocity, ct);
                Assert.Equal(expected, actual);
                output.WriteLine($"{release} remote={remoteId} remaining={remaining} target={target} current={actual}");
            }
            Assert.Equal(2, Volatile.Read(ref observations));
            Assert.Equal(ClientStatus.Playing, observer.Status);
            Assert.Equal(ClientStatus.Playing, peer.Status);
            await peer.DisconnectAsync(ct);
            await observer.DisconnectAsync(ct);
            await WaitUntilAsync(() => server.LogText.Contains("MotionPeer lost connection", StringComparison.Ordinal) && server.LogText.Contains("MotionWatch lost connection", StringComparison.Ordinal), ct);
        }
        finally
        {
            output.WriteLine(server.LogText);
        }
    }

    private static UmpkClient Build(JavaVersion version, string name, ITickSource? ticks)
    {
        UmpkClientBuilder builder = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), name))
            .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
            .ConfigureFeatures(features => { features.Terrain = false; features.Physics = false; features.Pathfinding = false; })
            .ConfigureOptions(options => options.AutoSendPosition = false);
        if (ticks is not null)
            builder.UseTickSource(ticks);
        return builder.Build();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!predicate())
            await Task.Delay(20, deadline.Token);
    }

    private sealed class ObservedTickSource : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public async Task FireAsync(CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, ct);
            await completed.Task.WaitAsync(ct);
        }

        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++sequence;
                completed.TrySetResult();
            }
        }
    }
}
