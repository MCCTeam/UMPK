using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>What a self effect's remaining duration actually is, and what a respawn does to the effect table.</summary>
/// <remarks>
/// <para>UMPK never ticked a duration down. <c>SelfState.ActiveEffects</c> held whatever number the last <c>update_mob_effect</c> carried, and the only writers are <c>EntityApplier.ApplyEffectAsync</c> and the <c>remove_mob_effect</c> arm, neither of which decrements. Vanilla's server sends <c>update_mob_effect</c> on add and on refresh and <c>remove_mob_effect</c> on expiry and nothing in between, so PRESENCE was reliable and the NUMBER was not: a 600-tick potion still reported 600 nine seconds later.</para>
/// <para>The applier stamps the session tick and derives the remainder, which keeps the wire value verbatim and computes the derived one - the same discipline <c>AirSupplyRule</c> follows.</para>
/// </remarks>
public sealed class SelfEffectDurationTests
{
    private const int SelfEntityId = 1;

    /// <summary>1.21.8, well inside the zero-based mob-effect era.</summary>
    private const int ModernProtocol = 772;

    /// <summary>1.8, whose respawn packet has no data-to-keep field at all.</summary>
    private const int LegacyProtocol = 47;

    /// <summary><c>minecraft:fire_resistance</c> on 764+ (zero-based).</summary>
    private const int FireResistanceModernId = 11;

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    // The derivation itself

    /// <summary>The headline case from the queue: apply 200, advance 40, read 160.</summary>
    [Fact]
    public void AnAppliedEffect_CountsDownWithTheSessionTick()
    {
        var effect = new ActiveEffect(FireResistanceModernId, Amplifier: 0, Duration: 200, Flags: 0)
        {
            AppliedAtTick = 0,
        };

        Assert.Equal(200, effect.RemainingTicksAt(0));
        Assert.Equal(160, effect.RemainingTicksAt(40));
        Assert.Equal(1, effect.RemainingTicksAt(199));
    }

    /// <summary>Past its end the remainder floors at zero rather than going negative, because the server's <c>remove_mob_effect</c> is what actually retires the entry and it may be a tick or two late.</summary>
    [Fact]
    public void AnExpiredEffect_FloorsAtZeroRatherThanGoingNegative()
    {
        var effect = new ActiveEffect(FireResistanceModernId, 0, Duration: 200, Flags: 0) { AppliedAtTick = 0 };

        Assert.Equal(0, effect.RemainingTicksAt(200));
        Assert.Equal(0, effect.RemainingTicksAt(5000));
    }

    /// <summary>An infinite effect, encoded as -1 from 1.21.2 onward, never counts down and never becomes a large finite number.</summary>
    [Fact]
    public void AnInfiniteEffect_StaysInfinite()
    {
        var effect = new ActiveEffect(FireResistanceModernId, 0, Duration: -1, Flags: 0) { AppliedAtTick = 0 };

        Assert.True(effect.IsInfinite);
        Assert.Equal(ActiveEffect.InfiniteRemaining, effect.RemainingTicksAt(0));
        Assert.Equal(ActiveEffect.InfiniteRemaining, effect.RemainingTicksAt(100_000));
    }

    /// <summary>An effect with no stamp cannot be dated, and the honest answer is "present, duration unknown" - never the raw applied duration, which is what a consumer gating on "duration >= route ticks" would otherwise read as a full potion.</summary>
    [Fact]
    public void AnUnstampedEffect_RefusesToGuessARemainder()
    {
        var effect = new ActiveEffect(FireResistanceModernId, 0, Duration: 200, Flags: 0);

        Assert.Equal(ActiveEffect.UnstampedTick, effect.AppliedAtTick);
        Assert.Equal(ActiveEffect.UnknownRemaining, effect.RemainingTicksAt(40));
    }

    /// <summary>A clock that ran backwards (a reconnect that reset the counter while an effect survived) is the same "cannot be dated" case, not a negative elapsed.</summary>
    [Fact]
    public void AStampFromTheFuture_IsUnknownRatherThanNegativeElapsed()
    {
        var effect = new ActiveEffect(FireResistanceModernId, 0, Duration: 200, Flags: 0) { AppliedAtTick = 500 };

        Assert.Equal(ActiveEffect.UnknownRemaining, effect.RemainingTicksAt(40));
    }

    // The applier stamps it

    [Fact]
    public async Task TheApplier_StampsASelfEffectWithTheSessionTick()
    {
        ApplierHarness harness = Build(ModernProtocol);
        harness.State.AdvanceSessionTick(7);

        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceModernId, Amplifier: 0, Duration: 200, Flags: 0));

        ActiveEffect effect = harness.State.Self.ActiveEffects[FireResistanceModernId];
        Assert.Equal(7, effect.AppliedAtTick);
        Assert.Equal(200, effect.Duration);

        harness.State.AdvanceSessionTick(40);
        Assert.Equal(160, effect.RemainingTicksAt(harness.State.SessionTick));
    }

    /// <summary>A refresh re-stamps. Without this, a potion drunk again at second 9 would still be dated from the first one and read as nearly expired.</summary>
    [Fact]
    public async Task ARefreshedEffect_IsRestampedAtTheRefresh()
    {
        ApplierHarness harness = Build(ModernProtocol);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceModernId, 0, Duration: 200, Flags: 0));

        harness.State.AdvanceSessionTick(40);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceModernId, 0, Duration: 300, Flags: 0));

        ActiveEffect effect = harness.State.Self.ActiveEffects[FireResistanceModernId];
        Assert.Equal(40, effect.AppliedAtTick);
        Assert.Equal(300, effect.RemainingTicksAt(40));

        harness.State.AdvanceSessionTick(60);
        Assert.Equal(240, effect.RemainingTicksAt(harness.State.SessionTick));
    }

    [Fact]
    public async Task RemoveMobEffect_RetiresTheEntryEntirely()
    {
        ApplierHarness harness = Build(ModernProtocol);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceModernId, 0, Duration: 200, Flags: 0));
        Assert.True(harness.State.Self.ActiveEffects.ContainsKey(FireResistanceModernId));

        await harness.ApplyAsync(new ClientboundRemoveMobEffectPacket(SelfEntityId, FireResistanceModernId));

        Assert.Empty(harness.State.Self.ActiveEffects);
    }

    // Respawn: which branch keeps the effects and which one throws them away

    /// <summary>A death respawn clears the table because the data-to-keep mask is zero. The server does not send <c>remove_mob_effect</c> for effects destroyed by death, so the respawn handler must clear them.</summary>
    [Fact]
    public async Task ADeathRespawn_ClearsTheEffectTable()
    {
        ApplierHarness harness = Build(ModernProtocol);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceModernId, 0, Duration: 200, Flags: 0));

        await harness.ApplyAsync(new ClientboundRespawnPacket(Spawn(), DataToKeep: 0, Legacy: null));

        Assert.Empty(harness.State.Self.ActiveEffects);
    }

    /// <summary>The dangerous polarity stated on its own: an INFINITE effect that survives a death is a bot that believes it is permanently fireproof.</summary>
    [Fact]
    public async Task AnInfiniteEffect_DoesNotSurviveADeathRespawn()
    {
        ApplierHarness harness = Build(ModernProtocol);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceModernId, 0, Duration: -1, Flags: 0));
        Assert.True(harness.State.Self.ActiveEffects[FireResistanceModernId].IsInfinite);

        await harness.ApplyAsync(new ClientboundRespawnPacket(Spawn(), DataToKeep: 0, Legacy: null));

        Assert.Empty(harness.State.Self.ActiveEffects);
    }

    /// <summary>A DIMENSION CHANGE keeps them. <c>ClientboundRespawnPacket(..., (byte)3)</c> (<c>KEEP_ALL_DATA</c>) does not destroy the player, so its effects are still running on the server side. Clearing here would blind the client to a potion it genuinely still has.</summary>
    [Theory]
    [InlineData((byte)3)] // KEEP_ALL_DATA, what changeDimension sends
    [InlineData((byte)2)] // KEEP_ENTITY_DATA alone
    [InlineData((byte)1)] // KEEP_ATTRIBUTE_MODIFIERS alone, what respawn behavior sends
    public async Task ARespawnThatKeepsAnything_KeepsTheEffectTable(byte dataToKeep)
    {
        ApplierHarness harness = Build(ModernProtocol);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceModernId, 0, Duration: 200, Flags: 0));

        await harness.ApplyAsync(new ClientboundRespawnPacket(Spawn(), dataToKeep, Legacy: null));

        Assert.True(harness.State.Self.ActiveEffects.ContainsKey(FireResistanceModernId));
    }

    /// <summary>47-404 have no keep mask on the wire at all, so the decoded packet always carries 0 and every legacy respawn clears. This is lossless because active effects are re-sent after the respawn frame.</summary>
    [Fact]
    public async Task ALegacyRespawn_ClearsTheEffectTable()
    {
        ApplierHarness harness = Build(LegacyProtocol);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, EffectId: 12, Amplifier: 0, Duration: 200, Flags: 0));
        Assert.NotEmpty(harness.State.Self.ActiveEffects);

        await harness.ApplyAsync(new ClientboundRespawnPacket(
            SpawnInfo: null, DataToKeep: 0,
            new LegacyRespawnFields(Dimension: 0, Difficulty: 0, GameMode: 0, LevelType: "default")));

        Assert.Empty(harness.State.Self.ActiveEffects);
    }

    // The counter is a LIVE counter, not a field only tests move

    /// <summary>The whole derivation rests on the session tick counter actually advancing on the client's own tick loop, so this drives a real <see cref="UmpkClient"/> over a real connection and counts. Without it every test above would pass against a counter nothing increments.</summary>
    [Fact]
    public async Task TheSessionTickCounter_AdvancesOnTheLiveTickLoop()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTicks();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = LiveClient(server, ticks);

        await JoinAsync(client, server, ct);

        long before = await client.InvokeAsync(c => c.State.SessionTick, ct);
        for (int i = 0; i < 40; i++)
            await ticks.FireAsync(ct);

        await WaitForAsync(
            async () => await client.InvokeAsync(c => c.State.SessionTick, ct) >= before + 40, ct);
        long after = await client.InvokeAsync(c => c.State.SessionTick, ct);
        Assert.Equal(before + 40, after);
    }

    // Support

    private static CommonPlayerSpawnInfo Spawn() => new(
        DimensionTypeId: 0, Dimension: "minecraft:the_nether", Seed: 0, GameType: 0, PreviousGameType: 0,
        IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);

    private static ApplierHarness Build(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Entities = true, Terrain = true });
        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;
        return harness;
    }

    private static JavaVersion LiveVersion => JavaVersions.V1_21_11;

    private static UmpkClient LiveClient(FakeJavaServer server, ITickSource ticks) =>
        new UmpkClientBuilder()
            .UseVersion(LiveVersion)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(LiveVersion.Version.Protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(f =>
            {
                f.Terrain = true;
                f.Physics = true;
                f.Pathfinding = false;
            })
            .Build();

    private static async Task JoinAsync(UmpkClient client, FakeJavaServer server, CancellationToken ct)
    {
        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, LiveVersion.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                    _ = await server.NextFrameAsync(ct).ConfigureAwait(false);

            }
            catch (Exception)
            {
                // The pipe closing at the end of the test is the expected way out.
            }
        }, ct);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await SendAsync(server, ProtocolPhase.Play, new ClientboundLoginPacket(
            PlayerId: SelfEntityId, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null), ct);
        await joined.Task.WaitAsync(Budget, ct);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        while (!await condition().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
        ProtocolDescriptor descriptor = LiveVersion.Protocol;
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    /// <summary>A tick source the test advances by hand, so a tick lands exactly where it is wanted.</summary>
    private sealed class ManualTicks : ITickSource
    {
        private readonly Channel<long> _ticks = Channel.CreateUnbounded<long>();
        private long _next;

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public IAsyncEnumerable<long> Ticks(CancellationToken cancellationToken = default) =>
            _ticks.Reader.ReadAllAsync(cancellationToken);

        public ValueTask FireAsync(CancellationToken ct) =>
            _ticks.Writer.WriteAsync(Interlocked.Increment(ref _next), ct);
    }
}
