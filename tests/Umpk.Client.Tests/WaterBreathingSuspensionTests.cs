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
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Client.Tests;

/// <summary>The offline validators for course rows M5, M7, and M8 cover the water-breathing suspension, the revoke-replan, and the proof that the life-safety supervisor's HEALTH arm keeps running while its breath arm is suspended.</summary>
/// <remarks>
/// <para>Every row runs in the same sealed bore, which is course row M5's own geometry reduced to the two features that decide the answer: more submerged travel than one lung buys, and no air anywhere inside it. E3 is the row that must keep refusing that on a plain lung; M5 is the row that must stop refusing it once the planner reads the effect, and the pair is what separates "breath-aware" from "water-averse".</para>
/// <para><b>The collar.</b> Rows that need the supervisor to ACT carry one air cell in the lid with stone above it, exactly as <see cref="LifeSafetySupervisorTests"/> does and for the same reason because a trip only fires where a vertical climb can reach air. It is one cell, so a two-cell-tall body can never stand in it and the route is unchanged.</para>
/// </remarks>
public sealed class WaterBreathingSuspensionTests
{
    private const int Protocol = 772;
    private const int FloorY = 70;

    /// <summary>Cells of sealed bore. One lung is 300 ticks and a bottom-walk is ~10 ticks a block.</summary>
    private const int BoreLength = 90;

    private static readonly Identifier WaterBreathing = Identifier.Minecraft("water_breathing");

    private readonly ITestOutputHelper _output;

    public WaterBreathingSuspensionTests(ITestOutputHelper output) => _output = output;

    /// <summary>E3's answer and M5's, side by side on the same bore: refused on a plain lung, planned and breath-validated once water breathing is held, with the suspension recorded on the plan.</summary>
    [Fact]
    public async Task M5_TheSealedBore_PlansAndValidatesOnlyWithWaterBreathing()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartBoreAsync();
        await using (loop)
        {
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;

            PlanCapture? plain = holder.CapturePlan(Goal);
            Assert.NotNull(plain);
            Assert.Null(holder.BuildExecutor(plain.Value, PathfinderOptions.Default, CancellationToken.None));

            GrantWaterBreathing(harness);

            PlanCapture? granted = holder.CapturePlan(Goal);
            Assert.NotNull(granted);
            Assert.True(granted.Value.Capabilities.TryGetEffect(WaterBreathing, out CapabilityEffect held));
            _output.WriteLine($"granted: remaining={held.RemainingTicks} estimated={held.DurationIsEstimated}");

            PlannedRoute? route = holder.BuildExecutor(granted.Value, PathfinderOptions.Default, CancellationToken.None);
            Assert.NotNull(route);
            Assert.True(route.Value.BreathSuspended);
            Assert.Contains(WaterBreathing, route.Value.Executor.Context.DependsOnEffects);
            _output.WriteLine($"suspended route: {route.Value.Executor.TotalSegments} segments");
        }
    }

    /// <summary>The supervisor's breath arm is what the suspension suspends, and nothing else. On a lung of ONE tick, in a bore whose collar makes a surfacing legal, an unsuspended supervisor pre-empts on the first tick; the suspended one does not.</summary>
    [Fact]
    public async Task M5_TheBreathArmIsSilentUnderTheSuspension_AndFiresWithoutIt()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartBoreAsync(collarToX: 8);
        await using (loop)
        {
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            GrantWaterBreathing(harness);

            PathExecutor suspended = Route(holder).Executor;
            Assert.True(suspended.Context.BreathSuspended);

            var profile = PhysicsProfile.ForProtocol(Protocol);
            var supervisor = new LifeSafetySupervisor(profile);
            supervisor.Reset(harness.State.Self.Health);
            harness.State.Self.AirSupply = 1;

            Assert.Equal(
                LifeSafetyAction.None,
                supervisor.Evaluate(
                    holder.EngineState!.Value,
                    harness.State.Self,
                    holder.WorldView!,
                    suspended,
                    waterBreathingHeld: true));

            // The control, on the identical tick: the same lung, the same bore, the same collar, with the suspension off. This is what the breath arm does when nothing has suspended it.
            var control = new LifeSafetySupervisor(profile);
            control.Reset(harness.State.Self.Health);
            Assert.Equal(
                LifeSafetyAction.Surfacing,
                control.Evaluate(
                    holder.EngineState!.Value,
                    harness.State.Self,
                    holder.WorldView!,
                    suspended,
                    waterBreathingHeld: false));
        }
    }

    /// <summary>Course row M7. The bore is crossed under a suspension, the effect is pulled mid-swim, and three things follow on the tick the revoke lands: the navigation tick reports the capability loss rather than driving the body on, the replan with the CURRENT capabilities refuses the continuation, and the supervisor's breath arm - live again the moment the effect is gone, not when a replan says so - surfaces the body.</summary>
    /// <remarks>The order matters and is the whole design. The plan's suspension is a statement about the capture; the supervisor's is a statement about NOW, and the second is what keeps a body alive between the revoke and whatever the navigator manages to do about it. A suspension that were carried only by the frozen plan would leave the breath arm off for the entire remaining route of a potion that no longer exists.</remarks>
    [Fact]
    public async Task M7_RevokingMidSwim_RefusesTheContinuationAndSurfaces()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartBoreAsync(collarToX: 8);
        await using (loop)
        {
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            GrantWaterBreathing(harness);

            PlannedRoute route = Route(holder);
            Assert.True(route.BreathSuspended);
            holder.BeginNavigation();

            // A few ticks of the crossing under the effect: the executor owns every one of them.
            for (int tick = 0; tick < 10; tick++)
                Assert.Equal(NavigationPreemption.None, holder.TickNavigation(route.Executor).Preemption);

            // The revoke, exactly as `effect clear PathBot water_breathing` lands it: the entity applier drops the effect out of self state and nothing else changes.
            harness.State.Self.ClearEffects();

            NavigationTickOutcome outcome = holder.TickNavigation(route.Executor);
            Assert.Equal(NavigationPreemption.CapabilityLost, outcome.Preemption);

            // What the navigator does with it: replan from where the body now is, with the capabilities it now has. Ninety blocks of bore on one lung is not a route.
            PlanCapture? replan = holder.CapturePlan(Goal);
            Assert.NotNull(replan);
            Assert.False(replan.Value.Capabilities.TryGetEffect(WaterBreathing, out _));
            Assert.Null(holder.BuildExecutor(replan.Value, PathfinderOptions.Default, CancellationToken.None));

            // And the in-flight backstop, read on the SAME executor whose frozen context still says the breath dimension was suspended. The suspension needs both halves - the plan's statement about the capture and the world's statement about now - so with the effect gone the arm is live, and `false` here is exactly what the navigation tick now passes.
            harness.State.Self.AirSupply = 1;
            var supervisor = new LifeSafetySupervisor(PhysicsProfile.ForProtocol(Protocol));
            supervisor.Reset(harness.State.Self.Health);
            Assert.True(route.Executor.Context.BreathSuspended);
            Assert.Equal(
                LifeSafetyAction.Surfacing,
                supervisor.Evaluate(
                    holder.EngineState!.Value,
                    harness.State.Self,
                    holder.WorldView!,
                    route.Executor,
                    waterBreathingHeld: false));

            // The counterfactual, on the identical tick: had the potion NOT been revoked, the same executor and the same lung would still be silent. That is what makes the line above a consequence of the revoke rather than of anything else in the fixture.
            var counterfactual = new LifeSafetySupervisor(PhysicsProfile.ForProtocol(Protocol));
            counterfactual.Reset(harness.State.Self.Health);
            Assert.Equal(
                LifeSafetyAction.None,
                counterfactual.Evaluate(
                    holder.EngineState!.Value,
                    harness.State.Self,
                    holder.WorldView!,
                    route.Executor,
                    waterBreathingHeld: true));
        }
    }

    /// <summary>Course row M8. The effect is held for the whole crossing and never revoked - breath is not the variable - and three cells of the bore's own floor turn to magma under the body. The supervisor's edge-triggered health crossing has to fire while the breath arm is still fully suspended, which is what proves the two triggers are independent rather than the health arm riding on the breath trip M7 already covers.</summary>
    [Fact]
    public async Task M8_AMagmaPatchUnderTheSuspension_PreEmptsOnHealth()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartBoreAsync(collarToX: 8);
        await using (loop)
        {
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            harness.State.Self.Health = 20f;
            GrantWaterBreathing(harness);

            PlannedRoute route = Route(holder);
            Assert.True(route.BreathSuspended);
            holder.BeginNavigation();

            Assert.Equal(NavigationPreemption.None, holder.TickNavigation(route.Executor).Preemption);

            // The world change the plan never saw: three cells of the bore's floor swapped to magma under the water, at 15% of the crossing. Fire resistance is deliberately NOT held, so the hot floor keeps hurting.
            Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
            Assert.True(blocks.TryGetValue(Identifier.Minecraft("magma_block"), out BlockDefinition? magma));
            for (int x = 12; x <= 14; x++)
                harness.State.World.SetBlockStateId(new BlockPos(x, FloorY - 1, 0), magma.DefaultStateId);

            // The lung is untouched: with water breathing held it never drains at all, so nothing here can be the breath arm firing by another name.
            Assert.Equal(harness.State.Self.MaxAirSupply, harness.State.Self.AirSupply);
            harness.State.Self.Health = 5f;

            Assert.Equal(
                NavigationPreemption.Surfacing, holder.TickNavigation(route.Executor).Preemption);
        }
    }

    /// <summary>Course row M8 as the LIVE course actually builds it, which is the row above with its one concession removed: the bore carries NO collar anywhere, so no vertical climb from any cell of it reaches air. The supervisor has to keep the body alive with the only escape it has, which is the route it has already walked.</summary>
    /// <remarks>
    /// <para>The row above passes because <c>StartBoreAsync(collarToX: 8)</c> punches nine cells of breathable collar into the lid, and the health arm's <c>CanSurface</c> gate reads that collar. M8's bore has none, so the gate reads false for the whole 24-block leg and the health crossing must use the route already traveled as its escape.</para>
    /// <para><b>The hot floor is the server's, not the engine's.</b> <c>PlayerPhysics</c> models no damage at all - hot-floor damage arrives as <c>set_health</c> frames - so the fixture writes it, from the rule that hot floors hurt only while the body is resting on them <b>only while grounded</b>. The rate is the harness's own live measurement, 4 HP over a 3-cell swap of roughly thirty ticks of bottom-walk, taken at the slow end as one heart every <see cref="HotFloorPeriodTicks"/> ticks. Nothing in the supervisor reads any of it; it reads <see cref="SelfState.Health"/>, which is exactly what the session writes.</para>
    /// <para><b>The refusal at the end is the row's other half, and it arrived separately.</b> When this row was first written it recorded what the replan returned and asserted nothing about it, because <see cref="Umpk.Pathfinding.Moves.Impl.MoveSwim"/> asked only whether the destination was a passable water column and never whether the cell's own floor was a hazard: in a two-cell flooded lane, where a swim node's feet rest ON the floor, it planned the identical crossing again as a run of 81 <c>Swim</c> segments sitting on the magma. <c>MoveHelper.CanSwimTo</c> asks the floor question now, so the crossing has no arm left - the bottom-walk was already refused by the hazard set, which is what made the swim arm the only survivor - and M8's own <c>final_expected</c>, refuse-by-abort, is complete: the retreat delivers the abort and the replan delivers the refusal.</para>
    /// </remarks>
    [Fact]
    public async Task M8_ASealedBore_KeepsTheBodyAliveWithNoVerticalEscape()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartBoreAsync();
        await using (loop)
        {
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            harness.State.Self.Health = 20f;
            GrantWaterBreathing(harness);

            PlannedRoute route = Route(holder);
            Assert.True(route.BreathSuspended);

            // The bore really is sealed, cell by cell: this is the whole difference from the row above.
            var profile = PhysicsProfile.ForProtocol(Protocol);
            for (int x = 0; x <= BoreLength; x++)
                Assert.False(
                    BreathModel.EscapeTicks(holder.WorldView!, x, FloorY, 0, profile).IsKnown,
                    $"the bore must be sealed at x={x}, or the vertical arm answers this row instead");

            holder.BeginNavigation();
            Assert.Equal(NavigationPreemption.None, holder.TickNavigation(route.Executor).Preemption);

            // The world change the plan never saw, sized as the live row sizes it: twenty cells of the bore's own floor swapped to magma under the water.
            Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
            Assert.True(blocks.TryGetValue(Identifier.Minecraft("magma_block"), out BlockDefinition? magma));
            int magmaState = magma.DefaultStateId;
            for (int x = HazardFromX; x <= HazardToX; x++)
                harness.State.World.SetBlockStateId(new BlockPos(x, FloorY - 1, 0), magmaState);

            NavigationPreemption trip = NavigationPreemption.None;
            NavigationPreemption release = NavigationPreemption.None;
            int tripTick = -1;
            float healthAtTrip = 0f;
            double xAtTrip = 0;
            int hotTicks = 0;
            int tick = 0;
            for (; tick < 2000; tick++)
            {
                NavigationTickOutcome outcome = holder.TickNavigation(route.Executor);
                if (tripTick < 0 && outcome.Preemption != NavigationPreemption.None)
                {
                    trip = outcome.Preemption;
                    tripTick = tick;
                    healthAtTrip = harness.State.Self.Health;
                    xAtTrip = holder.EngineState!.Value.Position.X;
                }
                else if (tripTick >= 0 && outcome.Preemption != trip)
                {
                    release = outcome.Preemption;
                    break;
                }

                if (tripTick >= 0 && (tick - tripTick) % 20 == 0)
                {
                    PhysicsState now = holder.EngineState!.Value;
                    _output.WriteLine(
                        $"  retreat+{tick - tripTick}: x={now.Position.X:F2} y={now.Position.Y:F3} "
                        + $"health={harness.State.Self.Health}");
                }

                PhysicsState state = holder.EngineState!.Value;
                bool hot = RestsOnHotFloor(harness, state, magmaState);
                hotTicks = hot ? hotTicks + 1 : 0;
                if (hotTicks > 0 && hotTicks % HotFloorPeriodTicks == 0)
                    harness.State.Self.Health -= 1f;

                if (harness.State.Self.Health <= 0f || outcome.State != PathExecutorState.InProgress)
                    break;

            }

            PhysicsState end = holder.EngineState!.Value;
            Identifier endSupport = harness.State.World.GetBlock(SupportUnder(end.Position)).Block.Id;
            _output.WriteLine(
                $"ticks={tick} trip={trip}@{tripTick} healthAtTrip={healthAtTrip} xAtTrip={xAtTrip:F2} "
                + $"release={release} health={harness.State.Self.Health} x={end.Position.X:F2} "
                + $"support={endSupport}");

            Assert.True(
                tripTick >= 0,
                $"the supervisor never took a single tick: the body crossed {HazardToX - HazardFromX + 1} "
                + $"cells of hot floor from 20 health down to {harness.State.Self.Health} with no vertical "
                + "escape anywhere and no intervention at all");

            // The escape is the RETREAT and not the climb: there is nothing above to climb to.
            Assert.Equal(NavigationPreemption.Retreating, trip);
            Assert.Equal(NavigationPreemption.Retreated, release);

            Assert.True(
                harness.State.Self.Health > 0f,
                $"the body died on the hot floor after {tick} ticks (trip {trip} at tick {tripTick})");

            // It went BACK along the route it had walked, and it is standing clear of the hazard rather than merely hovering over it: the settle at the end is on ordinary floor.
            Assert.True(
                end.Position.X < xAtTrip,
                $"the escape has to go BACK along the route the body has already walked: it tripped at "
                + $"x={xAtTrip:F2} and finished at x={end.Position.X:F2}");
            Assert.True(
                end.Position.X < HazardFromX,
                $"the body finished at x={end.Position.X:F2}, still inside the hot strip that starts at "
                + $"x={HazardFromX}");
            Assert.Equal(Identifier.Minecraft("stone"), endSupport);

            // The health arithmetic, stated rather than implied. The crossing fires AT the floor by definition, so no retreat can end above it; what the retreat buys is that health stops falling. It cost one heart here, and that heart is the failed settle on the strip's own first cell - the anchor's cell - which is what sent the retreat a cell further back.
            Assert.True(
                harness.State.Self.Health >= healthAtTrip - 1f,
                $"the retreat trades a bounded amount of health for the escape: it tripped at "
                + $"{healthAtTrip} and released at {harness.State.Self.Health}");

            // M8's own `final_expected` is refuse-by-abort. The ABORT is what the retreat delivers: the executor is abandoned and the navigator replans from where the body now is, with the hazard in the capture this time. The REFUSAL is the planner's, and it needs both halves of the capture to be true - the hazard has to be visible in it, and no move family may cross it.
            PlanCapture? replan = holder.CapturePlan(Goal);
            Assert.NotNull(replan);
            Assert.Equal(
                Identifier.Minecraft("magma_block"),
                replan.Value.Planning.GetBlock(new BlockPos(HazardFromX + 4, FloorY - 1, 0)).Block.Id);

            PlannedRoute? again = holder.BuildExecutor(replan.Value, PathfinderOptions.Default, CancellationToken.None);
            _output.WriteLine(
                again is { } found
                    ? $"replan: {found.Executor.TotalSegments} segments"
                    : "replan: refused");
            Assert.True(
                again is null,
                "the replan planned a route back across a strip that had just taken the body to the "
                + "health floor: the bottom-walk arm is refused by the hazard set, so a route can only "
                + "exist if the swim arm is still blind to the floor it rests on");
        }
    }

    /// <summary>The other side of the same crossing, and the reason the vertical arm was left exactly as it was: where a climb CAN reach air, it is still what the health crossing does. Same bore, same hazard, same crossing, one collar in the lid.</summary>
    /// <remarks>A climb off the floor stops a hot floor hurting - and unlike a retreat it also buys the lung, which is the other thing that kills a body in a flooded bore. <c>M8_AMagmaPatchUnderTheSuspension_PreEmptsOnHealth</c> above is the same reading taken through the navigation tick; this one reads the supervisor directly, so the two arms can be seen choosing on the same tick from the same state.</remarks>
    [Fact]
    public async Task M8_AVerticalEscapeStillPrefersSurfacing_OverTheRetreat()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartBoreAsync(collarToX: 8);
        await using (loop)
        {
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            harness.State.Self.Health = 20f;
            GrantWaterBreathing(harness);

            PlannedRoute route = Route(holder);
            holder.BeginNavigation();
            for (int tick = 0; tick < 8; tick++)
                Assert.Equal(NavigationPreemption.None, holder.TickNavigation(route.Executor).Preemption);

            // The collar is what separates this row from the one above, so it is asserted and not assumed.
            var profile = PhysicsProfile.ForProtocol(Protocol);
            PhysicsState state = holder.EngineState!.Value;
            Assert.True(
                BreathModel.EscapeTicks(
                    holder.WorldView!,
                    (int)Math.Floor(state.Position.X),
                    (int)Math.Floor(state.Position.Y),
                    0,
                    profile).IsKnown,
                "the collared column must offer a vertical escape, or this row is the sealed one again");

            harness.State.Self.Health = 5f;

            Assert.Equal(NavigationPreemption.Surfacing, holder.TickNavigation(route.Executor).Preemption);
        }
    }

    /// <summary>The ceiling is real: a retreat whose every settle finds the ground still hurting it walks back through its whole history and then stops, rather than owning the engine for the rest of the session.</summary>
    /// <remarks>The adversarial shape the settle window alone cannot bound, and the mirror of <c>LifeSafetySupervisorTests.Surfacing_IsStillBoundedWhileTheAirIsRisingTooSlowlyToArrive</c>: the body is hurt one point every ten ticks for as long as the retreat runs, so the window resets forever and <see cref="LifeSafetySupervisor.SettleTicksLeft"/> never reaches zero. Health is fed by hand, because the point is the SUPERVISOR's arithmetic and not any world's.</remarks>
    [Fact]
    public async Task ARetreatThatIsNeverSafeIsStillBounded()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartBoreAsync();
        await using (loop)
        {
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            harness.State.Self.Health = 20f;
            GrantWaterBreathing(harness);

            PlannedRoute route = Route(holder);
            holder.BeginNavigation();
            for (int tick = 0; tick < 60; tick++)
                Assert.Equal(NavigationPreemption.None, holder.TickNavigation(route.Executor).Preemption);

            harness.State.Self.Health = 6f;
            Assert.Equal(NavigationPreemption.Retreating, holder.TickNavigation(route.Executor).Preemption);

            int held = 1;
            for (int tick = 0; tick < 1000; tick++)
            {
                // A hit inside every settle window, at the smallest fall that still reads as one. The size is irrelevant to the arithmetic under test - the window resets on ANY fall below the worst health seen - and keeping it small is what stops the row ending on a dead player instead of on the clock it is measuring.
                if (tick % (LifeSafetySupervisor.RetreatSettleTicks - 5) == 0)
                    harness.State.Self.Health -= 0.01f;

                NavigationPreemption preemption = holder.TickNavigation(route.Executor).Preemption;
                if (preemption == NavigationPreemption.Retreated)
                    break;

                Assert.Equal(NavigationPreemption.Retreating, preemption);
                held++;
            }

            _output.WriteLine($"held={held} ceiling={LifeSafetySupervisor.MaxRetreatTicks}");
            Assert.Equal(LifeSafetySupervisor.MaxRetreatTicks, held);
        }
    }

    /// <summary>The first cell of the bore's floor the live row swaps to magma.</summary>
    private const int HazardFromX = 12;

    /// <summary>The last one: twenty cells, which is what the live row needs to force the crossing.</summary>
    private const int HazardToX = 31;

    /// <summary>Ticks of resting on the hot floor that cost one heart: four.</summary>
    /// <remarks>Sized from the harness's live measurement rather than picked. Live, a 3-cell magma swap costs a real 4 HP, which is 1.33 hearts a CELL; this fixture's executor crosses the bore at 5.1 ticks a cell (measured in the trace this row prints), so one heart every four ticks is 1.28 hearts a cell. The per-cell cost is what the row is built around, because the twenty-cell strip the live course builds has to be able to force the fourteen-point drop <see cref="LifeSafetySupervisor.HealthFloor"/> needs, and it is per-cell arithmetic that says whether it can.</remarks>
    private const int HotFloorPeriodTicks = 4;

    /// <summary>Whether the body's feet are resting on a hot floor, which is when the server applies contact damage.</summary>
    /// <remarks>
    /// <para>Read as GEOMETRY - the feet flush on the top face of the block below - and deliberately not off <see cref="PhysicsState.OnGround"/>, which is false for every tick of this crossing even though the body sits at exactly <c>y=70.0000</c> the whole way. That is UMPK's known <c>OnGround</c> latch under water, not a statement about the world, and the server has no such problem: the harness measured a real 4 HP off a 3-cell swap with the live bot resting at <c>y=100.0000</c> in exactly this pose. Modelling the damage off the latched flag would build the bug into the fixture and quietly assert that magma is harmless.</para>
    /// <para>It is also what makes a LIFT-OFF observable, which is the half of the escape that matters: It fires <c>stepOn</c> only against the block a body is resting on, so a body that leaves the floor stops being hurt by it on that tick.</para>
    /// </remarks>
    private static bool RestsOnHotFloor(ApplierHarness harness, in PhysicsState state, int magmaState)
    {
        Vec3d position = state.Position;
        if (Math.Abs(position.Y - Math.Round(position.Y)) > 1.0E-3)
            return false;

        var support = new BlockPos(
            (int)Math.Floor(position.X), (int)Math.Round(position.Y) - 1, (int)Math.Floor(position.Z));
        return harness.State.World.GetBlockStateId(support) == magmaState;
    }

    /// <summary>The block a resting body's <c>stepOn</c> fires against: the one under its feet.</summary>
    private static BlockPos SupportUnder(in Vec3d position)
        => BlockPos.Containing(new Vec3d(position.X, position.Y - 0.0625, position.Z));

    private static IGoal Goal => new GoalNear(BoreLength + 2, FloorY, 0, 0);

    private static PlannedRoute Route(PhysicsEngineHolder holder)
    {
        PlanCapture? capture = holder.CapturePlan(Goal);
        Assert.NotNull(capture);
        PlannedRoute? route = holder.BuildExecutor(capture.Value, PathfinderOptions.Default, CancellationToken.None);
        Assert.NotNull(route);
        return route.Value;
    }

    /// <summary>Ten minutes of water breathing on the session's own effect table, which is what <c>effect give PathBot minecraft:water_breathing 600 0 true</c> lands as.</summary>
    private static void GrantWaterBreathing(ApplierHarness harness)
    {
        Assert.True(
            JavaGameData.Registries(Protocol).MobEffects.TryGetNetworkId(WaterBreathing, out int networkId),
            "the 772 registry must be able to name water breathing");
        harness.State.Self.ApplyEffect(
            new ActiveEffect(networkId, Amplifier: 0, Duration: 12000, Flags: 0)
            {
                AppliedAtTick = harness.State.SessionTick,
            });
    }

    /// <summary>M5's bore reduced to its two load-bearing features: ninety cells of 1x2 flooded lane in a stone casing with no air inside it, and a dry mouth at each end so the route starts and ends breathing.</summary>
    /// <param name="collarToX">How far along the lane to punch a one-cell collar in the lid, with stone above it, starting at x=0. A body that rises into it breathes and a surfacing can reach it, but a two-cell-tall body can never stand in one cell, so the route is unchanged - the collar is what makes a life-safety trip LEGAL without changing what the planner does. It spans the first few cells rather than one, because a row that ticks the crossing before it measures anything has moved the body off the start column by the time it looks. Negative leaves the lid solid.</param>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)>
        StartBoreAsync(int collarToX = -1)
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Pathfinding = true, Terrain = true, Entities = true });
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
        int waterState = water.MinStateId;

        // The casing, tall enough that an escape scan up a lidded column meets stone rather than the top of the fixture.
        for (int x = -4; x <= BoreLength + 8; x++)
            for (int y = FloorY - 2; y <= FloorY + 8; y++)
                for (int z = -2; z <= 2; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        for (int x = 0; x <= BoreLength; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), waterState);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), waterState);
        }

        // The dry mouths at both ends, standing on the lane bed.
        for (int x = BoreLength + 1; x <= BoreLength + 3; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), 0);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), 0);
        }

        for (int x = 0; x <= collarToX; x++)
            world.SetBlockStateId(new BlockPos(x, FloorY + 2, 0), 0);

        harness.State.Registries = JavaGameData.Registries(Protocol);
        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        holder.TickIdle();
        Assert.True(holder.EngineState!.Value.IsUnderWater, "the fixture must start the player submerged");
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }
}
