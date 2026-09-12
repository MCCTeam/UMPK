using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The client-side producer for <see cref="PathfinderCapabilities"/>: what <c>PhysicsEngineHolder.CapturePlan</c> reads off the session loop, and what it says when a feature gate means it cannot read it at all.</summary>
/// <remarks>If the entity or inventory feature is disabled, the corresponding capability is unknown rather than absent. A hazard-gated consumer must refuse an unknown capability instead of treating it as false.</remarks>
public sealed class PathfinderCapabilityCaptureTests
{
    private const int Protocol = 772;
    private const int SelfEntityId = 1;
    private const int FloorY = 60;

    /// <summary><c>minecraft:fire_resistance</c> on 764+ (zero-based).</summary>
    private const int FireResistanceId = 11;

    private static readonly Identifier FireResistance = Identifier.Minecraft("fire_resistance");
    private static readonly Identifier DepthStrider = Identifier.Minecraft("depth_strider");

    // Effects

    /// <summary>The capture names the effect, carries its amplifier, and reports the remainder DERIVED against the capture tick rather than the applied duration.</summary>
    [Fact]
    public async Task TheCapture_NamesAnEffectAndDatesIt()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync();
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceId, Amplifier: 1, Duration: 200, Flags: 0));
        harness.State.AdvanceSessionTick(40);

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.True(capabilities.EffectsKnown);
        Assert.True(capabilities.TryGetEffect(FireResistance, out CapabilityEffect effect));
        Assert.Equal(FireResistanceId, effect.NetworkId);
        Assert.Equal(1, effect.Amplifier);
        Assert.Equal(160, effect.RemainingTicks);
        Assert.False(effect.IsInfinite);
        Assert.True(effect.DurationIsEstimated);
    }

    /// <summary>An effect applied on the capture tick itself is not an estimate: it is the number the server just stated.</summary>
    [Fact]
    public async Task AnEffectAppliedOnTheCaptureTick_IsNotReportedAsEstimated()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync();
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceId, 0, Duration: 200, Flags: 0));

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.True(capabilities.TryGetEffect(FireResistance, out CapabilityEffect effect));
        Assert.Equal(200, effect.RemainingTicks);
        Assert.False(effect.DurationIsEstimated);
    }

    [Fact]
    public async Task AnInfiniteEffect_IsReportedAsInfiniteRatherThanAsANumber()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync();
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceId, 0, Duration: -1, Flags: 0));
        harness.State.AdvanceSessionTick(400);

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.True(capabilities.TryGetEffect(FireResistance, out CapabilityEffect effect));
        Assert.True(effect.IsInfinite);
        Assert.Equal(CapabilityEffect.InfiniteRemaining, effect.RemainingTicks);
    }

    /// <summary>Protocols 107-404 carry no <c>minecraft:mob_effect</c> registry at all, so an effect there is present-but-unnameable. It still has to be reported, under a synthetic identifier that cannot collide with a vanilla one, rather than silently dropped.</summary>
    [Fact]
    public async Task AnUnnameableEffect_IsReportedUnderASyntheticIdentifier()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync(protocol: 340);
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, EffectId: 12, Amplifier: 0, Duration: 200, Flags: 0));

        PathfinderCapabilities capabilities = Capture(holder, protocol: 340);

        CapabilityEffect effect = Assert.Single(capabilities.Effects);
        Assert.Equal(Identifier.Parse("umpk:unknown_effect_12"), effect.Id);
        Assert.Equal(12, effect.NetworkId);
    }

    // Items

    [Fact]
    public async Task TheCapture_ListsNonEmptySlotsWithTheirCountsAndMenuIndices()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync();
        SetSlot(harness, menuSlot: 36, Identifier.Minecraft("torch"), count: 12);
        SetSlot(harness, menuSlot: 9, Identifier.Minecraft("torch"), count: 30);

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.True(capabilities.InventoryKnown);
        Assert.Equal(42, capabilities.CountOf(Identifier.Minecraft("torch")));
        Assert.Equal(2, capabilities.Items.Count);
        Assert.Contains(capabilities.Items, i => i.MenuSlot == 36 && i.Count == 12);
        Assert.Contains(capabilities.Items, i => i.MenuSlot == 9 && i.Count == 30);
    }

    /// <summary>The equipment slots resolve through the capture's own <c>HeldSlot</c>, which has to be the selected hotbar index the server last set, not a guess.</summary>
    [Fact]
    public async Task Equipment_ResolvesArmourAndTheSelectedMainHand()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync();
        SetSlot(harness, menuSlot: 8, Identifier.Minecraft("diamond_boots"), count: 1);
        SetSlot(harness, menuSlot: 40, Identifier.Minecraft("torch"), count: 5);
        harness.State.Self.HeldSlot = 4;

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.Equal(4, capabilities.HeldSlot);
        CapabilityItem? boots = capabilities.Equipment(EquipmentSlot.Feet);
        Assert.NotNull(boots);
        Assert.Equal(Identifier.Minecraft("diamond_boots"), boots!.Value.ItemId);
        CapabilityItem? hand = capabilities.Equipment(EquipmentSlot.MainHand);
        Assert.NotNull(hand);
        Assert.Equal(Identifier.Minecraft("torch"), hand!.Value.ItemId);
    }

    /// <summary>The enchantment readout works on the legacy band with no registry at all, which is the half of the range <c>EnchantmentReadout.CanEnumerate</c> answers false for.</summary>
    [Fact]
    public async Task ALegacyEnchantedBoot_ReportsItsLevelByIdentifierAndRefusesToEnumerate()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync(protocol: 754);
        SetSlot(harness, menuSlot: 8, Identifier.Minecraft("diamond_boots"), count: 1, protocol: 754,
            components: DataComponentMap.Empty.With(
                DataComponents.LegacyNbt, new LegacyNbtComponent(LegacyDepthStriderNbt(3))));

        PathfinderCapabilities capabilities = Capture(holder, protocol: 754);

        CapabilityItem boots = Assert.Single(capabilities.Items);
        Assert.True(boots.Enchantments.TryGetLevel(DepthStrider, out int level));
        Assert.Equal(3, level);
        Assert.False(boots.Enchantments.CanEnumerate);
        Assert.Empty(boots.Enchantments.All);
    }

    // Feature-gate honesty

    /// <summary>With entity tracking off the effect table can never be written, so the capture must say it cannot see effects - not report an empty list that reads as "no potions".</summary>
    [Fact]
    public async Task WithEntitiesOff_EffectsAreUnknownRatherThanEmpty()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) =
            await BuildAsync(features: new ClientFeatures { Entities = false, Inventory = true });
        _ = harness;

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.False(capabilities.EffectsKnown);
        Assert.Empty(capabilities.Effects);
        Assert.False(capabilities.TryGetEffect(FireResistance, out _));
        Assert.True(capabilities.InventoryKnown);
    }

    /// <summary>The control for the case above: the same session with entities ON, and a potion actually on the wire, reports it. Without this, a capture that always answered "unknown" would pass.</summary>
    [Fact]
    public async Task WithEntitiesOn_TheSamePotionOnTheWireIsSeen()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) =
            await BuildAsync(features: new ClientFeatures { Entities = true, Inventory = true });
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceId, 0, Duration: 200, Flags: 0));

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.True(capabilities.EffectsKnown);
        Assert.True(capabilities.TryGetEffect(FireResistance, out _));
    }

    [Fact]
    public async Task WithInventoryOff_ItemsAreUnknownRatherThanEmpty()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) =
            await BuildAsync(features: new ClientFeatures { Entities = true, Inventory = false });
        _ = harness;

        PathfinderCapabilities capabilities = Capture(holder);

        Assert.False(capabilities.InventoryKnown);
        Assert.Empty(capabilities.Items);
        Assert.Equal(0, capabilities.CountOf(Identifier.Minecraft("torch")));
        Assert.Null(capabilities.Equipment(EquipmentSlot.Feet));
        Assert.True(capabilities.EffectsKnown);
    }

    // It reaches both contexts

    /// <summary>The capture is only useful if it arrives where a move and a template can read it, so this drives the real <c>BuildExecutor</c> and reads the capabilities back off the executor's own context.</summary>
    [Fact]
    public async Task TheCapturedCapabilities_ReachThePathExecutionContext()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync();
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(
            SelfEntityId, FireResistanceId, 0, Duration: 200, Flags: 0));

        PlanCapture? capture = holder.CapturePlan(new GoalBlock(new BlockPos(3, FloorY + 1, 0)));
        Assert.NotNull(capture);
        PlannedRoute? route = holder.BuildExecutor(
            capture!.Value, Umpk.Pathfinding.PathfinderOptions.Default, CancellationToken.None);
        Assert.NotNull(route);

        PathfinderCapabilities carried = route!.Value.Executor.Context.Capabilities;
        Assert.True(carried.EffectsKnown);
        Assert.True(carried.TryGetEffect(FireResistance, out _));
    }

    // Support

    private static PathfinderCapabilities Capture(PhysicsEngineHolder holder, int protocol = Protocol)
    {
        _ = protocol;
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(new BlockPos(0, FloorY + 1, 0)));
        Assert.NotNull(capture);
        return capture!.Value.Capabilities;
    }

    private static NbtCompound LegacyDepthStriderNbt(int level) => new()
    {
        ["Enchantments"] = new NbtList(NbtTagType.Compound)
        {
            new NbtCompound
            {
                ["id"] = new NbtString("minecraft:depth_strider"),
                ["lvl"] = new NbtShort((short)level),
            },
        },
    };

    private static void SetSlot(
        ApplierHarness harness,
        int menuSlot,
        Identifier item,
        int count,
        int protocol = Protocol,
        DataComponentMap? components = null)
    {
        Assert.True(JavaGameData.Registries(protocol).Items.TryGet(item, out RegistryEntry<ItemDefinition> handle));
        harness.State.Inventory.SetSlot(0, menuSlot, new ItemStack(handle, count, components));
    }

    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder)> BuildAsync(
        int protocol = Protocol, ClientFeatures? features = null)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        ClientFeatures resolved = features ?? new ClientFeatures { Entities = true, Inventory = true };
        resolved.Physics = true;
        resolved.Terrain = true;
        var harness = new ApplierHarness(version!, resolved);
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = new ChannelSessionScheduler(),
        };

        var holder = new PhysicsEngineHolder(services, JavaGameData.BlockShapes(protocol), NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
            new RegistryBlockDataSource(blocks, isLegacy: protocol < 393),
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        for (int x = -6; x <= 6; x++)
            for (int z = -6; z <= 6; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stone!.DefaultStateId);

        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;
        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY + 1.0, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;

        holder.EnsureEngine();
        await Task.CompletedTask;
        return (harness, holder);
    }
}
