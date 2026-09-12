using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The two coherence rules the sprint split depends on, outside the tick loop.
///
/// <para>The split is: the host pushes <see cref="PhysicsConditions.BaseMovementSpeedAttribute"/> WITHOUT the sprint modifier and the engine applies it itself from the tick's input. Both halves of that have a failure mode that no assertion on speed would catch. If the host reads the attribute with the modifier still in it, the multiply lands twice (1.69x, not 1.3x). If the announced-sprint latch survives a respawn, the tick loop's edge trigger never fires again and the bot sprints for the rest of the session against a server that thinks it is walking.</para>
/// </summary>
public sealed class SprintConditionAndLatchTests
{
    private static JavaVersion Version => JavaVersions.V1_21_5;

    /// <summary>
    /// A hand-built attribute registry, kept rather than switched to the real one so the numbers below stay stated independently of the dataset.
    /// <para>The attribute is staged on <c>SelfState.Attributes</c> because the local player is not a member of the shared <c>EntityStore</c> and its attributes therefore live on <c>SelfState.Attributes</c>, which is what <c>ReadMovementSpeed</c> now reads. Every assertion below is unchanged. See <see cref="SelfMovementSpeed"/>.</para>
    /// </summary>
    private static readonly Registry<AttributeDefinition> Attributes =
        new RegistryBuilder<AttributeDefinition>(RegistryIds.Attribute, 1)
            .Add(0, Identifier.Minecraft("movement_speed"), new AttributeDefinition(0.1, 0, 1024))
            .Build();

    private static readonly Registry<EntityTypeDefinition> EntityTypes =
        new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType, 1)
            .Add(0, Identifier.Minecraft("player"), new EntityTypeDefinition(0.6f, 1.8f))
            .Build();

    /// <summary>The pushed condition excludes <c>minecraft:sprinting</c> and keeps everything else. The modifier is not hypothetical on the wire: <c>movement_speed</c> is syncable, so a server that receives the client's own START_SPRINTING installs the modifier on its copy of the player and broadcasts the resolved value back in a <c>ClientboundUpdateAttributesPacket</c>. Reading <c>AttributeInstance.Value</c> here would let that echo compound with the engine's own multiply.</summary>
    [Fact]
    public async Task PushedMovementSpeed_ExcludesTheSprintModifier_AndKeepsTheOthers()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            AttributeInstance speed = SelfMovementSpeed(harness);
            speed.BaseValue = 0.1;
            speed.AddOrReplaceModifier(new AttributeModifier(
                Identifier.Minecraft("sprinting"), 0.30000001192092896, AttributeModifierOperation.AddMultipliedTotal));
            speed.AddOrReplaceModifier(new AttributeModifier(
                Identifier.Minecraft("effect.speed"), 0.4, AttributeModifierOperation.AddMultipliedTotal));

            holder.PushConditions();

            // Speed I alone: 0.1 * 1.4 = 0.14. With sprint folded in as well it would be 0.182, and that is what the engine would then multiply by 1.3 again for 0.2366.
            PhysicsConditions conditions = Assert.NotNull(holder.EngineConditions);
            Assert.Equal(0.14f, conditions.BaseMovementSpeedAttribute, 6);
            Assert.NotEqual(0.182f, conditions.BaseMovementSpeedAttribute, 4);
        }
    }

    /// <summary>With no sprint modifier present the pushed value is unchanged, so the exclusion is not quietly dropping the modifier list on the floor.</summary>
    [Fact]
    public async Task PushedMovementSpeed_IsUnchangedWhenNoSprintModifierIsPresent()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            AttributeInstance speed = SelfMovementSpeed(harness);
            speed.BaseValue = 0.1;
            speed.AddOrReplaceModifier(new AttributeModifier(
                Identifier.Minecraft("effect.speed"), 0.4, AttributeModifierOperation.AddMultipliedTotal));

            holder.PushConditions();

            Assert.Equal(0.14f, Assert.NotNull(holder.EngineConditions).BaseMovementSpeedAttribute, 6);
        }
    }

    /// <summary>The announced-sprint latch is cleared on respawn because the client player state starts with sprinting false.</summary>
    [Fact]
    public async Task SprintLatchIsClearedOnRespawn()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        _ = holder;
        await using (loop)
        {
            harness.State.Self.Sprinting = true;

            await harness.ApplyAsync(new ClientboundRespawnPacket(
                new CommonPlayerSpawnInfo(
                    DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0,
                    PreviousGameType: 0, IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null,
                    PortalCooldown: 0, SeaLevel: 63),
                DataToKeep: 0,
                Legacy: null));

            Assert.False(harness.State.Self.Sprinting);
        }
    }

    /// <summary>The announced-sprint latch is also cleared on a fresh login.</summary>
    [Fact]
    public async Task SprintLatchIsClearedOnJoin()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        _ = holder;
        await using (loop)
        {
            harness.State.Self.Sprinting = true;

            await ApplyJoinAsync(harness);

            Assert.False(harness.State.Self.Sprinting);
        }
    }

    /// <summary>
    /// Stages movement speed on the local player's <c>SelfState.Attributes</c>, the same collection read by <c>PhysicsEngineHolder.ReadMovementSpeed</c> and written by the applier. The local player is not represented by an <c>Entity</c> in the shared store.
    /// <para>The harness also adds its auxiliary entity so other test surfaces retain the expected session shape.</para>
    /// </summary>
    private static AttributeInstance SelfMovementSpeed(ApplierHarness harness)
    {
        var self = new Entity(harness.State.Self.EntityId, Guid.NewGuid(), EntityTypes[0]);
        harness.State.Entities.Add(self);
        return harness.State.Self.Attributes.GetOrCreate(Identifier.Minecraft("movement_speed"), Attributes[0]);
    }

    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync()
    {
        JavaVersion version = Version;
        var harness = new ApplierHarness(version, new ClientFeatures { Physics = true, Entities = true });
        var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };

        var holder = new PhysicsEngineHolder(services, new AllAirBlockShapes(), NullLogger.Instance);
        await ApplyJoinAsync(harness);
        holder.EnsureEngine();
        return (harness, holder, scheduler);
    }

    private static Task ApplyJoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        return harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null)).AsTask();
    }

    /// <summary>Nothing is solid; this fixture never steps the engine, it only reads the pushed conditions.</summary>
    private sealed class AllAirBlockShapes : IBlockShapeSource
    {
        private static readonly Aabb[] Empty = [];

        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => Empty;

        public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => Empty;

        public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => Empty;

        public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => Empty;
    }
}
