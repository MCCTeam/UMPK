using System.Buffers;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Coverage for the session/state family: the server tick-rate estimate derived from game-time broadcasts, the keep-alive exchange, and the server brand.</summary>
public sealed class ServerStateApplierTests
{
    private static readonly JavaVersion Modern = JavaVersions.V1_21_5;

    private static ClientboundSetTimePacket Time(long gameTime) =>
        new(gameTime, DayTime: 6000, TickDayTime: false, ClockUpdates: []);

    /// <summary>A healthy server broadcasts 20 ticks of game time per wall-clock second which must read as 20 TPS.</summary>
    [Fact]
    public async Task TickRate_FullSpeed_Reads20()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(Time(1000));
        Assert.Null(harness.State.Server.TicksPerSecond);   // one sample is not an interval

        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(1020));

        Assert.Equal(20.0, harness.State.Server.TicksPerSecond!.Value, 3);
    }

    /// <summary>A server taking two seconds to advance 20 ticks is at half speed.</summary>
    [Fact]
    public async Task TickRate_HalfSpeed_Reads10()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromSeconds(2));
        await harness.ApplyAsync(Time(1020));

        Assert.Equal(10.0, harness.State.Server.TicksPerSecond!.Value, 3);
    }

    /// <summary>The 1.21.2+ pause-when-empty case. A paused server does not send a "0 ticks elapsed" update, it stops sending entirely, so the estimate must expire to unknown rather than sit on a stale value.</summary>
    [Fact]
    public async Task TickRate_PausedServer_ReadsUnknown_NotZero()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(1020));
        Assert.Equal(20.0, harness.State.Server.TicksPerSecond!.Value, 3);

        // The server pauses: no further broadcasts arrive at all.
        clock.Advance(ServerState.TickSampleLifetime + TimeSpan.FromSeconds(1));
        harness.State.Server.ExpireStaleTickRate(clock.GetTimestamp(), clock);

        Assert.Null(harness.State.Server.TicksPerSecond);
    }

    /// <summary>Before the lifetime elapses the last estimate still stands.</summary>
    [Fact]
    public async Task TickRate_ShortGap_KeepsTheEstimate()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(1020));

        clock.Advance(TimeSpan.FromSeconds(2));
        harness.State.Server.ExpireStaleTickRate(clock.GetTimestamp(), clock);

        Assert.NotNull(harness.State.Server.TicksPerSecond);
    }

    /// <summary>A frozen tick manager keeps broadcasting the same game time. A zero tick delta is an absence of information, not a measurement of zero, so it must not produce a 0 TPS reading.</summary>
    [Fact]
    public async Task TickRate_FrozenGameTime_NeverReadsZero()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(1000));

        Assert.Null(harness.State.Server.TicksPerSecond);
    }

    /// <summary>Game time going backwards (a reset or a dimension change) re-anchors instead of reading negative.</summary>
    [Fact]
    public async Task TickRate_BackwardsGameTime_ReAnchors()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(Time(5000));
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(10));
        Assert.Null(harness.State.Server.TicksPerSecond);

        // Re-anchored: the next honest interval measures normally.
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(30));
        Assert.Equal(20.0, harness.State.Server.TicksPerSecond!.Value, 3);
    }

    /// <summary>A server catching up after a stall cannot be reported above the vanilla ceiling.</summary>
    [Fact]
    public async Task TickRate_CatchUpBurst_ClampsTo20()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await harness.ApplyAsync(Time(1100));   // 100 ticks in 0.1s = 1000 TPS raw

        Assert.Equal(20.0, harness.State.Server.TicksPerSecond!.Value, 3);
    }

    /// <summary>The estimate must not depend on the Terrain feature or on a built world: the world applier is gated on both, which is why the sample is taken by the always-present connection applier.</summary>
    [Fact]
    public async Task TickRate_Works_WithTerrainDisabled()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, new ClientFeatures { Terrain = false }, clock);

        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(1020));

        Assert.Equal(20.0, harness.State.Server.TicksPerSecond!.Value, 3);
    }

    /// <summary>Recording the tick sample must not consume the packet: the world applier still owns world time.</summary>
    [Fact]
    public async Task TickRate_Sampling_DoesNotSwallowTheTimeUpdate()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null));

        await harness.ApplyAsync(Time(1000));

        Assert.Equal(6000, harness.State.World.TimeOfDay);
    }

    /// <summary>The response must echo the request id verbatim; a mismatch disconnects on vanilla.</summary>
    [Fact]
    public async Task KeepAlive_IsAnswered_AndRecorded()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(new ClientboundPlayKeepAlivePacket(987654321L));

        Assert.Contains(harness.Recorder.Packets, p => p is ServerboundPlayKeepAlivePacket { Id: 987654321L });
        Assert.Equal(987654321L, harness.State.Server.LastKeepAliveId);
        Assert.NotNull(harness.State.Server.KeepAliveTurnaround);
    }

    /// <summary>A vanilla server aims for a 15 second keep-alive cadence.</summary>
    [Fact]
    public async Task KeepAlive_Interval_IsMeasuredBetweenRequests()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(new ClientboundPlayKeepAlivePacket(1));
        Assert.Null(harness.State.Server.KeepAliveInterval);   // no interval from one sample

        clock.Advance(TimeSpan.FromSeconds(15));
        await harness.ApplyAsync(new ClientboundPlayKeepAlivePacket(2));

        Assert.Equal(TimeSpan.FromSeconds(15), harness.State.Server.KeepAliveInterval);
        Assert.Equal(2, harness.State.Server.LastKeepAliveId);
    }

    /// <summary>1.20.2+ send the brand during configuration and never during play, so the configuration payload is the only place a modern session can learn it.</summary>
    [Fact]
    public async Task Brand_ConfigurationPayload_IsDecoded()
    {
        var harness = new ApplierHarness(Modern);

        await harness.ApplyAsync(new ClientboundConfigCustomPayloadPacket(
            Identifier.Minecraft("brand"), BrandBody("Paper")));

        Assert.Equal("Paper", harness.State.Server.Brand);
    }

    /// <summary>A non-brand configuration payload must not be mistaken for one.</summary>
    [Fact]
    public async Task Brand_OtherConfigurationChannel_IsIgnored()
    {
        var harness = new ApplierHarness(Modern);

        await harness.ApplyAsync(new ClientboundConfigCustomPayloadPacket(
            Identifier.Minecraft("register"), BrandBody("Paper")));

        Assert.Null(harness.State.Server.Brand);
    }

    /// <summary>1.13-1.20.1 send the brand on the namespaced channel in the play phase.</summary>
    [Fact]
    public void Brand_PlayFrame_ModernChannel_IsRead()
    {
        byte[] frame = PlayCustomPayloadFrame("minecraft:brand", "Spigot");

        Assert.True(ServerBrandPayload.TryReadFromPlayFrame(frame, out string brand));
        Assert.Equal("Spigot", brand);
    }

    /// <summary>1.8-1.12.2 use the legacy <c>MC|Brand</c> channel, which is NOT a valid namespaced identifier and so can never route through the identifier-keyed plugin-channel table.</summary>
    [Fact]
    public void Brand_PlayFrame_LegacyChannel_IsRead()
    {
        byte[] frame = PlayCustomPayloadFrame("MC|Brand", "CraftBukkit");

        Assert.True(ServerBrandPayload.TryReadFromPlayFrame(frame, out string brand));
        Assert.Equal("CraftBukkit", brand);

        // Guard the reason this needs its own path: the legacy name is not parseable as an Identifier.
        Assert.False(Identifier.TryParse("MC|Brand", out _));
    }

    /// <summary>A different play channel must not be read as a brand.</summary>
    [Fact]
    public void Brand_PlayFrame_OtherChannel_IsIgnored()
    {
        byte[] frame = PlayCustomPayloadFrame("minecraft:register", "Spigot");

        Assert.False(ServerBrandPayload.TryReadFromPlayFrame(frame, out _));
    }

    /// <summary>A truncated payload must be rejected rather than throwing into the read loop.</summary>
    [Fact]
    public void Brand_PlayFrame_Truncated_IsRejected()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteString("minecraft:brand");
        w.WriteVarInt(64);   // claims 64 bytes of brand that are not there

        Assert.False(ServerBrandPayload.TryReadFromPlayFrame(buffer.WrittenSpan, out _));
    }

    /// <summary>A reconnect must not present the previous server's brand or tick rate as current.</summary>
    [Fact]
    public async Task SessionEnd_ClearsServerState()
    {
        var clock = new ManualTimeProvider();
        var harness = new ApplierHarness(Modern, time: clock);

        await harness.ApplyAsync(new ClientboundConfigCustomPayloadPacket(
            Identifier.Minecraft("brand"), BrandBody("Paper")));
        await harness.ApplyAsync(Time(1000));
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.ApplyAsync(Time(1020));
        await harness.ApplyAsync(new ClientboundPlayKeepAlivePacket(7));

        Assert.NotNull(harness.State.Server.Brand);
        Assert.NotNull(harness.State.Server.TicksPerSecond);

        harness.State.Server.ResetForSessionEnd();

        Assert.Null(harness.State.Server.Brand);
        Assert.Null(harness.State.Server.TicksPerSecond);
        Assert.Null(harness.State.Server.LastKeepAliveId);
        Assert.Null(harness.State.Server.KeepAliveInterval);
        Assert.Null(harness.State.Server.KeepAliveTurnaround);
        Assert.Null(harness.State.Server.ObservedLatency);
    }

    /// <summary>The brand body is a single length-prefixed UTF-8 string.</summary>
    private static byte[] BrandBody(string brand)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteString(brand);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A play-phase custom_payload frame body: channel string then the payload.</summary>
    private static byte[] PlayCustomPayloadFrame(string channel, string brand)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteString(channel);
        w.WriteString(brand);
        return buffer.WrittenSpan.ToArray();
    }
}
