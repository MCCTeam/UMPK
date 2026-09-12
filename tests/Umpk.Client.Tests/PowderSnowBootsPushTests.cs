using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The two halves of the client's side of powder-snow walking: pushing <see cref="PhysicsConditions.PowderSnowWalkable"/> from the feet slot, and noticing when the boots the running plan was made on come off.</summary>
/// <remarks>
/// <para><b>The item identity is exact.</b> Only leather boots in the feet slot make powder snow walkable. Netherite boots and leather boots in other slots do not.</para>
/// <para><b>Why the mid-route arm exists.</b> <c>PathfinderCapabilities</c> is frozen at capture and <see cref="PhysicsConditions"/> changes with equipment. Removing the boots mid-route must trigger <c>NavigationPreemption.CapabilityLost</c> and a replan with the current capabilities.</para>
/// <para><b>It is scoped to routes that lean on the boots</b>, exactly as the effect arm is scoped to <c>DependsOnEffects</c>: a plan that never stands on powder snow is not watched at all, so taking boots off in a lava field replans nothing.</para>
/// </remarks>
public sealed class PowderSnowBootsPushTests
{
    /// <summary>1.21.11, the protocol the live course runs on.</summary>
    private const int LiveProtocol = 774;

    private const int SelfEntityId = 1;

    /// <summary>The player-window (menu-space) FEET slot; <c>InventoryPipelineTests</c> pins it.</summary>
    private const int BootsMenuSlot = 8;

    /// <summary>The player-window (menu-space) HEAD slot; armour runs head-first from 5.</summary>
    private const int HelmetMenuSlot = 5;

    private static readonly Registry<ItemDefinition> TestItems =
        new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
            .Add(1, Identifier.Minecraft("leather_boots"), new ItemDefinition(1))
            .Add(2, Identifier.Minecraft("netherite_boots"), new ItemDefinition(2))
            .Build();

    private static ItemStack Stack(string path)
    {
        Assert.True(
            TestItems.TryGet(Identifier.Minecraft(path), out RegistryEntry<ItemDefinition> entry),
            $"the fixture registry has no minecraft:{path}");
        return new ItemStack(entry, 1);
    }

    /// <summary>THE CLAIM. Leather boots in the feet slot, and the pushed conditions say the body may walk on powder snow.</summary>
    [Fact]
    public void LeatherBootsInTheFeetSlot_PushPowderSnowWalkable()
        => Assert.True(PushedWith(BootsMenuSlot, "leather_boots").PowderSnowWalkable);

    /// <summary>The negative controls, one per way of being wrong: nothing on the feet, the best boots in the game on the feet, and the right boots in the wrong slot.</summary>
    [Theory]
    [InlineData(BootsMenuSlot, null, "no boots at all")]
    [InlineData(BootsMenuSlot, "netherite_boots", "canEntityWalkOnPowderSnow names LEATHER boots")]
    [InlineData(HelmetMenuSlot, "leather_boots", "it names the FEET slot")]
    public void EverythingElse_DoesNot(int slot, string? item, string why)
    {
        Assert.False(PushedWith(slot, item).PowderSnowWalkable);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>The same read reaches the PLANNER's capture, which is a different surface: the engine gets a bool on <see cref="PhysicsConditions"/> and the planner derives its own from the captured item list. Both have to agree or the plan and the body disagree about the same lane.</summary>
    [Theory]
    [InlineData("leather_boots", true)]
    [InlineData("netherite_boots", false)]
    [InlineData(null, false)]
    public void TheCaptureAndTheConditionsAgree(string? item, bool expected)
    {
        PlanCapture capture = CaptureWith(BootsMenuSlot, item);
        Assert.Equal(expected, capture.Conditions.PowderSnowWalkable);
        Assert.Equal(expected, capture.Capabilities.PowderSnowWalkable);
    }

    /// <summary>A route that STANDS on powder snow leans on the boots and says so, which is what scopes the mid-route watch.</summary>
    [Fact]
    public void ARouteOverPowderSnow_DependsOnTheBoots()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(BootsMenuSlot, "leather_boots");
        PathExecutor executor = BuildRoute(harness, holder);

        Assert.True(executor.Context.DependsOnPowderSnowBoots);
    }

    /// <summary>The scope control: the same booted body over plain stone leans on nothing, so taking the boots off must not cost it a replan.</summary>
    [Fact]
    public void ARouteOverStone_DoesNotDependOnTheBoots()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(BootsMenuSlot, "leather_boots", snow: false);
        PathExecutor executor = BuildRoute(harness, holder);

        Assert.False(executor.Context.DependsOnPowderSnowBoots);

        harness.State.Inventory.SetSlot(InventoryState.PlayerWindowId, BootsMenuSlot, ItemStack.Empty);
        Assert.NotEqual(NavigationPreemption.CapabilityLost, holder.TickNavigation(executor).Preemption);
    }

    /// <summary>The case the <c>Capabilities.PowderSnowWalkable</c> guard on the route scan exists for, and it is not an optimisation. An UNBOOTED body can legitimately be routed over powder snow without ever standing on it: a swim node's support cell is not a floor it rests on, and <c>RestsOnAHazardousFloor</c> lets the swim family pass over one because <c>PresentsAFloor</c> answers false for bare powder snow.</summary>
    /// <remarks>Without the guard, that route's segments report a powder-snow support, the executor is marked as depending on boots the body never had, and <c>LostThePowderSnowBoots</c> fires on the very first tick and on every replan after it - a navigation that can never start. Found by ablation: the guard was the one term in this commit no other row could see.</remarks>
    [Fact]
    public void AnUnbootedSwimOverPowderSnow_DoesNotDependOnBootsItNeverHad()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) =
            Build(BootsMenuSlot, item: null, snow: true, flooded: true);
        PathExecutor executor = BuildRoute(harness, holder);

        Assert.False(executor.Context.DependsOnPowderSnowBoots);
        Assert.NotEqual(NavigationPreemption.CapabilityLost, holder.TickNavigation(executor).Preemption);
    }

    /// <summary>THE MID-ROUTE ARM. The boots come off while a route that stands on powder snow is running, and the next tick pre-empts with <c>CapabilityLost</c> instead of walking the body into a pit the plan no longer describes.</summary>
    [Fact]
    public void LosingTheBootsMidRoute_PreemptsWithCapabilityLost()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(BootsMenuSlot, "leather_boots");
        PathExecutor executor = BuildRoute(harness, holder);

        Assert.NotEqual(NavigationPreemption.CapabilityLost, holder.TickNavigation(executor).Preemption);

        harness.State.Inventory.SetSlot(InventoryState.PlayerWindowId, BootsMenuSlot, ItemStack.Empty);
        Assert.Equal(NavigationPreemption.CapabilityLost, holder.TickNavigation(executor).Preemption);
    }

    /// <summary>Swapping leather boots for netherite is losing them, because the vanilla test is an item identity and netherite fails it. A check written as "is the feet slot non-empty" would pass every other row in this file and walk the body into the snow here.</summary>
    [Fact]
    public void SwappingToNetheriteMidRoute_IsAlsoLosingThem()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(BootsMenuSlot, "leather_boots");
        PathExecutor executor = BuildRoute(harness, holder);

        harness.State.Inventory.SetSlot(
            InventoryState.PlayerWindowId, BootsMenuSlot, Stack("netherite_boots"));
        Assert.Equal(NavigationPreemption.CapabilityLost, holder.TickNavigation(executor).Preemption);
    }

    private static PathExecutor BuildRoute(ApplierHarness harness, PhysicsEngineHolder holder)
    {
        BlockPos goal = new(6, 64, 0);
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(goal));
        Assert.NotNull(capture);

        PlannedRoute? route = holder.BuildExecutor(capture!.Value, PathfinderOptions.Default, default);
        Assert.NotNull(route);
        Assert.NotNull(harness);
        return route!.Value.Executor;
    }

    private static PhysicsConditions PushedWith(int slot, string? item)
        => CaptureWith(slot, item).Conditions;

    private static PlanCapture CaptureWith(int slot, string? item)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(slot, item);
        BlockPos here = BlockPos.Containing(harness.State.Self.Position);
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(here));
        Assert.NotNull(capture);
        return capture!.Value;
    }

    private static (ApplierHarness Harness, PhysicsEngineHolder Holder) Build(
        int slot, string? item, bool snow = true, bool flooded = false)
    {
        Assert.True(JavaVersions.TryGetByProtocol(LiveProtocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Terrain = true, Inventory = true, Entities = true });
        harness.State.Registries = JavaGameData.Registries(LiveProtocol);
        harness.State.Self.EntityId = SelfEntityId;

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

        var holder = new PhysicsEngineHolder(
            services, JavaGameData.BlockShapes(LiveProtocol), NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(LiveProtocol).Blocks;
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, LiveProtocol),
            new RegistryBlockDataSource(blocks, isLegacy: false),
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("powder_snow"), out BlockDefinition? powderSnow));

        // A corridor on z=0 with a powder-snow section at x 2..4, stone beneath it, so the snow is a pit and not a void. Off the corridor there is no floor at all, so the plan either uses the snow or there is no plan - the same discriminating shape the pathfinding fixtures use.
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        for (int x = -4; x <= 10; x++)
        {
            bool isSnow = snow && x is >= 2 and <= 4;
            world.SetBlockStateId(new BlockPos(x, 63, 0), (isSnow ? powderSnow : stone).DefaultStateId);
            if (isSnow)
            {
                world.SetBlockStateId(new BlockPos(x, 62, 0), stone.DefaultStateId);
                if (flooded)
                {
                    // Two blocks of water standing over the snow. The body swims the section instead of walking it, so its feet cell sits directly above powder snow while its weight is on nothing at all.
                    world.SetBlockStateId(new BlockPos(x, 64, 0), water.DefaultStateId);
                    world.SetBlockStateId(new BlockPos(x, 65, 0), water.DefaultStateId);
                }
            }
        }

        harness.State.InstallWorld(world);
        // A flooded run starts the body IN the water over the snow, because that is the only way to reach the case under test: the swim family plants the node, and the first segment's own start cell is the one sitting directly above powder snow.
        harness.State.Self.Position = flooded ? new Vec3d(3.5, 64, 0.5) : new Vec3d(0.5, 64, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        harness.State.Self.Attributes.SeedPlayerDefaults(harness.State.Registries, known: true);
        if (item is not null)
            harness.State.Inventory.SetSlot(InventoryState.PlayerWindowId, slot, Stack(item));

        holder.EnsureEngine();
        return (harness, holder);
    }
}
