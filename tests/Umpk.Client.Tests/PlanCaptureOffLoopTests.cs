using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>What <c>PhysicsEngineHolder.CapturePlan</c> does on the session loop, stated as a property rather than as a duration.</summary>
/// <remarks>
/// <para>The step is invoked through <c>ISessionServices.InvokeOnLoopResult</c>, so its work occurs between session-loop ticks. Copying the whole planning box there would cost O(volume): 0.7 ms at the median box and 7 ms at the largest course box, plus a multi-megabyte allocation for every plan and replan.</para>
/// <para>A benchmark cannot pin that, because a fast enough machine can hide work that remains O(volume). What pins it is the count of sections the region has copied at the moment <c>CapturePlan</c> returns, which must be ZERO: the box is bounded on the loop and every section in it is copied later, by whoever reads it, which is the search on the thread pool.</para>
/// </remarks>
public sealed class PlanCaptureOffLoopTests
{
    private const int FloorY = 75;
    private const int Protocol = 772;

    [Fact]
    public void CapturePlan_CopiesNoTerrain_AndDefersEveryBlockReadToTheSearch()
    {
        PhysicsEngineHolder holder = CreateHolder();

        PlanCapture? capture = holder.CapturePlan(new GoalBlock(new BlockPos(2, FloorY, 0)));

        Assert.NotNull(capture);
        RegionSnapshotFacts region = Facts(capture!.Value);

        Assert.True(region.IsDemandFaulted, "the loop-side step built an eagerly captured region");
        Assert.Equal(0, region.SectionsCaptured);
        Assert.True(region.CellCount > 100_000, $"the box shrank to {region.CellCount} cells; the pin is about WHEN it is copied, not how big it is");
    }

    /// <summary>And the sections do get copied, by the search: the same capture, after a plan, has materialised a handful of them - not the box, and not none.</summary>
    [Fact]
    public void TheSearch_MaterializesOnlyTheSectionsItReads()
    {
        PhysicsEngineHolder holder = CreateHolder();

        PlanCapture? capture = holder.CapturePlan(new GoalBlock(new BlockPos(2, FloorY, 0)));
        Assert.NotNull(capture);

        Assert.NotNull(holder.BuildExecutor(capture!.Value, PathfinderOptions.Default, CancellationToken.None));

        RegionSnapshotFacts region = Facts(capture.Value);
        Assert.True(region.SectionsCaptured > 0, "the search copied nothing at all, which means it read nothing");
        Assert.True(
            region.SectionsCaptured < region.CellCount / 4096,
            $"the search copied {region.SectionsCaptured} sections, which is the whole box");
    }

    private readonly record struct RegionSnapshotFacts(bool IsDemandFaulted, int SectionsCaptured, long CellCount);

    private static RegionSnapshotFacts Facts(PlanCapture capture) => new(
        capture.Planning.Region.IsDemandFaulted,
        capture.Planning.Region.SectionsCaptured,
        capture.Planning.Region.CellCount);

    /// <summary>A live holder over a nine-by-nine stone platform at <see cref="FloorY"/> - 1 with the player at its centre: the same geometry the plan-statistics suite uses, minus the navigator and the loop.</summary>
    private static PhysicsEngineHolder CreateHolder()
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        var applier = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Pathfinding = true, Terrain = true });
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = applier.State,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = new ChannelSessionScheduler(),
        };

        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var holder = new PhysicsEngineHolder(services, shapes, NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY - 1, z), stone.DefaultStateId);

        applier.State.InstallWorld(world);
        applier.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
        applier.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();

        return holder;
    }
}
