using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Movement;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// <see cref="Navigator.MoveToAsync"/> through the real chain a <c>move</c> command drives: the real session loop, the real planner, the real executor, the real engine and the version's real block data.
/// <para>A move must enter the destination block instead of stopping one block short. This requirement is independent of whether the destination contains water. <c>MoveToAsync</c> built <c>NearGoal(BlockPos.Containing(target), 1)</c>, and <c>AStarPathFinder.Calculate</c> returns Success at the FIRST popped node the goal accepts, so for any destination two or more blocks away, the last step can be omitted.</para>
/// <para>The same radius has a second face: for a destination ONE block away the near goal accepts the START block, the path is a single node, <c>PathSegmentBuilder.FromPath</c> yields no segments and <c>MoveToAsync</c> must treat that one-node path as an already reached block or continue with sub-block movement as required.</para>
/// </summary>
public sealed class MoveToDestinationBlockTests
{
    private const int FloorY = 75;

    /// <summary>1.8.9 and 1.21.7/1.21.8: one protocol either side of the flattening.</summary>
    public static TheoryData<int> Protocols => [47, 772];

    /// <summary>The movement probe's own <c>cap_water</c> geometry: a water cell at the anchor's own level, two blocks away, with a stone floor under it. The player must END UP IN IT, and the assertion re-reads the block it ended in so it cannot pass against a fixture that has no water.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MoveToAWaterBlockTwoAwayEndsInsideTheWater(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.Water);

        await fixture.MoveToAsync(new Vec3d(2, FloorY, 0));

        BlockPos arrival = BlockPos.Containing(fixture.Self.Position);
        Assert.Equal(new BlockPos(2, FloorY, 0), arrival);
        Assert.True(
            MoveHelperIsWater(fixture.World.GetBlock(arrival)),
            $"protocol {protocol}: the block the player ended in is {fixture.World.GetBlock(arrival)}, not water, "
                + "so this fixture could not have proved anything about entering water");
    }

    /// <summary>The same geometry without water proves that destination handling is independent of fluid state.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MoveToADryBlockTwoAwayEndsInsideIt(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.Bare);

        await fixture.MoveToAsync(new Vec3d(2, FloorY, 0));

        Assert.Equal(new BlockPos(2, FloorY, 0), BlockPos.Containing(fixture.Self.Position));
    }

    /// <summary>A destination ONE block away, which is what every directional <c>move</c> resolves to. This threw It must move rather than report "No path to the goal was found."</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MoveToTheNeighbouringBlockActuallyMovesThere(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.Bare);

        await fixture.MoveToAsync(new Vec3d(0, FloorY, -1));

        Assert.Equal(new BlockPos(0, FloorY, -1), BlockPos.Containing(fixture.Self.Position));
    }

    /// <summary>The point the player is already standing on. Nothing to plan and nothing to walk, so it completes and leaves the player where it is. The block-level goal must not satisfied by the start node and the segment list was empty.)</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MoveToThePointAlreadyOccupiedCompletesWithoutMoving(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.Bare);
        Vec3d before = fixture.Self.Position;

        await fixture.MoveToAsync(new Vec3d(0.5, FloorY, 0.5));

        Assert.Equal(before.X, fixture.Self.Position.X, 6);
        Assert.Equal(before.Z, fixture.Self.Position.Z, 6);
    }

    /// <summary>
    /// A destination INSIDE the block the player already occupies, which is what <c>move center</c> always resolves to and what any <c>move x y z</c> issued from the destination block resolves to.
    /// <para>Neither the planner nor the executor can express a fraction of a block: A* is satisfied by the start node, and every completion predicate in <c>SegmentGeometry</c> tests <c>Math.Floor(target)</c>. The offsets below are the four quadrants of the block plus two near-edge cases, so a controller that only worked in one direction fails here.</para>
    /// </summary>
    [Theory]
    [InlineData(47, 0.12, 0.12)]
    [InlineData(47, 0.90, 0.10)]
    [InlineData(47, 0.10, 0.90)]
    [InlineData(47, 0.95, 0.95)]
    [InlineData(47, 0.70, 0.30)]
    [InlineData(772, 0.12, 0.12)]
    [InlineData(772, 0.90, 0.10)]
    [InlineData(772, 0.10, 0.90)]
    [InlineData(772, 0.95, 0.95)]
    [InlineData(772, 0.70, 0.30)]
    public async Task MoveToAPointInsideTheOccupiedBlockWalksTheRemainingFraction(int protocol, double x, double z)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.Bare);
        await fixture.PlaceAsync(new Vec3d(x, FloorY, z));
        var target = new Vec3d(0.5, FloorY, 0.5);
        double startDistance = Horizontal(fixture.Self.Position, target);

        await fixture.MoveToAsync(target);

        double arrived = Horizontal(fixture.Self.Position, target);
        Assert.True(
            arrived <= Navigator.SubBlockArrivalTolerance,
            $"protocol {protocol}: started {startDistance:F3} away, ended {arrived:F3} away at {fixture.Self.Position}");

        // It must remain there after the lease is released and the idle tick takes over.
        await fixture.IdleAsync(40);
        double settled = Horizontal(fixture.Self.Position, target);
        Assert.True(
            settled <= Navigator.SubBlockArrivalTolerance,
            $"protocol {protocol}: drifted to {settled:F3} away at {fixture.Self.Position} after 2s of idling");
    }

    private static double Horizontal(Vec3d a, Vec3d b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>A destination NOTHING can stand in (a solid stone block) still completes, by the near-goal fallback, and stops next to it. This is the behaviour every caller had before, kept deliberately: refusing a route to a block that cannot be occupied would break <c>move</c> onto a ladder rung, whose foot position <c>AStarPathFinder.IsGoalReachableFootPosition</c> rejects for the same reason. Detecting the shortfall is the caller's job, using the observed position.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MoveToABlockNothingCanStandInFallsBackAndStopsNextToIt(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.SolidPillar);

        await fixture.MoveToAsync(new Vec3d(2, FloorY, 0));

        BlockPos arrival = BlockPos.Containing(fixture.Self.Position);
        Assert.NotEqual(new BlockPos(2, FloorY, 0), arrival);
        Assert.True(
            Math.Abs(arrival.X - 2) + Math.Abs(arrival.Y - FloorY) + Math.Abs(arrival.Z) <= 1,
            $"protocol {protocol}: the fallback should have stopped next to the pillar, it stopped at {arrival}");
    }

    /// <summary>
    /// The SAME unstandable destination, approached from the cell already beside it. This is the edge case in the fallback that the row above proves works from a distance: <c>NearGoal(destination, 1)</c> accepts the destination and its six face neighbours, so a body already standing on one of them satisfies the goal at the START node. <c>AStarPathFinder.Calculate</c> short-circuits that to a ONE-node path, <c>PathSegmentBuilder.FromPath</c> yields no segments, <c>PhysicsEngineHolder.BuildExecutor</c> returns null on <c>segments.Count == 0</c>, and the fallback's <c>?? throw</c> fires "No path to the goal was found." at a body that is already as close as any body can get.
    /// <para>It is the same shape this class's own remarks record for the one-block case, and the one <c>RunNavigationAsync</c> guards with <c>goal.IsInGoal(here)</c> for ItemsCollector. The <c>MoveToAsync</c> fallback never got that guard.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MoveToABlockNothingCanStandInFromTheCellBesideItDoesNotThrow(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.SolidPillar);
        await fixture.PlaceAsync(new Vec3d(1.5, FloorY, 0.5));

        await fixture.MoveToAsync(new Vec3d(2, FloorY, 0));

        BlockPos arrival = BlockPos.Containing(fixture.Self.Position);
        Assert.NotEqual(new BlockPos(2, FloorY, 0), arrival);
        Assert.True(
            Math.Abs(arrival.X - 2) + Math.Abs(arrival.Y - FloorY) + Math.Abs(arrival.Z) <= 1,
            $"protocol {protocol}: the body should still be beside the pillar, it is at {arrival}");
    }

    /// <summary>
    /// The predicate the whole report rests on, exercised directly, because the navigation-level rows above cannot distinguish "answers the truth" from "answers false to everything": every standable destination they name is also REACHED, and every unreachable one throws before a verdict exists. So the discrimination is pinned here.
    /// <list type="bullet">
    /// <item>the open cell on the platform: standable</item>
    /// <item>the solid pillar cell: not standable, nothing fits in a block</item>
    /// <item>the cell whose HEAD is inside the pillar: not standable, which is the dripstone shape - the
    /// floor is fine and the spike is in the head band</item>
    /// <item>a column no chunk has been loaded for: standable, because "I have never seen that terrain"
    /// must not be reported as "you cannot stand there"</item>
    /// </list>
    /// </summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task CanStandAtAnswersTheOccupancyQuestionAndNotAConstant(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.SolidPillar);

        Assert.True(await fixture.CanStandAtAsync(new BlockPos(0, FloorY, 0)));
        Assert.False(await fixture.CanStandAtAsync(new BlockPos(2, FloorY, 0)));

        // The head band: feet on the platform floor, head inside the block that is there.
        Assert.False(await fixture.CanStandAtAsync(new BlockPos(2, FloorY - 1, 0)));

        // Far outside the 9x9 platform this fixture built, so no chunk holds it.
        Assert.True(await fixture.CanStandAtAsync(new BlockPos(4000, FloorY, 4000)));
    }

    /// <summary>The verified surface's half of the same story, from BOTH starts: the body ends beside the pillar and the verdict says so with <see cref="MoveOutcome.StoppedNear"/> rather than the flat <see cref="MoveOutcome.StoppedShort"/> a route that simply did not deliver gets. <c>Reached</c> stays false in both, because getting close is not arriving.</summary>
    [Theory]
    [InlineData(47, 0.5)]
    [InlineData(47, 1.5)]
    [InlineData(772, 0.5)]
    [InlineData(772, 1.5)]
    public async Task VerifiedMoveToABlockNothingCanStandInReportsStoppedNear(int protocol, double startX)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.SolidPillar);
        await fixture.PlaceAsync(new Vec3d(startX, FloorY, 0.5));

        MoveResult result = await fixture.MoveToVerifiedAsync(new Vec3d(2, FloorY, 0));

        Assert.False(result.Reached);
        Assert.True(result.DestinationUnstandable);
        Assert.Equal(MoveOutcome.StoppedNear, result.Outcome);
    }

    /// <summary>The control for the row above, and the one that keeps the new outcome from swallowing the old one: the destination here is perfectly standable, so a verdict that did not reach it has to stay <see cref="MoveOutcome.StoppedShort"/>. The route is refused for REACHABILITY (a sealed pocket), which is the other failure entirely.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task VerifiedMoveToAStandableButSealedOffBlockStaysStoppedShort(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.SealedPocket);

        // The navigation itself still refuses; what this row pins is the classification of the verdict a caller gets when it does NOT, so it is judged from the pose the refusal left the body in.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.MoveToVerifiedAsync(new Vec3d(3, FloorY, 0)));

        MoveResult judged = Navigator.Judge(
            new Vec3d(3, FloorY, 0), fixture.Self.Position, subBlockRequest: false);
        Assert.False(judged.Reached);
        Assert.False(judged.DestinationUnstandable);
        Assert.Equal(MoveOutcome.StoppedShort, judged.Outcome);
    }

    /// <summary>The byte-identical control: a destination that IS standable and IS reached. Nothing about the new outcome may touch this, so it asserts the arrival AND the verdict shape.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task VerifiedMoveToAStandableBlockStillReadsReached(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.Bare);

        MoveResult result = await fixture.MoveToVerifiedAsync(new Vec3d(2, FloorY, 0));

        Assert.True(result.Reached);
        Assert.False(result.DestinationUnstandable);
        Assert.Equal(MoveOutcome.Reached, result.Outcome);
    }

    /// <summary>"I cannot stand there" and "I cannot get there" must not collapse into one another. Here the destination is a perfectly standable cell inside a sealed stone box: the exact goal fails on REACHABILITY rather than on occupancy, every cell of the radius-1 ball around it is solid too, so the fallback finds nothing either and the refusal is the correct answer. This row is what stops a relaxation of the unstandable case from quietly turning every unreachable destination into a shrug.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task MoveToAStandableButSealedOffBlockStillRefuses(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.SealedPocket);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.MoveToAsync(new Vec3d(3, FloorY, 0)));

        Assert.Equal("No path to the goal was found.", thrown.Message);
    }

    /// <summary>1.12.2 and 1.21.7/1.21.8, the two protocols <c>LadderClimbProbeFixtureTests</c> pins the climb on. Protocol 47 is excluded because this all-air fixture cannot represent its climb behavior. Protocols 110, 340, 404, 756, and 772 finish on the rung at y=76.008.</summary>
    public static TheoryData<int> ClimbProtocols => [340, 772];

    /// <summary>The movement probe's ladder fixture exercises the near-goal fallback through <see cref="Navigator.MoveToAsync"/> exactly as <c>cap_ladder</c> drives it. The rung is not a foot position the planner will finish in, so this route depends on the near-goal fallback surviving.</summary>
    [Theory]
    [MemberData(nameof(ClimbProtocols))]
    public async Task MoveToALadderRungStillClimbs(int protocol)
    {
        await using Fixture fixture = await Fixture.StartAsync(protocol, Terrain.Ladder);

        await fixture.MoveToAsync(new Vec3d(1, FloorY + 2, 0));

        Vec3d arrival = fixture.Self.Position;
        Assert.True(arrival.Y >= FloorY + 1.0, $"protocol {protocol}: the climb did not happen, y={arrival.Y}");
        Assert.True(
            arrival.X > 1.0 && arrival.X < 2.0,
            $"protocol {protocol}: the climb left the ladder column at {arrival}");
    }

    private static bool MoveHelperIsWater(BlockState state)
        => state.IsFluid && !state.IsDefault && state.Block.Id == Identifier.Minecraft("water");

    private enum Terrain
    {
        /// <summary>A flat stone platform and nothing else.</summary>
        Bare,

        /// <summary>The probe's water cell at (2, FloorY, 0), floored with stone.</summary>
        Water,

        /// <summary>A stone block occupying (2, FloorY, 0), so nothing can stand there.</summary>
        SolidPillar,

        /// <summary>A standable air cell at (3, FloorY, 0) with a stone floor under it, sealed on all six sides by stone. The cell itself passes every occupancy predicate; nothing can REACH it, and nothing can reach any cell of the radius-1 ball around it either.</summary>
        SealedPocket,

        /// <summary>The probe's three-rung ladder against a wall at (1, *, -1).</summary>
        Ladder,
    }

    /// <summary>A joined client with a running session loop, the version's real block data, and a navigator built on the real physics holder. Everything <see cref="Navigator.MoveToAsync"/> touches is the live object; only the connection is absent.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ChannelSessionScheduler _scheduler;

        private readonly PhysicsEngineHolder _holder;

        private Fixture(
            ChannelSessionScheduler scheduler,
            ApplierHarness harness,
            Navigator navigator,
            Umpk.Game.World.World world,
            PhysicsEngineHolder holder)
        {
            _scheduler = scheduler;
            Harness = harness;
            Navigator = navigator;
            World = world;
            _holder = holder;
        }

        public ApplierHarness Harness { get; }

        public Navigator Navigator { get; }

        public Umpk.Game.World.World World { get; }

        public Umpk.Client.State.SelfState Self => Harness.State.Self;

        public ValueTask DisposeAsync() => _scheduler.DisposeAsync();

        /// <summary>Puts the player at a position and re-seeds the engine, the way a server teleport does.</summary>
        public async Task PlaceAsync(Vec3d position)
        {
            Harness.State.Self.Position = position;
            Harness.State.Self.Velocity = Vec3d.Zero;
            await _scheduler.InvokeAsync(_holder.ResyncPosition, CancellationToken.None);
        }

        /// <summary>The occupancy predicate the report is built on, run on the loop as the navigator runs it.</summary>
        public Task<bool> CanStandAtAsync(BlockPos cell) => _scheduler.InvokeAsync(
            () => _holder.CanStandAt(cell, PathfinderOptions.Default), CancellationToken.None);

        /// <summary>Drives the production navigator from explicit logical ticks; it owns no timer.</summary>
        public async Task MoveToAsync(Vec3d target)
        {
            Task movement = Navigator.MoveToAsync(target, CancellationToken.None);
            while (!movement.IsCompleted)
            {
                await _scheduler.InvokeAsync(Navigator.TickOnLoop, CancellationToken.None);
                await Task.Yield();
            }

            await movement;
        }

        /// <summary>Drives the verified production surface from the same explicit logical ticks.</summary>
        public async Task<MoveResult> MoveToVerifiedAsync(Vec3d target)
        {
            Task<MoveResult> movement = Navigator.MoveToVerifiedAsync(target, CancellationToken.None);
            while (!movement.IsCompleted)
            {
                await _scheduler.InvokeAsync(Navigator.TickOnLoop, CancellationToken.None);
                await Task.Yield();
            }

            return await movement;
        }

        /// <summary>Runs the client's own idle tick, which is what takes over once the lease is released.</summary>
        public async Task IdleAsync(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                await _scheduler.InvokeAsync(_holder.TickIdle, CancellationToken.None);

        }

        public static async Task<Fixture> StartAsync(int protocol, Terrain terrain)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
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

            IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
            var holder = new PhysicsEngineHolder(services, shapes, NullLogger.Instance);

            Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
            var data = new RegistryBlockDataSource(blocks, isLegacy: protocol < 393);
            var world = new Umpk.Game.World.World(
                WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
                data,
                WorldFactory.EmptyBiomes());

            Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
            int stoneState = stone.DefaultStateId;

            // probe_anchor: a stone floor at FloorY-1 over [-4..4]^2 with air above it.
            for (int x = -4; x <= 4; x++)
                for (int z = -4; z <= 4; z++)
                    world.SetBlockStateId(new BlockPos(x, FloorY - 1, z), stoneState);

            switch (terrain)
            {
                case Terrain.Water:
                    Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
                    world.SetBlockStateId(new BlockPos(2, FloorY, 0), water.DefaultStateId);
                    break;

                case Terrain.SolidPillar:
                    world.SetBlockStateId(new BlockPos(2, FloorY, 0), stoneState);
                    break;

                case Terrain.SealedPocket:
                    // (3, FloorY, 0) stays air over the platform's own stone floor, so it is standable. Everything that touches it - the four sides, the cell above, and the whole radius-1 ball's other members - is stone, so no body can be put next to it either.
                    foreach (BlockPos wall in (BlockPos[])
                    [
                        new(2, FloorY, 0), new(4, FloorY, 0), new(3, FloorY, -1), new(3, FloorY, 1),
                        new(3, FloorY + 1, 0),
                        new(2, FloorY + 1, 0), new(4, FloorY + 1, 0), new(3, FloorY + 1, -1), new(3, FloorY + 1, 1),
                    ])
                        world.SetBlockStateId(wall, stoneState);

                    break;

                case Terrain.Ladder:
                    int ladder = LadderStates.FacingSouthDry(blocks, data);
                    for (int y = FloorY; y <= FloorY + 3; y++)
                        world.SetBlockStateId(new BlockPos(1, y, -1), stoneState);

                    for (int y = FloorY; y <= FloorY + 2; y++)
                        world.SetBlockStateId(new BlockPos(1, y, 0), ladder);

                    break;

                case Terrain.Bare:
                default:
                    break;
            }

            harness.State.InstallWorld(world);
            harness.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
            harness.State.Self.Velocity = Vec3d.Zero;
            await scheduler.InvokeAsync(holder.EnsureEngine, CancellationToken.None).ConfigureAwait(false);

            var navigator = new Navigator(services, holder, new MovementLeaseManager(), NullLogger.Instance);
            return new Fixture(scheduler, harness, navigator, world, holder);
        }
    }
}
