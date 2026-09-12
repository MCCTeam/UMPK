using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// Server-applied velocity: <c>set_entity_motion</c> aimed at the LOCAL player.
/// <para>Knockback from a mob or player, explosions, bounces, and fishing-rod pulls use this packet. Two conditions must hold:</para>
/// <list type="number">
/// <item><description>
/// The applier resolved the target with <c>EntityStore.TryGet</c> only. The local player is not in that store because <c>SelfState</c> is separate. Frames for the local entity ID must update self state directly.
/// </description></item>
/// <item><description>
/// From protocol 773 the three short components are not on the wire at all. 1.21.9 replaced them with the low-precision quantized block (<c>LpVec3</c>) and the bound codec parks it raw in <c>ModernVelocityRaw</c> while constructing the shorts as literal zeroes, so <c>VelocityX / 8000.0</c> evaluates to exactly zero on 1.21.9, 1.21.10, 1.21.11, 26.1 and 26.2. Fixing the routing alone still delivers zero knockback on those protocols.
/// </description></item>
/// </list>
/// <para>The load-bearing assertions here are the ones that step the PHYSICS ENGINE. A test that asserted <c>SelfState.Velocity</c> changed would pass with the engine still ignoring it, because the engine steps from its own fields and writes the result back over self state every tick.</para>
/// </summary>
public sealed class SelfVelocityTests
{
    /// <summary>A protocol 775 clientbound <c>set_entity_motion</c> payload for a <c>minecraft:player_attack</c> knockback. It contains VarInt entity id 1531 followed by the six-byte <c>LpVec3</c> block.</summary>
    private const string LiveKnockbackFrame = "fb0b31337fff5c59";

    /// <summary>The <c>LpVec3</c> block of <see cref="LiveKnockbackFrame"/>, without the entity id.</summary>
    private static readonly byte[] LiveKnockbackBlock = Convert.FromHexString("31337fff5c59");

    private const int LiveKnockbackEntityId = 1531;

    // Vanilla knockback is 0.4 horizontal and 0.4 * 0.9 vertical off a base of 0.4; the quantization is what makes these not round numbers, and they are the values vanilla's own read behavior produces.
    private const double LiveKnockbackX = -0.39998779222364644;
    private const double LiveKnockbackY = 0.36080083012879194;

    private const int ModernProtocol = 775;
    private const int ShortEraProtocol = 762;
    private const int SolidStateId = 1;
    private const int SelfEntityId = 1;

    /// <summary>The captured frame, decoded by the codec the catalog binds at protocol 775, carries the knockback velocity in its raw block and zero in all three short components.</summary>
    [Fact]
    public void LiveCaptured26_1Frame_CarriesItsVelocityOnlyInTheRawBlock()
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(ModernProtocol, "set_entity_motion");
        var context = new PacketCodecContext(JavaGameData.Registries(ModernProtocol), IConnectionCodecState.Empty);
        var packet = (ClientboundSetEntityMotionPacket)codec.Decode(
            Convert.FromHexString(LiveKnockbackFrame), context);

        Assert.Equal(LiveKnockbackEntityId, packet.EntityId);
        Assert.Equal(0, packet.VelocityX);
        Assert.Equal(0, packet.VelocityY);
        Assert.Equal(0, packet.VelocityZ);
        Assert.NotNull(packet.ModernVelocityRaw);
        Assert.Equal(LiveKnockbackBlock, packet.ModernVelocityRaw);

        Vec3d decoded = LowPrecisionVelocity.Decode(packet.ModernVelocityRaw!);
        Assert.Equal(LiveKnockbackX, decoded.X, 12);
        Assert.Equal(LiveKnockbackY, decoded.Y, 12);
        Assert.Equal(0.0, decoded.Z, 12);
    }

    /// <summary>The whole live frame, through the bound codec and the applier chain, on a client whose own entity id is the one the frame names. The velocity has to land on self state rather than be dropped by an entity-store miss.</summary>
    [Fact]
    public async Task LiveCaptured26_1Frame_AppliesToSelfWhenItNamesTheClientsOwnEntityId()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(ModernProtocol, selfEntityId: LiveKnockbackEntityId);
        await using (loop)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(ModernProtocol, "set_entity_motion");
            var context = new PacketCodecContext(JavaGameData.Registries(ModernProtocol), IConnectionCodecState.Empty);
            await harness.ApplyAsync(codec.Decode(Convert.FromHexString(LiveKnockbackFrame), context));

            Assert.Equal(LiveKnockbackX, harness.State.Self.Velocity.X, 12);
            Assert.Equal(LiveKnockbackY, harness.State.Self.Velocity.Y, 12);
            Assert.Equal(0.0, harness.State.Self.Velocity.Z, 12);
            Assert.Equal(1, harness.VelocityPushCount);
            _ = holder;
        }
    }

    /// <summary>
    /// The captured 26.1 knockback must move the player, which means it has to be inside the physics engine when the next tick runs. The player stands on a floor and is hit; twenty ticks later the server-visible position has to be somewhere else.
    /// <para>Deliberately not an assertion on <c>SelfState.Velocity</c>: that field is overwritten from the engine's own state on every tick, so it can hold the right number while nothing moves at all, so observable movement is the required assertion.</para>
    /// </summary>
    [Fact]
    public async Task ModernKnockback_MovesThePlayer_ThroughThePhysicsEngine()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(ModernProtocol, selfEntityId: SelfEntityId);
        await using (loop)
        {
            StandOnFloor(harness, holder);
            Vec3d before = harness.State.Self.Position;

            await harness.ApplyAsync(RoundTrip(ModernProtocol, SelfEntityId, -3200, 2880, 0, LiveKnockbackBlock));

            // The vertical component is a hop, so its evidence is the PEAK: a 0.36 launch is back on the floor well inside twenty ticks, and a test that only looked at the end would see y = 65 whether or not the engine ever got the velocity.
            double peakY = harness.State.Self.Position.Y;
            for (int i = 0; i < 20; i++)
            {
                holder.TickIdle();
                peakY = Math.Max(peakY, harness.State.Self.Position.Y);
            }

            double moved = before.X - harness.State.Self.Position.X;
            Assert.True(moved > 0.3, $"the knockback never reached the engine: X moved {moved}");
            Assert.True(
                peakY > before.Y + 0.3,
                $"the upward component never reached the engine: peak Y was {peakY}, floor is {before.Y}");
        }
    }

    /// <summary>The same proof on the three-short wire form covers protocols 1.8 through 1.21.8. send <c>value * 8000</c> as shorts, clamped SERVER-side to +/-3.9 in both the legacy and modern layouts <c>ClientboundSetEntityMotionPacket</c>), which is why the client applies no clamp of its own.</summary>
    [Fact]
    public async Task ShortWireLayoutKnockback_MovesThePlayer_ThroughThePhysicsEngine()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(ShortEraProtocol, selfEntityId: SelfEntityId);
        await using (loop)
        {
            StandOnFloor(harness, holder);
            Vec3d before = harness.State.Self.Position;

            await harness.ApplyAsync(RoundTrip(ShortEraProtocol, SelfEntityId, -3200, 2880, 0, LiveKnockbackBlock));

            Assert.Equal(-0.4, harness.State.Self.Velocity.X, 6);
            Assert.Equal(0.36, harness.State.Self.Velocity.Y, 6);
            Assert.Equal(1, harness.VelocityPushCount);

            Tick(holder, 20);
            double moved = before.X - harness.State.Self.Position.X;
            Assert.True(moved > 0.3, $"the knockback never reached the engine: X moved {moved}");
        }
    }

    /// <summary>A control that separates "the engine was pushed" from "the engine was reset". A knockback must not clear the fall the player is already in: applying delta movement alone preserves fall distance. Routing the push through <c>PhysicsPositionDirty</c> (which calls <c>PlayerPhysics.Reset</c>) would zero it, and this is the only thing that would notice.</summary>
    [Fact]
    public async Task Knockback_DoesNotResetTheFallInProgress()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(ModernProtocol, selfEntityId: SelfEntityId);
        await using (loop)
        {
            // No floor: fall for a while so the engine has accumulated a real fall distance.
            harness.State.Self.Position = new Vec3d(0.5, 200.0, 0.5);
            harness.State.Self.Velocity = Vec3d.Zero;
            holder.ResyncPosition();
            Tick(holder, 20);

            double fallBefore = holder.EngineState!.Value.FallDistance;
            Assert.True(fallBefore > 1.0, $"the test never got the player falling (fall distance {fallBefore})");

            await harness.ApplyAsync(RoundTrip(ModernProtocol, SelfEntityId, -3200, 0, 0, LiveKnockbackBlock));

            PhysicsState after = holder.EngineState!.Value;
            Assert.True(
                after.FallDistance >= fallBefore,
                $"the velocity push reset the engine: fall distance went {fallBefore} -> {after.FallDistance}");
            Assert.Equal(LiveKnockbackX, after.Velocity.X, 12);
        }
    }

    /// <summary>The self routing must not cost the entity-store path anything: a frame for a TRACKED entity still lands on that entity, does not touch self, and does not push the physics engine.</summary>
    [Fact]
    public async Task Motion_ForATrackedEntity_StillLandsOnThatEntity()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(ModernProtocol, selfEntityId: SelfEntityId);
        await using (loop)
        {
            const int OtherId = 4242;
            await harness.ApplyAsync(new ClientboundAddEntityPacket(
                OtherId, Guid.NewGuid(), TypeId: 0, X: 1, Y: 70, Z: 1,
                XRot: 0, YRot: 0, YHeadRot: 0, Data: 0,
                VelocityX: 0, VelocityY: 0, VelocityZ: 0, ModernVelocityRaw: null));
            Assert.True(harness.State.Entities.TryGet(OtherId, out Entity? entity));

            harness.State.Self.Velocity = Vec3d.Zero;
            await harness.ApplyAsync(RoundTrip(ModernProtocol, OtherId, -3200, 2880, 0, LiveKnockbackBlock));

            Assert.Equal(LiveKnockbackX, entity!.Velocity.X, 12);
            Assert.Equal(LiveKnockbackY, entity.Velocity.Y, 12);
            Assert.Equal(Vec3d.Zero, harness.State.Self.Velocity);
            Assert.Equal(0, harness.VelocityPushCount);
            _ = holder;
        }
    }

    /// <summary>A frame for an entity nobody is tracking stays a no-op, and never leaks onto self.</summary>
    [Fact]
    public async Task Motion_ForAnUntrackedEntity_IsANoOp()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(ModernProtocol, selfEntityId: SelfEntityId);
        await using (loop)
        {
            harness.State.Self.Velocity = Vec3d.Zero;
            await harness.ApplyAsync(RoundTrip(ModernProtocol, 999_999, -3200, 2880, 0, LiveKnockbackBlock));

            Assert.Equal(Vec3d.Zero, harness.State.Self.Velocity);
            Assert.Equal(0, harness.VelocityPushCount);
            _ = holder;
        }
    }

    /// <summary>Both wire eras, across the whole supported band, through the codec each protocol actually binds. The packet is built with BOTH forms populated so the era's own encoder picks the one it writes, which is what makes this a statement about the binding rather than a restatement of it.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(578)]
    [InlineData(762)]
    [InlineData(766)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(775)]
    [InlineData(776)]
    public async Task SelfMotion_IsRoutedToSelfOnEveryProtocolBand(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Physics = true, Entities = true });
        harness.State.Self.EntityId = SelfEntityId;
        harness.State.Self.Velocity = Vec3d.Zero;

        await harness.ApplyAsync(RoundTrip(protocol, SelfEntityId, -3200, 2880, 0, LiveKnockbackBlock));

        // The two forms quantize differently, so the band assertion is on the value being the knockback rather than on an exact encoding: -0.4 / +0.36 short versus -0.399988 / +0.360801 quantized.
        Assert.InRange(harness.State.Self.Velocity.X, -0.405, -0.395);
        Assert.InRange(harness.State.Self.Velocity.Y, 0.355, 0.365);
        Assert.Equal(0.0, harness.State.Self.Velocity.Z, 6);
        Assert.Equal(1, harness.VelocityPushCount);
    }

    private static object RoundTrip(int protocol, int entityId, short x, short y, short z, byte[] modernRaw)
        => BoundDescriptorCodec.RoundTrip(
            protocol, "set_entity_motion",
            new ClientboundSetEntityMotionPacket(entityId, x, y, z, modernRaw));

    private static void StandOnFloor(ApplierHarness harness, PhysicsEngineHolder holder)
    {
        for (int x = -8; x <= 8; x++)
            for (int z = -8; z <= 8; z++)
                harness.State.World.SetBlockStateId(new BlockPos(x, 64, z), SolidStateId);

        harness.State.Self.Position = new Vec3d(0.5, 65.0, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.ResyncPosition();
        Tick(holder, 5);
        Assert.True(harness.State.Self.OnGround, "the fixture never put the player on the floor");
    }

    private static void Tick(PhysicsEngineHolder holder, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            holder.TickIdle();

    }

    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync(
        int protocol, int selfEntityId)
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

        var holder = new PhysicsEngineHolder(services, new NonZeroStateIsSolid(), NullLogger.Instance);
        harness.PositionResync = holder.ResyncPosition;
        harness.VelocityPush = holder.ApplyVelocity;

        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: selfEntityId, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null));

        Assert.Equal(selfEntityId, harness.State.Self.EntityId);
        holder.EnsureEngine();
        return (harness, holder, scheduler);
    }

    /// <summary>State 0 (air) is empty, anything else is a full cube, so the test owns its own terrain.</summary>
    private sealed class NonZeroStateIsSolid : IBlockShapeSource
    {
        private static readonly Aabb[] Cube = [new(0, 0, 0, 1, 1, 1)];
        private static readonly Aabb[] Empty = [];

        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => GetCollisionShapes(state.StateId);

        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => stateId == 0 ? Empty : Cube;

        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => GetCollisionShapes(state.StateId);

        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => GetCollisionShapes(stateId);
    }
}
