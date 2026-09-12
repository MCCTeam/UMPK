using Umpk;
using Umpk.Client;
using Umpk.Client.Snapshots;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Snapshots;

/// <summary><see cref="SelfSnapshot"/>, <see cref="AbilitiesSnapshot"/>, <see cref="EffectSnapshot"/> and <see cref="ClientSnapshots"/> form the off-loop self read-model. A client that has not been placed in the world must not report its origin/Survival defaults as a reading.</summary>
public sealed class SelfSnapshotTests
{
    // SelfSnapshot.Project

    [Fact]
    public void Self_ProjectsEveryTrackedField()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        SelfState self = state.Self;
        self.EntityId = 7;
        self.Uuid = Guid.NewGuid();
        self.Username = "Tester";
        self.Position = new Vec3d(1, 2, 3);
        self.Velocity = new Vec3d(0.1, 0.2, 0.3);
        self.Yaw = 90f;
        self.Pitch = -10f;
        self.OnGround = false;
        self.Health = 15f;
        self.Food = 18;
        self.Saturation = 3.5f;
        self.ExperienceLevel = 5;
        self.ExperienceProgress = 0.25f;
        self.TotalExperience = 123;
        self.HeldSlot = 4;
        self.GameMode = GameMode.Creative;
        self.Sneaking = true;
        self.Sprinting = true;
        self.HasSpawned = true;

        SelfSnapshot snapshot = SelfSnapshot.Project(state);

        Assert.Equal(self.EntityId, snapshot.EntityId);
        Assert.Equal(self.Uuid, snapshot.Uuid);
        Assert.Equal(self.Username, snapshot.Username);
        Assert.Equal(self.Position, snapshot.Position);
        Assert.Equal(self.Velocity, snapshot.Velocity);
        Assert.Equal(self.Yaw, snapshot.Yaw);
        Assert.Equal(self.Pitch, snapshot.Pitch);
        Assert.Equal(self.OnGround, snapshot.OnGround);
        Assert.Equal(self.Health, snapshot.Health);
        Assert.Equal(self.Food, snapshot.Food);
        Assert.Equal(self.Saturation, snapshot.Saturation);
        Assert.Equal(self.ExperienceLevel, snapshot.ExperienceLevel);
        Assert.Equal(self.ExperienceProgress, snapshot.ExperienceProgress);
        Assert.Equal(self.TotalExperience, snapshot.TotalExperience);
        Assert.Equal(self.HeldSlot, snapshot.HeldSlot);
        Assert.Equal(self.GameMode, snapshot.GameMode);
        Assert.Equal(self.Sneaking, snapshot.Sneaking);
        Assert.Equal(self.Sprinting, snapshot.Sprinting);
        Assert.Equal(self.HasSpawned, snapshot.HasSpawned);
    }

    [Fact]
    public void HasSpawned_IsFalse_BeforeTheInitialTeleport()
    {
        // A fresh tracker, with no play packets applied at all: the join packet has not run, let alone the teleport that actually places the player.
        var harness = new ApplierHarness(JavaVersions.V1_21_11);

        Assert.False(SelfSnapshot.Project(harness.State).HasSpawned);
    }

    [Fact]
    public async Task HasSpawned_IsTrue_AfterTheInitialTeleport()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_11);

        await harness.ApplyAsync(Join());
        await harness.ApplyAsync(Teleport());

        Assert.True(SelfSnapshot.Project(harness.State).HasSpawned);
    }

    /// <summary>The tracker's defaults (origin, Survival) must never be mistaken for a reading when unspawned.</summary>
    [Fact]
    public void Self_DefaultsAreNotMarkedAsAReading()
    {
        var state = new ClientState(new ClientFeatures().Normalized());

        SelfSnapshot snapshot = SelfSnapshot.Project(state);

        Assert.False(snapshot.HasSpawned);
        Assert.Equal(Vec3d.Zero, snapshot.Position);
        Assert.Equal(GameMode.Survival, snapshot.GameMode);
    }

    [Fact]
    public void EyePosition_IsFeetPlusStandingEyeHeight()
    {
        var snapshot = new SelfSnapshot(
            0, Guid.Empty, string.Empty, new Vec3d(10, 64, -3), Vec3d.Zero, 0f, 0f, true,
            20f, 20, 0f, 300, 300, 0, 0f, 0, 0, GameMode.Survival, false, false, true);

        Assert.Equal(PlayerReach.EyePosition(snapshot.Position), snapshot.EyePosition);
        Assert.Equal(64 + PlayerReach.StandingEyeHeight, snapshot.EyePosition.Y);
    }

    [Theory]
    [InlineData(GameMode.Spectator, true)]
    [InlineData(GameMode.Survival, false)]
    public void IsSpectator_ReflectsGameMode(GameMode mode, bool expected)
    {
        var snapshot = new SelfSnapshot(
            0, Guid.Empty, string.Empty, Vec3d.Zero, Vec3d.Zero, 0f, 0f, true,
            20f, 20, 0f, 300, 300, 0, 0f, 0, 0, mode, false, false, true);

        Assert.Equal(expected, snapshot.IsSpectator);
    }

    // AbilitiesSnapshot.Project

    [Fact]
    public void Abilities_ProjectsEveryField()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        SelfState self = state.Self;
        self.Flying = true;
        self.MayFly = true;
        self.Invulnerable = true;
        self.InstantBuild = true;
        self.FlyingSpeed = 0.1f;
        self.WalkingSpeed = 0.2f;

        AbilitiesSnapshot snapshot = AbilitiesSnapshot.Project(self);

        Assert.True(snapshot.Flying);
        Assert.True(snapshot.MayFly);
        Assert.True(snapshot.Invulnerable);
        Assert.True(snapshot.InstantBuild);
        Assert.Equal(0.1f, snapshot.FlyingSpeed);
        Assert.Equal(0.2f, snapshot.WalkingSpeed);
    }

    // EffectSnapshot.Project

    [Fact]
    public void Effects_AmplifierZero_ProjectsAsLevelOne()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.ApplyEffect(new ActiveEffect(EffectId: 1, Amplifier: 0, Duration: 200, Flags: 0));

        IReadOnlyList<EffectSnapshot> effects = EffectSnapshot.Project(state);

        EffectSnapshot effect = Assert.Single(effects);
        Assert.Equal(0, effect.Amplifier);
        Assert.Equal(1, effect.Level);
    }

    [Fact]
    public void Effects_AmplifierTwo_ProjectsAsLevelThree()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.ApplyEffect(new ActiveEffect(EffectId: 1, Amplifier: 2, Duration: 200, Flags: 0));

        EffectSnapshot effect = Assert.Single(EffectSnapshot.Project(state));

        Assert.Equal(2, effect.Amplifier);
        Assert.Equal(3, effect.Level);
    }

    [Fact]
    public void Effects_NegativeDuration_IsInfinite()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.ApplyEffect(new ActiveEffect(EffectId: 1, Amplifier: 0, Duration: -1, Flags: 0));

        EffectSnapshot effect = Assert.Single(EffectSnapshot.Project(state));

        Assert.Equal(-1, effect.Duration);
        Assert.True(effect.IsInfinite);
    }

    [Theory]
    [InlineData((byte)0x01, true, false, false)]
    [InlineData((byte)0x02, false, true, false)]
    [InlineData((byte)0x04, false, false, true)]
    public void Effects_FlagBits_ProjectIndependently(byte flags, bool ambient, bool particles, bool icon)
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.ApplyEffect(new ActiveEffect(EffectId: 1, Amplifier: 0, Duration: 200, Flags: flags));

        EffectSnapshot effect = Assert.Single(EffectSnapshot.Project(state));

        Assert.Equal(ambient, effect.IsAmbient);
        Assert.Equal(particles, effect.ShowParticles);
        Assert.Equal(icon, effect.ShowIcon);
    }

    /// <summary>An effect whose id no registry is loaded to resolve is still reported, never dropped.</summary>
    [Fact]
    public void Effects_UnnamedEffect_KeepsItsNetworkIdAndIsStillReported()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.ApplyEffect(new ActiveEffect(EffectId: 999, Amplifier: 0, Duration: 200, Flags: 0));

        EffectSnapshot effect = Assert.Single(EffectSnapshot.Project(state));

        Assert.Equal(999, effect.NetworkId);
        Assert.Contains("999", effect.EffectId.Path, StringComparison.Ordinal);
    }

    // ClientSnapshots marshalling

    [Fact]
    public async Task SnapshotsAsync_MarshalsOntoTheSessionLoop()
    {
        await using var inner = new ChannelSessionScheduler();
        var scheduler = new RecordingScheduler(inner);
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(JavaVersions.V1_21_11)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseScheduler(scheduler)
            .Build();

        // The calling thread is never on the injected scheduler's loop: this is the caller-side half of the proof (a thread-id comparison alone is not, because the thread pool can hand the resumed continuation back the very same thread the loop happens to run on).
        Assert.False(scheduler.IsCurrent);

        SelfSnapshot snapshot = await client.Snapshots.SelfAsync();

        Assert.NotNull(snapshot);

        // The load-bearing half: recorded from INSIDE the delegate UmpkClient.InvokeAsync handed to the scheduler, so it is true only if that delegate actually ran on the injected scheduler's own drain loop (ChannelSessionScheduler.IsCurrent is an AsyncLocal scoped to that exact instance), not merely on some thread that happens to share a pool-recycled id with the caller.
        Assert.True(scheduler.WorkRanOnTheInjectedLoop);
    }

    private static ClientboundLoginPacket Join()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        return new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
    }

    private static ClientboundPlayerPositionPacket Teleport()
    {
        var modern = new PositionMoveRotation(new Vec3d(10.5, 65.0, -12.5), Vec3d.Zero, YRot: 90f, XRot: 0f);
        return new ClientboundPlayerPositionPacket(
            10.5, 65.0, -12.5, Yaw: 90f, Pitch: 0f, RelativeFlags: 0, TeleportId: 7, ModernValues: modern);
    }

    /// <summary>Wraps a real <see cref="ChannelSessionScheduler"/> and records, from INSIDE the work delegate, that the inner scheduler's own <see cref="ISessionScheduler.IsCurrent"/> (an <c>AsyncLocal</c> scoped to that exact instance) was true while it ran. A thread-id comparison would not do: the shared thread pool can hand a caller's resumed continuation the very id the loop happens to run on.</summary>
    private sealed class RecordingScheduler(ISessionScheduler inner) : ISessionScheduler
    {
        public bool WorkRanOnTheInjectedLoop { get; private set; }

        public bool IsCurrent => inner.IsCurrent;

        public void Post(Action work) => inner.Post(work);

        public Task InvokeAsync(Action work, CancellationToken cancellationToken) => inner.InvokeAsync(work, cancellationToken);

        public Task<TResult> InvokeAsync<TResult>(Func<TResult> work, CancellationToken cancellationToken) =>
            inner.InvokeAsync(
                () =>
                {
                    WorkRanOnTheInjectedLoop = inner.IsCurrent;
                    return work();
                }, cancellationToken);

        public Task InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken) => inner.InvokeAsync(work, cancellationToken);

        public Task<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> work, CancellationToken cancellationToken) =>
            inner.InvokeAsync(work, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
