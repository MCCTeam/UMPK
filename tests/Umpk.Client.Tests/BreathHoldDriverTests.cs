using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Client.Tests;

/// <summary>The driver half of the breathing schedule: whether the pause the plan priced is actually SPENT, and whether the two effects that suspend the lung are both honoured while it is.</summary>
/// <remarks>
/// <para>The executor can hold and can say so, but it cannot see <c>SelfState.AirSupply</c> - <c>Umpk.Pathfinding</c> does not reference <c>Umpk.Client</c> - so only the driver can decide whether a hold is working. These rows drive <c>PhysicsEngineHolder.TickNavigation</c> against a real engine in a real fixture, which is the same path the live client takes.</para>
/// <para>Drowning is suspended by <c>water_breathing</c> or <c>conduit_power</c>, and refusal system already mirrors that rule. The risk in adding a second subsystem that asks "can the body breathe" is that it is written from the potion path and silently drops the conduit arm, which no potion-only test could ever catch. So the conduit is given its OWN row, holding conduit power and nothing else, and it is the row that would fail against a narrower predicate.</para>
/// <para>The segments are hand-built rather than planned. The mechanism under test is the driver's decision loop, and a fixture that also had to plan and swim a dive would fail for a dozen reasons that are not this one; the planner and executor halves are pinned separately.</para>
/// </remarks>
public sealed class BreathHoldDriverTests
{
    private const int Protocol = 772;
    private const int FloorY = 64;

    /// <summary>The pause the plan schedules, in ticks.</summary>
    private const double PlannedHoldTicks = 60.0;

    private static readonly Identifier WaterBreathing = Identifier.Minecraft("water_breathing");
    private static readonly Identifier ConduitPower = Identifier.Minecraft("conduit_power");

    private readonly ITestOutputHelper _output;

    public BreathHoldDriverTests(ITestOutputHelper output) => _output = output;

    /// <summary>The pause is SPENT: the driver holds at the cell, the lung climbs, and the hold is reported as a breathing pause rather than as a surfacing rescue.</summary>
    /// <remarks>
    /// The last clause is the acceptance criterion for the whole feature. A route that arrives can arrive because the schedule ran or because the life-safety supervisor rescued it, and those are opposite outcomes. Counting <see cref="NavigationPreemption.Breathing"/> apart from <see cref="NavigationPreemption.Surfacing"/> is the only way to tell them apart.
    /// <para>Measured: <c>air 60 -&gt; 300, breathing 60, surfacing 0, surfaced 0</c>. The hold serves its full planned sixty ticks and fills the lung completely, with no rescue anywhere.</para>
    /// <para>The executor then reports <c>Failed</c>, and that is a property of the FIXTURE rather than of the hold: sixty ticks of floating at a one-cell bell leaves the body up in the pocket rather than on the lane, and the hand-built swim segment that follows expects to start from the lane. A planned route would have a segment shaped for the departure. Nothing this row asserts depends on the end state, and the rows that do assert it are the suspended ones, which never move.</para>
    /// </remarks>
    [Fact]
    public async Task TheDriverSpendsThePauseThePlanComputed()
    {
        Run run = await DriveAsync(startAir: 60);

        _output.WriteLine($"{run}");

        Assert.True(run.BreathingTicks > 0, $"no breathing tick was ever taken: {run}");
        Assert.Equal(0, run.SurfacingTicks);
        Assert.True(run.EndAir > run.StartAir, $"the hold must refill the lung: {run}");
    }

    /// <summary><b>The conduit ablation.</b> Conduit power ALONE, with no potion anywhere, is enough to make the pause unnecessary.</summary>
    /// <remarks>
    /// <para>This is the row a copy-paste of the potion path would fail, and it is why it exists. Drowning is suspended by <c>water_breathing</c> OR <c>conduit_power</c>, so a body under a conduit does not deplete and has nothing to refill.</para>
    /// <para>It also covers a failure that is not obvious: an immune body's lung NEVER RISES, so a hold that waited for a rise would burn its grace window and abandon a perfectly good route. The suspension has to be a release, not an abandon, and this row is what says so.</para>
    /// </remarks>
    [Fact]
    public async Task ConduitPowerAlone_MakesThePauseUnnecessary()
    {
        Run run = await DriveAsync(startAir: 60, grant: ConduitPower);

        _output.WriteLine($"conduit only: {run}");

        // Released at once, not waited out and not abandoned.
        Assert.True(run.BreathingTicks <= 2, $"a suspended lung must not be waited on: {run}");
        Assert.Equal(0, run.SurfacedTicks);
        Assert.Equal(PathExecutorState.Complete, run.State);
    }

    /// <summary>The same, on the potion arm, so the pair shows the predicate is a real OR.</summary>
    [Fact]
    public async Task WaterBreathingPotionAlone_MakesThePauseUnnecessary()
    {
        Run run = await DriveAsync(startAir: 60, grant: WaterBreathing);

        _output.WriteLine($"potion only: {run}");

        Assert.True(run.BreathingTicks <= 2, $"a suspended lung must not be waited on: {run}");
        Assert.Equal(0, run.SurfacedTicks);
        Assert.Equal(PathExecutorState.Complete, run.State);
    }

    /// <summary>The ablation is VERIFIED TO ABLATE: with neither effect held, the same fixture really does wait.</summary>
    /// <remarks>Without this the two rows above prove nothing - a hold that never ran for any reason would satisfy both of them. The contrast is the evidence.</remarks>
    [Fact]
    public async Task WithNeitherEffect_TheSameFixtureActuallyWaits()
    {
        Run plain = await DriveAsync(startAir: 60);
        Run conduit = await DriveAsync(startAir: 60, grant: ConduitPower);
        Run potion = await DriveAsync(startAir: 60, grant: WaterBreathing);

        _output.WriteLine($"  none    : {plain}");
        _output.WriteLine($"  conduit : {conduit}");
        _output.WriteLine($"  potion  : {potion}");

        Assert.True(
            plain.BreathingTicks > conduit.BreathingTicks + 10,
            $"the conduit row must differ from the unprotected one by a real wait: {plain} vs {conduit}");
        Assert.True(
            plain.BreathingTicks > potion.BreathingTicks + 10,
            $"the potion row must differ from the unprotected one by a real wait: {plain} vs {potion}");
    }

    /// <summary>The safety invariant: a hold whose lung FALLS below what it began with is abandoned at once, whatever any wall clock says.</summary>
    /// <remarks>The wall-clock caps inherited from the emergency ascent are not an air clock. A body that holds at a cell that turns out to be wet drains at one a tick, so a hold entered on air 40 reaches the drowning point on tick 60 - inside both <c>MaxSurfacingTicks</c> and <c>MaxSurfacingTicksTotal</c>. Only the lung can see this.</remarks>
    [Fact]
    public async Task AHoldWhoseLungFalls_IsAbandonedAtOnce()
    {
        Run run = await DriveAsync(startAir: 60, sealedLid: true);

        _output.WriteLine($"sealed lid over the hold cell: {run}");

        // Abandoned, not held to a clock: the driver raises Surfaced so the navigator replans on the lung the body actually has.
        Assert.True(run.SurfacedTicks > 0, $"a draining hold must be abandoned: {run}");
        Assert.True(
            run.BreathingTicks < PhysicsEngineHolder.BreathHoldGraceTicks,
            $"the abandon must not wait out the grace window when the lung is actively falling: {run}");
    }

    /// <summary>A lung that dips and then carries on rising is a hold that is WORKING, and must not be abandoned for it.</summary>
    /// <remarks>
    /// <para><b>Measured live at the same one-cell head bells, over four runs of E30, E31 and E32:</b></para>
    /// <code>
    /// ABANDONED (the lung started falling again after gaining): held 3 ticks, air 150 -&gt; 153 (best 154). ABANDONED ... held 7 ticks, air 181 -&gt; 179 (best 184). ABANDONED ... held 4 ticks, air  51 -&gt;  48 (best  54).
    /// </code>
    /// <para>Every one of those cells WORKS: the same bells filled the lung to 300 on other runs of the same rows. A body at a one-cell head bell sits with its eye within hundredths of the water line and bobs across it, so it alternates refill ticks with drain ticks - the SUCCESSFUL holds average 3.2 to 3.5 a tick rather than a clean four, which is that same bob not going badly - and the number the driver reads is the server's, arriving as entity metadata over a network.</para>
    /// <para><b>No per-tick bound can be written honestly here.</b> The last of those rows lost NINE of lung inside six ticks, and vanilla drains one a tick, so that number did not come from the lung: it is the client's own prediction being overwritten by a server frame. The client's eye test and the server's disagree for a few ticks at a time at a bell, and the server wins, in jumps. A bound wide enough to absorb that is wide enough to rubber-stamp a cell that is genuinely failing.</para>
    /// <para>So the judgement is made once a WINDOW instead of once a tick: over <see cref="PhysicsEngineHolder.BreathHoldGraceTicks"/>, is the lung higher than it was at the window's start? Jitter cancels over twenty-four ticks and a trend does not. A working cell climbs at a measured 3.2 to 3.5 a tick and clears the bar by seventy; a dead one drains one a tick and fails it by twenty-four, which is the sealed-lid row above, unchanged at 23 ticks.</para>
    /// </remarks>
    [Fact]
    public async Task AHoldThatDipsAndKeepsRising_IsNotAbandoned()
    {
        // E31's live sequence: one refill tick, one drain tick, repeating. Net +3 every two.
        Run bobbing = await DriveAsync(startAir: 150, scriptedAirSteps: [4, -1]);
        _output.WriteLine($"bobbing at the bell: {bobbing}");

        Assert.Equal(0, bobbing.SurfacedTicks);
        Assert.True(
            bobbing.BreathingTicks > PhysicsEngineHolder.BreathHoldGraceTicks,
            $"a hold whose lung is climbing must outlast the grace window: {bobbing}");
        Assert.True(bobbing.EndAir > bobbing.StartAir, $"and it must actually have gained: {bobbing}");

        // E30's fourth bell: `air 64 -> 59 (best 68)` inside SIX ticks. Nine of lung cannot leave a body in six ticks at vanilla's one a tick, so that is the client's own prediction being overwritten by a server frame - and it is why no per-tick bound could be written honestly. Over a window the same cell is plainly gaining.
        Run jumped = await DriveAsync(startAir: 150, scriptedAirSteps: [4, 4, 4, -9, 4, 4, 4, 4]);
        _output.WriteLine($"server correction: {jumped}");

        Assert.Equal(0, jumped.SurfacedTicks);
        Assert.True(jumped.EndAir > jumped.StartAir, $"a corrected but gaining lung must hold: {jumped}");
    }

    /// <summary>The ARRIVAL at a bell is not a failure: a lung that falls the whole way up into the pocket, and only then starts gaining, must be given the grace window rather than the losing-ground arm.</summary>
    /// <remarks>
    /// <para>Measured live on course row E30's third bell, which the losing-ground arm threw away while the body was still climbing into perfectly good air:</para>
    /// <code>
    /// ABANDONED (the lung is losing ground against the pause's own start): held 6 ticks,
    ///     air 192 -&gt; 187 (best 192).
    /// </code>
    /// <para>A best that never moved off the entry is the signature: not one refill tick had landed yet. The body reaches a bell still submerged and has to rise into the pocket first, and the lung falls the whole way up. Under the window rule the arrival is simply part of the first window and is judged with it, which is the outcome this row pins.</para>
    /// </remarks>
    [Fact]
    public async Task AHoldThatFallsOnTheWayIntoThePocket_IsGivenTheGraceWindow()
    {
        // Five drain ticks climbing into the pocket, then three refill ticks: the lung is six below its start before the first gain lands, which is past the losing-ground bound, and net +7 per cycle once it is in.
        Run run = await DriveAsync(startAir: 150, scriptedAirSteps: [-1, -1, -1, -1, -1, 4, 4, 4]);

        _output.WriteLine($"falling into the pocket: {run}");

        Assert.Equal(0, run.SurfacedTicks);
        Assert.True(run.EndAir > run.StartAir, $"the pause must be allowed to reach the air: {run}");
    }

    /// <summary>The other side of the window: a lung that is NET LOSING at the bell is still abandoned, on the first window that closes on it.</summary>
    /// <remarks>One refill tick against nine drain ticks is a cell that is not keeping up, and it is the discriminator for the row above - widen the judgement to anything that swallows this and the abandon arm stops existing. The cost is asserted rather than assumed: one window of drain, at one a tick, and the hard floor still catches a lung that runs out inside a window.</remarks>
    [Fact]
    public async Task AHoldWhoseLungIsNetLosing_IsStillAbandoned()
    {
        // One refill tick against nine drain ticks: net minus five every ten, which is a cell that is not keeping up.
        Run run = await DriveAsync(startAir: 150, scriptedAirSteps: [4, -1, -1, -1, -1, -1, -1, -1, -1, -1]);

        _output.WriteLine($"net-losing bell: {run}");

        Assert.True(run.SurfacedTicks > 0, $"a net-losing hold must still be abandoned: {run}");
        Assert.True(
            run.BreathingTicks < PhysicsEngineHolder.BreathHoldGraceTicks,
            $"and not by waiting out the grace window: {run}");

        // The guarantee the window actually gives, asserted rather than assumed: the judgement lands at the first window boundary and the lung drains one a tick, so a pause that is going wrong can never cost the body more than one window's worth of air before it is thrown away.
        Assert.True(
            run.EndAir > run.StartAir - PhysicsEngineHolder.BreathHoldGraceTicks,
            $"an abandoned pause must not have cost the body a real part of its lung: {run}");
    }

    /// <summary>Every way a hold can end reports itself, in the one shape the course reads.</summary>
    /// <remarks>
    /// <para>The acceptance criterion for this feature is per-hold telemetry, and the course counts it out of the client transcript, so the exact wording is load bearing rather than cosmetic. Three exits have to be covered and one of them was silently missing when first written: a hold that runs its full planned count is released INSIDE the executor, so the driver never decided anything and never logged. That is the ORDINARY, successful case - the one a passing row is made of - and it was the only one producing no evidence at all.</para>
    /// <para>All three lines share <c>held N ticks, air A -&gt; B</c> so one pattern counts holds, and the abandon says <c>ABANDONED</c> so it can be counted apart from the two that worked.</para>
    /// </remarks>
    [Fact]
    public async Task EveryWayAHoldCanEnd_ReportsItself()
    {
        var log = new CapturingLoggerProvider();

        Run completed = await DriveAsync(startAir: 290, logger: log.Logger);
        _output.WriteLine($"nearly-full lung: {completed}");

        // A pause PRICED shorter than the refill it would need, so the EXECUTOR releases on its own count and the driver never decides anything. This is the ordinary successful case and it was the one exit that logged nothing when this file was first written.
        //
        // The lung is deliberately high (200, needing 25 ticks against a 20-tick plan) rather than low. At air 20 the life-safety supervisor pre-empts the route before the scheduled pause can run at all - measured, `breathing 0, surfacing 37` - which is a real interaction worth knowing about but not the exit this row is trying to reach.
        Run onTheClock = await DriveAsync(startAir: 200, logger: log.Logger, holdTicks: 20.0);
        _output.WriteLine($"short plan:       {onTheClock}");

        Run abandoned = await DriveAsync(startAir: 60, sealedLid: true, logger: log.Logger);
        _output.WriteLine($"sealed lid:       {abandoned}");

        foreach (string line in log.Lines)
            _output.WriteLine($"  LOG {line}");

        Assert.Contains(log.Lines, line => line.Contains("Breath hold at", StringComparison.Ordinal));

        // Every breath-hold line carries the shape the course's telemetry assertion matches on.
        foreach (string line in log.Lines.Where(l => l.Contains("Breath hold at", StringComparison.Ordinal)))
            Assert.Matches(@"held \d+ ticks, air -?\d+ -> -?\d+", line);

        // And an abandon is distinguishable from a hold that worked.
        Assert.Contains(log.Lines, line => line.Contains("ABANDONED", StringComparison.Ordinal));

        // All three exits must be reported by name.
        Assert.Contains(log.Lines, line => line.Contains("released (the lung is full)", StringComparison.Ordinal));
        Assert.Contains(log.Lines, line => line.Contains("completed (the plan's own count)", StringComparison.Ordinal));
    }

    /// <summary>The smallest logger that remembers what it was told, so a row can assert on telemetry.</summary>
    private sealed class CapturingLoggerProvider
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines => _lines;

        public ILogger Logger => new Capturing(_lines);

        private sealed class Capturing(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => sink.Add(formatter(state, exception));
        }
    }

    /// <summary>What one driven run did.</summary>
    private readonly record struct Run(
        int StartAir, int EndAir, int BreathingTicks, int SurfacingTicks, int SurfacedTicks, PathExecutorState State)
    {
        public override string ToString()
            => $"air {StartAir} -> {EndAir}, breathing {BreathingTicks}, surfacing {SurfacingTicks}, "
                + $"surfaced {SurfacedTicks}, state {State}";
    }

    /// <summary>Drives the holder's own navigation tick over two hand-built segments whose FIRST carries the scheduled pause, and counts what the driver did.</summary>
    /// <param name="startAir">The lung the body arrives at the hold cell with.</param>
    /// <param name="grant">A breath effect to hold, or null for an unprotected body.</param>
    /// <param name="sealedLid">Whether to seal the cell above the hold, so the body cannot breathe there and the lung falls.</param>
    private async Task<Run> DriveAsync(
        int startAir, Identifier? grant = null, bool sealedLid = false, ILogger? logger = null,
        double holdTicks = PlannedHoldTicks, int[]? scriptedAirSteps = null)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartBellAsync(sealedLid, logger);
        await using (loop)
        {
            SelfState self = harness.State.Self;
            self.AirSupply = startAir;
            if (grant is { } effect)
                Grant(harness, effect);

            var execution = new PathExecutionContext(
                holder.WorldView!, PhysicsProfile.ForProtocol(Protocol), PhysicsConditions.Default);

            var segments = new List<PathSegment>
            {
                Segment(0, 1, holdTicks),
                Segment(1, 2, 0.0),
            };

            var executor = new PathExecutor(execution, segments);
            holder.BeginNavigation();

            int breathing = 0;
            int surfacing = 0;
            int surfaced = 0;
            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 400; tick++)
            {
                // Vanilla's baseTick order: the lung moves before the body does.
                if (scriptedAirSteps is { Length: > 0 } steps)
                {
                    // The lung is driven from the script instead of predicted, because the case being measured is one the client's own prediction cannot produce: the air the DRIVER reads is the server's, arriving as entity metadata, and a body bobbing about the surface line at a bell alternates a refill tick with a drain tick.
                    self.AirSupply = Math.Clamp(
                        self.AirSupply + steps[tick % steps.Length], 0, self.MaxAirSupply);
                }
                else
                    holder.TickAirSupply();

                NavigationTickOutcome outcome = holder.TickNavigation(executor);
                switch (outcome.Preemption)
                {
                    case NavigationPreemption.Breathing:
                        breathing++;
                        break;
                    case NavigationPreemption.Surfacing:
                        surfacing++;
                        break;
                    case NavigationPreemption.Surfaced:
                        surfaced++;
                        break;
                    default:
                        break;
                }

                state = outcome.State;
                if (outcome.Preemption == NavigationPreemption.Surfaced
                    || state != PathExecutorState.InProgress)
                    break;

            }

            return new Run(startAir, self.AirSupply, breathing, surfacing, surfaced, state);
        }
    }

    private static PathSegment Segment(int fromX, int toX, double hold) => new()
    {
        Start = new Vec3d(fromX + 0.5, FloorY, 0.5),
        End = new Vec3d(toX + 0.5, FloorY, 0.5),
        StartFeetY = FloorY,
        EndFeetY = FloorY,
        MoveType = MoveType.Swim,
        PlannedTickCost = 10.0,
        BreathHoldTicks = hold,
    };

    /// <summary>Ten minutes of the named effect on the session's own table, which is what <c>effect give PathBot minecraft:&lt;id&gt; 600 0 true</c> lands as.</summary>
    private static void Grant(ApplierHarness harness, Identifier effect)
    {
        Assert.True(
            JavaGameData.Registries(Protocol).MobEffects.TryGetNetworkId(effect, out int networkId),
            $"the {Protocol} registry must be able to name {effect}");
        harness.State.Self.ApplyEffect(
            new ActiveEffect(networkId, Amplifier: 0, Duration: 12000, Flags: 0)
            {
                AppliedAtTick = harness.State.SessionTick,
            });
    }

    /// <summary>A short flooded lane with a one-cell air bell over the cell the hold is taken at: the E31 pausebell shape, reduced to the two cells the driver's decision actually reads.</summary>
    /// <param name="sealedLid">Fills the bell in, so the hold cell cannot breathe and the lung falls.</param>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)>
        StartBellAsync(bool sealedLid, ILogger? logger = null)
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
        var holder = new PhysicsEngineHolder(services, shapes, logger ?? NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        int stoneState = stone.DefaultStateId;
        int waterState = water.MinStateId;

        for (int x = -4; x <= 12; x++)
            for (int y = FloorY - 2; y <= FloorY + 8; y++)
                for (int z = -3; z <= 3; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        for (int x = -2; x <= 8; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), waterState);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), waterState);
        }

        // The bell: one air cell over the head cell at the hold's destination, x=1.
        if (!sealedLid)
            world.SetBlockStateId(new BlockPos(1, FloorY + 2, 0), 0);

        harness.State.Registries = JavaGameData.Registries(Protocol);
        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        holder.TickIdle();
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }
}
