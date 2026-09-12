using Microsoft.Extensions.Logging;
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
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The per-plan statistics line <c>PhysicsEngineHolder.BuildExecutor</c> emits.
/// <para>The statistics expose the three numbers that show whether a plan was expensive: nodes expanded, wall-clock time, and the size of the region handed to the search. The pathfinding harness reads them from the client debug log, so the rendered shape is a contract: <c>Plan computed: status=... nodes=... planMs=... regionCells=... segments=...</c>, once per plan attempt, on the failed attempts as well as the successful ones.</para>
/// </summary>
public sealed class PlanStatsLoggingTests
{
    private const int FloorY = 75;

    /// <summary>1.21.7/1.21.8, so the plan runs against real block data and real collision shapes.</summary>
    private const int Protocol = 772;

    /// <summary>The region <c>CapturePlan</c> copies for a goal two blocks east of a start at (0, 75, 0): the bounding box of the two, grown by the capture's 24-block margin, is x -24..26, y 51..99, z -24..24, so 51 * 49 * 49 cells. Stated here rather than read back off the capture, which would make the assertion agree with whatever the code produced.</summary>
    private const long WalkableRegionCells = 51L * 49 * 49;

    /// <summary>The same box for a goal twenty blocks straight up: x -24..24, y 51..119, z -24..24, so 49 * 69 * 49 cells.</summary>
    private const long UnreachableRegionCells = 49L * 69 * 49;

    /// <summary>The box for a goal 38 blocks east of the start: x -24..62, y 51..99, z -24..24, so 87 * 49 * 49.</summary>
    private const long OffLatticeNearGoalRegionCells = 87L * 49 * 49;

    /// <summary>What the capture degenerates to when nothing tells it where the goal is: the 24-block margin box around the start alone, 49 * 49 * 49. This pins the fallback region size.</summary>
    private const long StartOnlyRegionCells = 49L * 49 * 49;

    [Fact]
    public void BuildExecutor_LogsThePlanStatistics_ForAPlanThatSucceeds()
    {
        var logger = new CapturingLogger();
        Harness harness = Harness.Create(logger);

        PlanCapture? capture = harness.Holder.CapturePlan(new GoalBlock(new BlockPos(2, FloorY, 0)));
        Assert.NotNull(capture);

        PathExecutor? executor = harness.Holder.BuildExecutor(
            capture!.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;

        Assert.NotNull(executor);
        string line = SingleStatsLine(logger);

        Assert.StartsWith("Plan computed: status=Success nodes=", line, StringComparison.Ordinal);
        PlanStats stats = PlanStats.Parse(line);
        Assert.True(stats.Nodes > 0, $"the search expanded no nodes: {line}");
        Assert.True(stats.PlanMs >= 0, $"planMs was negative: {line}");
        Assert.Equal(WalkableRegionCells, stats.RegionCells);
        Assert.True(stats.Segments > 0, $"a successful plan reported no segments: {line}");
    }

    /// <summary>The attempt that returns null must still report. Without this the harness would see a navigation start and never learn what the planner spent failing it, which is the case the numbers matter most for.</summary>
    [Fact]
    public void BuildExecutor_LogsThePlanStatistics_ForAPlanThatFindsNoPath()
    {
        var logger = new CapturingLogger();
        Harness harness = Harness.Create(logger);

        // Twenty blocks of open air above the floor: nothing to stand on, so the goal is refused before the search expands anything.
        PlanCapture? capture = harness.Holder.CapturePlan(new GoalBlock(new BlockPos(0, FloorY + 20, 0)));
        Assert.NotNull(capture);

        PathExecutor? executor = harness.Holder.BuildExecutor(
            capture!.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;

        Assert.Null(executor);
        string line = SingleStatsLine(logger);

        PlanStats stats = PlanStats.Parse(line);
        Assert.Equal("Failed", stats.Status);
        Assert.Equal(0, stats.Segments);
        Assert.Equal(UnreachableRegionCells, stats.RegionCells);
    }

    /// <summary>Register #3. <c>MoveToAsync</c> falls back to an internal radius-1 <c>NearGoal</c> when nothing can stand in the destination block, and that goal did not implement <c>TryGetTargetHint</c>, so <c>ApproximateGoal</c> fell through to the lattice probe, which samples offsets in <c>{-r, 0, +r}</c> for r in steps of four and therefore finds a radius-1 ball only by luck. Every miss returned <c>start</c>, and the captured region degenerated to a margin box around the START with everything past it reading as air. 38 blocks east is deliberately OFF that lattice.</summary>
    [Fact]
    public void CapturePlan_SizesTheRegionFromTheInternalNearGoalsOwnTarget()
    {
        var logger = new CapturingLogger();
        Harness harness = Harness.Create(logger);

        PlanCapture? capture = harness.Holder.CapturePlan(new NearGoal(new BlockPos(38, FloorY, 0), 1));
        Assert.NotNull(capture);

        Assert.Null(harness.Holder.BuildExecutor(
            capture!.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor);

        Assert.Equal(OffLatticeNearGoalRegionCells, PlanStats.Parse(SingleStatsLine(logger)).RegionCells);
    }

    /// <summary>The explicit region-size hint paired with the arithmetic assertion above.</summary>
    [Fact]
    public void TheInternalNearGoal_HandsBackItsOwnTarget()
    {
        var target = new BlockPos(38, FloorY, 0);

        // Through the interface deliberately: TryGetTargetHint has a default implementation, so a call on the concrete type would not compile and, more to the point, would not be the call ApproximateGoal makes.
        IGoal goal = new NearGoal(target, 1);

        Assert.True(goal.TryGetTargetHint(out BlockPos hint));
        Assert.Equal(target, hint);
    }

    /// <summary>The probe is still the fallback, and must stay one: a column goal has no concrete destination to hand back, so <c>GoalXZ</c> takes <c>IGoal</c>'s default and sizes off the probe exactly as before. Without this the test above would pass just as happily if every goal had been given a hint, which would be a different and wrong change.</summary>
    [Fact]
    public void CapturePlan_StillSizesAHintlessGoalOffTheProbe()
    {
        var logger = new CapturingLogger();
        Harness harness = Harness.Create(logger);

        PlanCapture? capture = harness.Holder.CapturePlan(new GoalXZ(38, 0));
        Assert.NotNull(capture);

        Assert.Null(harness.Holder.BuildExecutor(
            capture!.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor);

        Assert.Equal(StartOnlyRegionCells, PlanStats.Parse(SingleStatsLine(logger)).RegionCells);
    }

    private static string SingleStatsLine(CapturingLogger logger)
    {
        List<string> lines = logger.Entries
            .Where(e => e.Level == LogLevel.Debug && e.Message.StartsWith("Plan computed:", StringComparison.Ordinal))
            .Select(e => e.Message)
            .ToList();

        Assert.Single(lines);
        return lines[0];
    }

    /// <summary>The parsed form of one rendered statistics line, so the assertions name what they read.</summary>
    private readonly record struct PlanStats(string Status, long Nodes, long PlanMs, long RegionCells, int Segments)
    {
        public static PlanStats Parse(string line)
        {
            const string Prefix = "Plan computed: ";
            Assert.StartsWith(Prefix, line, StringComparison.Ordinal);

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string token in line[Prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int split = token.IndexOf('=', StringComparison.Ordinal);
                Assert.True(split > 0, $"token '{token}' is not a key=value pair in: {line}");
                values[token[..split]] = token[(split + 1)..];
            }

            Assert.Equal(
                new[] { "status", "nodes", "planMs", "regionCells", "segments" }.Order(StringComparer.Ordinal),
                values.Keys.Order(StringComparer.Ordinal));

            return new PlanStats(
                values["status"],
                long.Parse(values["nodes"], System.Globalization.CultureInfo.InvariantCulture),
                long.Parse(values["planMs"], System.Globalization.CultureInfo.InvariantCulture),
                long.Parse(values["regionCells"], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(values["segments"], System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>A live <see cref="PhysicsEngineHolder"/> over a nine-by-nine stone platform at <see cref="FloorY"/> - 1, with the player standing at its centre. The same geometry the movement probes use, minus the navigator and the session loop: <c>CapturePlan</c> and <c>BuildExecutor</c> are both callable directly.</summary>
    private sealed record Harness(PhysicsEngineHolder Holder)
    {
        public static Harness Create(ILogger logger)
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
            var holder = new PhysicsEngineHolder(services, shapes, logger);

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

            return new Harness(holder);
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
