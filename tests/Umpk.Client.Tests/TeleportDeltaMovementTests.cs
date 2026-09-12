using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// Teleports carry momentum through the applier and into the physics engine.
/// <para>A 1.21.2+ <c>player_position</c> carries a <c>PositionMoveRotation</c> whose <c>deltaMovement</c> is on the wire. UMPK must resolve it alongside x/y/z/yaw/pitch; otherwise it touched the delta, so a knockback, a launch or an end-portal exit arrived with the momentum silently dropped. The pre-1.21.2 wire has no delta on it, but the component is KEPT when its axis is relative and ZEROED when it is absolute.</para>
/// <para>Every case goes through the codec the catalog binds for the era, so the delta must survive the wire as well as the applier.</para>
/// </summary>
public sealed class TeleportDeltaMovementTests
{
    private const int ModernProtocol = 770;
    private const int LegacyProtocol = 762;

    // Vanilla Relative bit indices.
    private const int RelX = 1 << 0;
    private const int RelY = 1 << 1;
    private const int RelZ = 1 << 2;
    private const int RelYaw = 1 << 3;
    private const int RelDeltaX = 1 << 5;
    private const int RelDeltaY = 1 << 6;
    private const int RelDeltaZ = 1 << 7;
    private const int RelRotateDelta = 1 << 8;

    /// <summary>With no <c>DELTA_*</c> bit set, every axis takes the packet's delta outright, replacing whatever the client had. This is <c>calculateDelta</c>'s absolute branch.</summary>
    [Fact]
    public async Task ModernTeleport_AbsoluteDelta_ReplacesTheVelocity()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(ModernProtocol);
        await using (loop)
        {
            harness.State.Self.Velocity = new Vec3d(9, 9, 9);

            await ApplyAsync(harness, ModernProtocol, Modern(
                position: new Vec3d(10, 70, -10), delta: new Vec3d(0.25, -0.5, 0.125),
                yaw: 0f, pitch: 0f, flags: 0));

            Assert.Equal(new Vec3d(0.25, -0.5, 0.125), harness.State.Self.Velocity);
            _ = holder;
        }
    }

    /// <summary>With the <c>DELTA_*</c> bits set, the packet's delta is ADDED to what the client already had, which is how a server nudges momentum without knowing it.</summary>
    [Fact]
    public async Task ModernTeleport_RelativeDelta_AddsToTheVelocity()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(ModernProtocol);
        await using (loop)
        {
            harness.State.Self.Velocity = new Vec3d(1.0, 2.0, 3.0);

            await ApplyAsync(harness, ModernProtocol, Modern(
                position: new Vec3d(10, 70, -10), delta: new Vec3d(0.5, 0.25, -1.0),
                yaw: 0f, pitch: 0f, flags: RelDeltaX | RelDeltaY | RelDeltaZ));

            Assert.Equal(new Vec3d(1.5, 2.25, 2.0), harness.State.Self.Velocity);
            _ = holder;
        }
    }

    /// <summary>
    /// <c>ROTATE_DELTA</c> (bit 8) rotates the CURRENT delta by the rotation change before the per-axis combine. The case is picked so the answer is analytic rather than a restatement of the code: a velocity of +1 on X, rotated by a yaw change of +90 degrees, must come out as +1 on Z.
    /// <para>The encoded bit 8 must survive decoding and reach the applier as <c>ROTATE_DELTA</c>.</para>
    /// </summary>
    [Fact]
    public async Task ModernTeleport_RotateDelta_RotatesTheCarriedVelocity()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(ModernProtocol);
        await using (loop)
        {
            harness.State.Self.Velocity = new Vec3d(1.0, 0.0, 0.0);
            harness.State.Self.Yaw = 0f;
            harness.State.Self.Pitch = 0f;

            await ApplyAsync(harness, ModernProtocol, Modern(
                position: new Vec3d(0, 70, 0), delta: Vec3d.Zero, yaw: 90f, pitch: 0f,
                flags: RelRotateDelta | RelDeltaX | RelDeltaY | RelDeltaZ));

            Assert.Equal(0.0, harness.State.Self.Velocity.X, 6);
            Assert.Equal(0.0, harness.State.Self.Velocity.Y, 6);
            Assert.Equal(1.0, harness.State.Self.Velocity.Z, 6);

            // Control: the identical frame WITHOUT bit 8 keeps the velocity on X.
            harness.State.Self.Velocity = new Vec3d(1.0, 0.0, 0.0);
            harness.State.Self.Yaw = 0f;
            await ApplyAsync(harness, ModernProtocol, Modern(
                position: new Vec3d(0, 70, 0), delta: Vec3d.Zero, yaw: 90f, pitch: 0f,
                flags: RelDeltaX | RelDeltaY | RelDeltaZ));

            Assert.Equal(1.0, harness.State.Self.Velocity.X, 6);
            Assert.Equal(0.0, harness.State.Self.Velocity.Z, 6);
            _ = holder;
        }
    }

    /// <summary>The resolved velocity has to reach the ENGINE, not just self state: the engine steps from its own fields and writes the result back every tick, so updating self state alone is insufficient.</summary>
    [Fact]
    public async Task ModernTeleport_Velocity_ReachesThePhysicsEngine()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(ModernProtocol);
        await using (loop)
        {
            await ApplyAsync(harness, ModernProtocol, Modern(
                position: new Vec3d(0, 70, 0), delta: new Vec3d(0.5, 0, 0), yaw: 0f, pitch: 0f, flags: 0));
            Assert.Equal(0.5, harness.State.Self.Velocity.X, 6);

            double before = harness.State.Self.Position.X;
            holder.TickIdle();

            // Air drag is 0.91 per tick, so the step is about 0.455. The broken build seeded the engine with a zeroed velocity and this axis did not move at all.
            Assert.True(
                harness.State.Self.Position.X - before > 0.3,
                $"the engine did not carry the teleport velocity: X moved {harness.State.Self.Position.X - before}");
        }
    }

    /// <summary>Pre-1.21.2: no delta rides the wire, and vanilla keeps the component whose axis is relative while zeroing the ones that are absolute.</summary>
    [Fact]
    public async Task LegacyTeleport_KeepsRelativeAxesAndZeroesAbsoluteOnes()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(LegacyProtocol);
        await using (loop)
        {
            harness.State.Self.Velocity = new Vec3d(1.0, 2.0, 3.0);

            // X and Z relative, Y absolute.
            await ApplyAsync(harness, LegacyProtocol, Legacy(
                new Vec3d(4, 70, 8), yaw: 0f, pitch: 0f, flags: RelX | RelZ | RelYaw));

            Assert.Equal(new Vec3d(1.0, 0.0, 3.0), harness.State.Self.Velocity);
            _ = holder;
        }
    }

    /// <summary>The modern path clamps the resolved pitch to [-90, 90], which vanilla's the legacy path does not.</summary>
    [Fact]
    public async Task ModernTeleport_ClampsThePitch()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(ModernProtocol);
        await using (loop)
        {
            await ApplyAsync(harness, ModernProtocol, Modern(
                position: new Vec3d(0, 70, 0), delta: Vec3d.Zero, yaw: 0f, pitch: 140f, flags: 0));

            Assert.Equal(90f, harness.State.Self.Pitch);
            _ = holder;
        }
    }

    private static ValueTask ApplyAsync(ApplierHarness harness, int protocol, ClientboundPlayerPositionPacket packet) =>
        harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "player_position", packet));

    private static ClientboundPlayerPositionPacket Modern(Vec3d position, Vec3d delta, float yaw, float pitch, int flags) =>
        new(position.X, position.Y, position.Z, yaw, pitch, flags, TeleportId: 7,
            ModernValues: new PositionMoveRotation(position, delta, yaw, pitch));

    private static ClientboundPlayerPositionPacket Legacy(Vec3d position, float yaw, float pitch, int flags) =>
        new(position.X, position.Y, position.Z, yaw, pitch, flags, TeleportId: 7, ModernValues: null);

    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync(
        int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Physics = true, Entities = true });
        var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };

        var holder = new PhysicsEngineHolder(services, new FlagsBlockShapes(), NullLogger.Instance);
        harness.PositionResync = holder.ResyncPosition;

        await JoinAsync(harness);
        holder.EnsureEngine();
        return (harness, holder, scheduler);
    }

    private static async Task JoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        var join = new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
        await harness.ApplyAsync(join);
    }
}
