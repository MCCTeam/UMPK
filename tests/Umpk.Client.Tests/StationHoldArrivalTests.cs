using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// Where the water station hold ANCHORS, which is a different question from whether it presses the right direction (<see cref="StationHoldInputTests"/> owns that) and the one the live course row E6 has been failing on.
/// <para>The hold does not hold the body AT its anchor. It parks the body a fixed distance downstream of it, because <c>HoldStationToleranceSq</c> is a radial deadband: inside it nothing is pressed, so the current carries the body to the deadband's downstream edge and the hold catches it there. That offset is a constant of the deadband, not of the arrival, so an anchor placed near the low edge of the goal cell parks the body in the cell BELOW it. A submerged arrival is exactly that case: the body is buoyant, <c>OnGround</c> is false, <c>GroundedSegmentController.ShouldComplete</c> takes its water arm, and that arm completes on <c>SegmentGeometry.IsCenterInsideTargetBlock</c>, which guarantees only <c>cellX + 0.0</c>.</para>
/// <para>The anchor, rather than the deadband alone, determines the hold point. Anchoring at the centre of the cell the navigation completed in parks the body a fixed offset downstream of the centre, which is still inside the cell for every arrival, by construction.</para>
/// </summary>
public sealed class StationHoldArrivalTests
{
    private const int Protocol = 772;
    private const int FloorY = 84;
    private const int WaterY = 85;

    /// <summary>The channel's source cell; the current runs from here toward -X.</summary>
    private const int SourceX = 10;

    /// <summary>The goal cell, one short of the source, reached by wading UPSTREAM.</summary>
    private const int GoalX = 9;

    private const int LaneZ = 4;

    /// <summary>Where the body parks is decided by the GOAL CELL, not by where in that cell the body happened to stop. That is the whole property, and it is what makes the hold safe for an arrival anywhere in the cell rather than only for a high one.</summary>
    /// <remarks>
    /// <para>The park offset is a constant of the deadband: the hold presses nothing inside <c>HoldStationToleranceSq</c>, so the current carries the body to that circle's downstream edge and the hold catches it there. Anchored at the cell centre, the park is therefore <c>cellX + 0.5 - offset</c> for the same offset at every arrival, which is inside the cell by construction. Anchored at the arrival, it is <c>arrivalX - offset</c>, which leaves the cell as soon as the arrival is under <c>cellX + offset</c>.</para>
    /// <para>The band below is that construction, not a curve fit: the measured offset is 0.1932..0.2215, so the park lands in <c>cellX + 0.278..0.307</c>.</para>
    /// </remarks>
    [Fact]
    public async Task TheParkPositionIsSetByTheGoalCellAndNotByWhereTheBodyStopped()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            Vec3d arrival = NavigateUpstream(harness, holder);
            Assert.True(holder.HasStationHold, $"the water hold did not arm at {arrival}");

            for (int tick = 0; tick < 400; tick++)
                holder.TickIdle();

            Vec3d rest = harness.State.Self.Position;
            Assert.Equal(GoalX, (int)Math.Floor(rest.X));

            double offsetFromCellCentre = GoalX + 0.5 - rest.X;
            Assert.InRange(offsetFromCellCentre, 0.15, 0.25);
        }
    }

    /// <summary>The mechanism behind the row above, asserted directly: the hold anchors on the CENTRE of the cell the navigation completed in, not on the position it completed at.</summary>
    /// <remarks>This assertion distinguishes an active station hold from a fixture that happens to arrive high enough. The behavioural test above can only ever exercise the arrival its own geometry produces; this one pins the property that holds for EVERY arrival, including the <c>cellX + 0.0025</c> the water completion arm permits.</remarks>
    [Fact]
    public async Task TheHoldAnchorsOnTheArrivalCellsCentreAndNotOnTheArrivalPosition()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        await using (loop)
        {
            Vec3d arrival = NavigateUpstream(harness, holder);

            Vec3d? anchor = holder.StationHoldAnchor;
            Assert.NotNull(anchor);
            Assert.Equal(Math.Floor(arrival.X) + 0.5, anchor.Value.X, 9);
            Assert.Equal(Math.Floor(arrival.Z) + 0.5, anchor.Value.Z, 9);
        }
    }

    /// <summary>The latch stays as narrow as it was. A navigation that ends on dry ground must leave no hold at all, because the idle tick outside a completed water navigation is byte-for-byte vanilla.</summary>
    [Fact]
    public async Task AnArrivalOnDryGroundLeavesNoHold()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(dry: true);
        await using (loop)
        {
            NavigateUpstream(harness, holder);
            Assert.False(holder.HasStationHold);
            Assert.Null(holder.StationHoldAnchor);
        }
    }

    /// <summary>Runs the upstream wade to completion and answers the position it completed at.</summary>
    private static Vec3d NavigateUpstream(ApplierHarness harness, PhysicsEngineHolder holder)
    {
        PlanCapture? capture = holder.CapturePlan(new GoalNear(GoalX, WaterY, LaneZ, 0));
        Assert.NotNull(capture);
        PathExecutor? executor = holder.BuildExecutor(
            capture.Value, Umpk.Pathfinding.PathfinderOptions.Default, CancellationToken.None)?.Executor;
        Assert.NotNull(executor);

        PathExecutorState state = PathExecutorState.InProgress;
        for (int tick = 0; tick < 1200 && state == PathExecutorState.InProgress; tick++)
            state = holder.TickNavigation(executor).State;

        Assert.Equal(PathExecutorState.Complete, state);
        Vec3d arrival = harness.State.Self.Position;
        Assert.Equal(GoalX, (int)Math.Floor(arrival.X));
        return arrival;
    }

    /// <summary>The offline twin of E6's channel: a sealed one-wide lane with a source at the +X end and one level of fall per block toward -X, which is exactly what a vanilla server spreads into a sealed channel. The bot starts downstream and is asked to wade up it.</summary>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)>
        StartAsync(bool dry = false)
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Pathfinding = true, Terrain = true });
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

        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var holder = new PhysicsEngineHolder(services, shapes, NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        int stoneState = stone.DefaultStateId;

        for (int x = -2; x <= 14; x++)
            for (int z = LaneZ - 2; z <= LaneZ + 2; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stoneState);

        for (int x = -2; x <= 14; x++)
            for (int y = WaterY; y <= WaterY + 1; y++)
            {
                world.SetBlockStateId(new BlockPos(x, y, LaneZ - 1), stoneState);
                world.SetBlockStateId(new BlockPos(x, y, LaneZ + 1), stoneState);
            }

        if (!dry)
            for (int x = SourceX; x >= SourceX - 7; x--)
                world.SetBlockStateId(new BlockPos(x, WaterY, LaneZ), WaterState(water, data, SourceX - x));

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(SourceX - 6 + 0.5, WaterY, LaneZ + 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }

    /// <summary>The <c>minecraft:water</c> state with a given <c>level</c>. Water's only property is <c>level</c>, so the state id is the block's minimum plus the level.</summary>
    private static int WaterState(BlockDefinition water, IBlockDataSource data, int level)
    {
        int id = water.MinStateId + level;
        var state = new BlockState(data, id);
        Assert.True(state.TryGetProperty("level", out string actual));
        Assert.Equal(level.ToString(System.Globalization.CultureInfo.InvariantCulture), actual);
        return id;
    }
}
