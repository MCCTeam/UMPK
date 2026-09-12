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

/// <summary>The server-teleport to physics-engine seam. The physics engine steps from its own position and writes the result back into self state every tick, so a teleport that is not pushed into the engine survives less than one tick: the engine was seeded at world install, when self position was still the origin, and the next tick restored the origin. That made the client report 0/0/0 for its own position in every configuration that had movement enabled.</summary>
/// <remarks>These drive the real chain end to end: the frame is encoded and decoded through the codec the bound version catalog binds for the era, dispatched into the real applier catalog, and then stepped with a real <see cref="PhysicsEngineHolder"/> wired to the same seam <c>UmpkClient</c> wires live. A test that only asserted self state right after the apply would pass against the broken build, because the overwrite happens on the following tick.</remarks>
public sealed class PositionResyncTests
{
    // One protocol per clientbound player_position wire form: 47 is the 1.8 flag-byte form, 340 and 477 the 1.9 form with a teleport id, 578 the same across the 1.15 spawn rework, 755 the 1.17 form with the trailing dismount bool, 762 after that bool was removed, and 772 the 1.21.2 teleport-id-first PositionMoveRotation form.
    public static TheoryData<int> Protocols => [47, 340, 477, 578, 755, 762, 772];

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task Teleport_ReSeeds_The_Engine_And_The_Position_Survives_The_Next_Tick(int protocol)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(protocol);
        await using (loop)
        {
            object teleport = BoundDescriptorCodec.RoundTrip(
                protocol, "player_position", Teleport(protocol, 128.5, 71.0, -64.5, 90f, 12f, flags: 0));
            await harness.ApplyAsync(teleport);

            Assert.Equal(new Vec3d(128.5, 71.0, -64.5), harness.State.Self.Position);
            Assert.Equal(1, harness.PositionResyncCount);

            // Exactly what UmpkClient.OnTickOnLoop does once per tick.
            holder.TickIdle();

            // The engine free-falls in the empty test world, so Y takes one gravity step. The horizontal axes must not move at all, and nothing may snap back to the origin.
            Assert.Equal(128.5, harness.State.Self.Position.X, 6);
            Assert.Equal(-64.5, harness.State.Self.Position.Z, 6);
            Assert.True(
                Math.Abs(harness.State.Self.Position.Y - 71.0) < 1.0,
                $"Y drifted off the teleport: {harness.State.Self.Position.Y}");
        }
    }

    /// <summary>Relative axes must offset the tracked position rather than replace it, and the engine must be re-seeded at the RESOLVED absolute position. Seeding from the raw packet fields would put the engine at the offset itself (here 3/8/-2) and the next tick would drag self state there, which is a worse failure than the one being fixed because the position would look plausible.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task Relative_Teleport_Offsets_Rather_Than_Replaces(int protocol)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(protocol);
        await using (loop)
        {
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
                protocol, "player_position", Teleport(protocol, 100, 70, 50, 10f, 5f, flags: 0)));
            Assert.Equal(new Vec3d(100, 70, 50), harness.State.Self.Position);

            // X, Y, Z and yaw relative (0x01|0x02|0x04|0x08); pitch absolute.
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
                protocol, "player_position", Teleport(protocol, 3, 8, -2, 45f, 30f, flags: 0x0F)));

            Assert.Equal(new Vec3d(103, 78, 48), harness.State.Self.Position);
            Assert.Equal(55f, harness.State.Self.Yaw);
            Assert.Equal(30f, harness.State.Self.Pitch);

            holder.TickIdle();

            Assert.Equal(103, harness.State.Self.Position.X, 6);
            Assert.Equal(48, harness.State.Self.Position.Z, 6);
            Assert.True(
                Math.Abs(harness.State.Self.Position.Y - 78.0) < 1.0,
                $"Y drifted off the relative teleport: {harness.State.Self.Position.Y}");
        }
    }

    /// <summary>The join case that made this user-visible: the world is installed (which seeds the engine at the origin) and the server's initial synchronise-position lands after it. Ten idle ticks later the client must still report the server's position, not the origin.</summary>
    [Fact]
    public async Task Join_Teleport_Still_Holds_After_Many_Ticks()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(772);
        await using (loop)
        {
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
                772, "player_position", Teleport(772, -220.5, 64.0, 331.5, 180f, 0f, flags: 0)));

            for (int i = 0; i < 10; i++)
                holder.TickIdle();

            Assert.Equal(-220.5, harness.State.Self.Position.X, 6);
            Assert.Equal(331.5, harness.State.Self.Position.Z, 6);
            Assert.NotEqual(Vec3d.Zero, harness.State.Self.Position);
        }
    }

    private static ClientboundPlayerPositionPacket Teleport(
        int protocol, double x, double y, double z, float yaw, float pitch, byte flags)
    {
        // From 1.21.2 the wire carries a PositionMoveRotation (position, delta movement, rotation) instead of loose fields, so the record has to be built in that shape for the era's encoder.
        PositionMoveRotation? modern = protocol >= 768
            ? new PositionMoveRotation(new Vec3d(x, y, z), Vec3d.Zero, yaw, pitch)
            : null;
        return new ClientboundPlayerPositionPacket(x, y, z, yaw, pitch, flags, TeleportId: 7, ModernValues: modern);
    }

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

        // Login installs the world; UmpkClient creates the engine right there, while self position is still the origin. That ordering is the whole bug, so the test reproduces it rather than seeding the engine after the teleport.
        await JoinAsync(harness);
        holder.EnsureEngine();
        Assert.Equal(Vec3d.Zero, harness.State.Self.Position);

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
