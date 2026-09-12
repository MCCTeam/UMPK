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

/// <summary>
/// The life-safety supervisor checks before every executor tick and takes control when the player is about to drown or die.
/// <para>Every row here runs in the SAME lidded, flooded trench, because the point of the design is that the terrain does not decide: the PLANNED ROUTE does. A bare "is there air straight up" test answers "no" everywhere in this fixture and would either fire everywhere or nowhere.</para>
/// </summary>
public sealed class LifeSafetySupervisorTests
{
    private const int Protocol = 772;
    private const int FloorY = 70;
    private const int TrenchLength = 20;

    /// <summary>The flooded lane of the E19 replica: x=0..12, with the shaft at x=13.</summary>
    private const int LaneLength = 13;

    /// <summary>The lane length the route-sized-target row runs on: longer than the shared <see cref="TrenchLength"/>, so the continuation's peak deficit sizes a hold that a partial refill cannot satisfy.</summary>
    /// <remarks>
    /// <c>BreathValidator.RealTicks</c> uses <c>Math.Max(charged, blocks * walkTicksPerBlock)</c> for submerged travel without charging a dry station hold as lung consumption. The lane is long enough that the continuation target remains beyond a partial refill and the required hold exceeds the 80-tick stall window.
    /// <para>Twenty-eight, not more: at thirty the lane is longer than one lung buys with the collar only at the start, and the route is refused outright. Measured here it holds for 85 ticks against an 80-tick stall window and a 150-tick ceiling, so both bounds still bite.</para>
    /// </remarks>
    private const int RouteSizedTargetTrenchLength = 28;

    /// <summary>The E19 replica's top water cell, and the level the corridor floor stands on.</summary>
    private const int ShaftTopY = FloorY + 10;

    private readonly ITestOutputHelper _output;

    public LifeSafetySupervisorTests(ITestOutputHelper output) => _output = output;

    /// <summary>Two blocks from the mouth, with a hundred ticks of air. The vertical column is sealed, so a ceiling-based trip would have nothing to say; the route says air is 20 real ticks away, whose threshold is 31, and 100 is comfortably above it.</summary>
    [Fact]
    public async Task Supervisor_DoesNotFireUnderALidWithAPlannedAirHole()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(startX: 18);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, out PlanCapture capture);

            // The lid is real: straight up from here there is no air at all, and the model says so WITHOUT that becoming a panic.
            BreathEscape vertical = BreathModel.EscapeTicks(
                capture.Planning, 18, FloorY, 0, PhysicsProfile.ForProtocol(Protocol));
            Assert.False(vertical.IsKnown);

            harness.State.Self.AirSupply = 100;
            holder.BeginNavigation();

            for (int tick = 0; tick < 20; tick++)
            {
                NavigationTickOutcome outcome = holder.TickNavigation(executor);
                AssertNotPreEmpted(outcome);
                if (outcome.State != PathExecutorState.InProgress)
                    break;

            }
        }
    }

    /// <summary>
    /// The same trench, the same lid, the same hundred ticks of air, at the other end of it. Air is now 204 real ticks away along the route, whose threshold is 215, and the supervisor pre-empts on the first tick rather than letting the executor walk the player into the twenty blocks it cannot afford.
    /// <para>The lid carries a breathable collar over the start column, because a trip is now allowed only where a surfacing can reach air. The collar is what the hold climbs to. It is one air cell with stone above it, so it is breathable and not an exit, and the route is unchanged - a two-cell-tall body can never stand in it.</para>
    /// </summary>
    [Fact]
    public async Task Supervisor_FiresBeforeTheAirBudgetIsSpent()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(startX: 0, breatheHoleAtX: 0);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, out _);
            harness.State.Self.AirSupply = 100;
            holder.BeginNavigation();

            NavigationTickOutcome outcome = holder.TickNavigation(executor);

            Assert.Equal(NavigationPreemption.Surfacing, outcome.Preemption);
        }
    }

    /// <summary>The release is computed from the REMAINING ROUTE at the moment of the trip and frozen, because once the supervisor has stepped the engine the executor is dead and cannot be asked anything. Banking the air releases the hold and asks the navigator for a replan.</summary>
    [Fact]
    public async Task Surfacing_ReleasesOnBankedAirAndAsksForAReplan()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(startX: 0, breatheHoleAtX: 0);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, out _);
            harness.State.Self.AirSupply = 100;
            holder.BeginNavigation();

            Assert.Equal(NavigationPreemption.Surfacing, holder.TickNavigation(executor).Preemption);

            // Still short of the release band: the trip fired at a threshold of 215, and the band is sixty ticks of refill above it, clamped to a full lung.
            harness.State.Self.AirSupply = 200;
            Assert.Equal(NavigationPreemption.Surfacing, holder.TickNavigation(executor).Preemption);

            harness.State.Self.AirSupply = 300;
            Assert.Equal(NavigationPreemption.Surfaced, holder.TickNavigation(executor).Preemption);

            // And the hold is gone: the next tick belongs to the executor again.
            Assert.Equal(NavigationPreemption.None, holder.TickNavigation(executor).Preemption);
        }
    }

    /// <summary>A surfacing must restore enough air for the rest of the route, not merely enough to clear the bar that fired it. This is course row E5 surfaceholes.</summary>
    /// <remarks>
    /// <para>The hold must not release on 99 ticks of air when the continuation needs 201.</para>
    /// <para>99 is <c>Math.Max(tripThreshold, Threshold(deepest)) + ResumeRefillBonusTicks</c> with the bracket at 39, and both of its terms are LOCAL. The trip threshold is the cost of reaching the NEXT breathing node, which near a lid hole is a handful of ticks; the escape threshold is the vertical climb out of the deepest cell on the route, which in a trench three blocks deep is 8.62. Neither says anything about the eighteen-block submerged leg the route takes AFTER that breath, and that leg is what the validator refuses on. A refill sized by the bar that fired is guaranteed to release straight back into a route the player cannot afford.</para>
    /// <para>The fixture is E5's shape reduced to its two features: a breathing node early (the step at x=2, an air bell on the lane) so the trip threshold is small, and a long submerged leg after it so the route's peak deficit is not.</para>
    /// </remarks>
    [Fact]
    public async Task Surfacing_ReleasesWithEnoughAirForTheWholeRemainingRoute()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartCollaredTrenchAsync();
        await using (loop)
        {
            PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);

            // Plan on a full lung: the route is affordable, which is what makes the refusal after the surfacing a refill problem rather than a routing problem.
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            PlanCapture? planned = holder.CapturePlan(new GoalNear(TrenchLength + 2, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            var supervisor = new LifeSafetySupervisor(profile);
            supervisor.Reset(harness.State.Self.Health);

            // Drawn down to where the trip really fires: the next breathing node is the bell at x=2.
            harness.State.Self.AirSupply = 20;
            LifeSafetyAction action = supervisor.Evaluate(
                holder.EngineState!.Value, harness.State.Self, planned.Value.Planning, executor);
            Assert.Equal(LifeSafetyAction.Surfacing, action);

            double release = supervisor.ResumeAir;
            BreathValidation continuation = BreathValidator.Validate(
                executor.Segments, planned.Value.Planning, profile, executor.AllowSprint, (int)Math.Floor(release));

            _output.WriteLine(
                $"release={release:F1} peak={continuation.PeakDeficitTicks:F2} budget={continuation.BudgetTicks:F0}");
            Assert.True(
                continuation.IsSurvivable,
                $"the hold released on {release:F1} ticks of air, and the remaining {executor.TotalSegments} "
                + $"segments need a peak of {continuation.PeakDeficitTicks:F2} against the "
                + $"{continuation.BudgetTicks:F0}-tick budget that air buys");
        }
    }

    /// <summary>The hold must reach its release target. This runs against the real engine at a real lid hole and applies the protocol air arithmetic on every tick.</summary>
    /// <remarks>
    /// <para>The fixture records both the release target and the air at release. This distinguishes a target that is too low from a hold that fails to reach a correct target.</para>
    /// <para>Measured here, at a two-cell flooded lane with one collared hole in its lid: a hold that starts at <c>air 20</c> spends seven ticks climbing, bobs through the surface for about forty more while it settles, and gains 3.11 ticks of air a tick against the clean four. <see cref="LifeSafetySupervisor.MaxSurfacingTicks"/> was 80 and was the ONLY bound, so it fired on tick 80 with the lung at 264 and still rising, ten ticks short of the 274 it had frozen. The failure was verbatim <c>the hold aimed at 274,0 ticks of air and handed back 264 after 80 ticks, with the lung still rising</c>.</para>
    /// <para>The route is deliberately one unbroken submerged run with no air bell on it, so the target the hold freezes is the whole of it and a partial refill will not do.</para>
    /// <para>A clean source refills four air units per tick. Since the release target is clamped to <see cref="BreathModel.FullLungTicks"/>, a clean hold needs at most 75 ticks. The 150-tick ceiling still covers lossy sources in <see cref="Surfacing_IsStillBoundedWhileTheAirIsRisingTooSlowlyToArrive"/>. This row verifies that the target covers the whole remaining route and that the hold stops on that target.</para>
    /// <para>Which of the three bars wins here is not the point and is not asserted: the route carries no bell, so the trip threshold and the continuation plus its reserve (221.68) are the same number to within a third of a tick, by construction. The row asserts that the target covers the continuation, which is what makes the closing <c>IsSurvivable</c> non-accidental.</para>
    /// </remarks>
    [Fact]
    public async Task Surfacing_KeepsFillingAtALidHoleUntilTheRouteSizedTargetIsMet()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartLidHoleTrenchAsync(RouteSizedTargetTrenchLength);
        await using (loop)
        {
            PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);

            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            PlanCapture? planned = holder.CapturePlan(
                new GoalNear(RouteSizedTargetTrenchLength + 2, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            // The holder's own navigation tick, reproduced so the test can read the hold's frozen target: air first (vanilla's baseTick order), then the supervisor, then the surfacing controller driving the engine. This is PhysicsEngineHolder.TickNavigation's Surfacing arm exactly.
            var engine = new PlayerPhysics(planned.Value.Planning, profile);
            engine.SetConditions(PhysicsConditions.Default);
            engine.Reset(new Vec3d(0.5, FloorY, 0.5), 0f, 0f);
            engine.Step(MovementInput.None);

            SelfState self = harness.State.Self;
            self.AirSupply = 20;
            var supervisor = new LifeSafetySupervisor(profile);
            supervisor.Reset(self.Health);

            int held = 0;
            double target = -1;
            for (int tick = 0; tick < 600; tick++)
            {
                self.AirSupply = AirSupplyRule.Next(self.AirSupply, engine.State.IsUnderWater, false);
                LifeSafetyAction action = supervisor.Evaluate(
                    engine.State, self, planned.Value.Planning, executor);
                if (action == LifeSafetyAction.Surfaced)
                    break;

                Assert.True(
                    action is LifeSafetyAction.Surfacing or LifeSafetyAction.SurfaceBreathing,
                    $"the supervisor let go of the engine on tick {tick}: {action}");
                if (held == 0)
                    target = supervisor.ResumeAir;

                held++;
                if (action == LifeSafetyAction.Surfacing)
                {
                    (MovementInput input, float yaw, float pitch) = SurfacingController.Next(engine.State);
                    engine.SetRotation(yaw, pitch);
                    engine.Step(input);
                }
                else
                {
                    // The climb has arrived and the hold has taken over. These are the same two lines the driver's SurfaceBreathing arm runs: no rotation change, and the driver's own breath-hold input at the anchor the supervisor froze.
                    Assert.NotNull(supervisor.SurfacingAnchor);
                    engine.Step(
                        PhysicsEngineHolder.BreathHoldInput(engine.State, supervisor.SurfacingAnchor!.Value));
                }
            }

            BreathValidation continuation = BreathValidator.Validate(
                executor.Segments, planned.Value.Planning, profile, executor.AllowSprint, self.AirSupply);

            _output.WriteLine(
                $"held={held} target={target:F1} release={self.AirSupply} "
                + $"peak={continuation.PeakDeficitTicks:F2} budget={continuation.BudgetTicks:F0}");

            Assert.True(
                self.AirSupply >= target,
                $"the hold aimed at {target:F1} ticks of air and handed back {self.AirSupply} after {held} "
                + "ticks, with the lung still rising");

            // The row only measures anything if the target is the CONTINUATION's and not one of the two local bars, so that is asserted rather than assumed: the trip threshold at a hole is a handful of ticks and the vertical escape out of a two-cell lane is under ten, and a hold sized by either would release into a route it cannot afford. See the remarks for why this replaced a bound on `held`.
            double continuationPeak = BreathValidator.PeakDeficit(
                executor.Segments, 0, planned.Value.Planning, profile, executor.AllowSprint);
            BreathEscape local = BreathModel.EscapeTicks(planned.Value.Planning, 0, FloorY, 0, profile);
            double locallySized = BreathModel.Threshold(local.Ticks) + LifeSafetySupervisor.ResumeRefillBonusTicks;
            _output.WriteLine(
                $"continuationPeak={continuationPeak:F2} target={target:F1} locallySized={locallySized:F1}");
            Assert.True(
                target >= continuationPeak + BreathModel.ReactionTicks,
                $"the hold froze {target:F1}, under the {continuationPeak:F2}-tick continuation plus the "
                + $"{BreathModel.ReactionTicks}-tick reserve the validator judges it at");
            Assert.True(
                target > locallySized * 3,
                $"the target {target:F1} is not distinguishable from a hold sized by the climb out of "
                + $"this hole alone, which is {locallySized:F1}");

            // And it stopped when it had what it came for, rather than running out the new ceiling.
            Assert.True(
                held < LifeSafetySupervisor.MaxSurfacingTicksTotal,
                $"the hold ran {held} ticks, which is the ceiling rather than the target");

            // The end-to-end effect: the continuation the navigator replans is one the player can afford.
            Assert.True(
                continuation.IsSurvivable,
                $"the hold released on {self.AirSupply} and the remaining {executor.TotalSegments} segments "
                + $"need a peak of {continuation.PeakDeficitTicks:F2} against the "
                + $"{continuation.BudgetTicks:F0}-tick budget that air buys");
        }
    }

    /// <summary>The other half of the same change: a hold that buys NO air still gives up on <see cref="LifeSafetySupervisor.MaxSurfacingTicks"/>, which is what that constant was always for.</summary>
    /// <remarks>A climb into a motion-blocking lid is the case: the eye never leaves the water, so the lung only falls. The air series is fed by hand rather than by the engine because the point is the SUPERVISOR's arithmetic - a stalled hold must not inherit the ceiling a filling one gets.</remarks>
    [Fact]
    public async Task Surfacing_GivesUpOnTheStallWindowWhenTheHoldBuysNoAir()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartLidHoleTrenchAsync();
        await using (loop)
        {
            PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            PlanCapture? planned = holder.CapturePlan(new GoalNear(TrenchLength + 2, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            var supervisor = new LifeSafetySupervisor(profile);
            supervisor.Reset(harness.State.Self.Health);
            harness.State.Self.AirSupply = 100;

            int held = CountHoldTicks(supervisor, holder, harness, planned.Value, executor, airStep: -1, limit: 400);

            Assert.Equal(LifeSafetySupervisor.MaxSurfacingTicks, held);
        }
    }

    /// <summary>A surfacing that has climbed to air must refill the lung before it hands the navigator back, or the replan the navigator makes next is priced against the empty lung the climb finished on and is refused on the spot.</summary>
    /// <remarks>
    /// <para>A body can reach the air cell while its 0.6-wide box still overlaps a neighboring lid cell. In that position, the lid prevents the eye from leaving the water. <see cref="SurfacingController"/> is a vertical CLIMB and deliberately presses nothing lateral ("in a shaft lateral drift is a wall"), so it cannot centre a body on a one-cell hole, and its latched <c>Sprint</c> holds the swimming pose whose eye sits at <c>+0.4</c> instead of <c>+1.62</c>. The scheduled pause at the previous bell, on the same geometry, filled the lung completely - <c>air 162 -&gt; 300</c> - because <c>PhysicsEngineHolder.BreathHoldInput</c> steers to the cell centre and never presses <c>Sprint</c>.</para>
    /// <para>The fixture sweeps offsets across the bell column. The box clears the neighboring lid cell only between 0.3 and 0.7:</para>
    /// <code>
    /// offset 0.20  y=71.400 pose=Swimming   air 20 -&gt; 0     80 ticks, bought nothing offset 0.25  y=71.400 pose=Swimming   air 20 -&gt; 0     80 ticks, bought nothing offset 0.30  y=71.500 pose=Crouching  air 20 -&gt; 265   79 ticks offset 0.50  y=71.500 pose=Crouching  air 20 -&gt; 265   79 ticks offset 0.70  y=71.500 pose=Crouching  air 20 -&gt; 265   79 ticks offset 0.75  y=71.400 pose=Swimming   air 20 -&gt; 0     80 ticks, bought nothing offset 0.80  y=71.400 pose=Swimming   air 20 -&gt; 0     80 ticks, bought nothing
    /// </code>
    /// <para><c>y=71.400</c> is <c>SurfacingDrainTests.ASealedLid_DrownsWhicheverWaySprintIsSet</c>'s measured result. A body one tenth of a block off center remains under the lid even with air over its head. The <c>0.50</c> case is the centered control.</para>
    /// <para>The assertion is the one the navigator actually depends on: after the release, the plan the navigator would make on its very next line must not be refused on breath.</para>
    /// </remarks>
    [Theory]
    [InlineData(0.20)]
    [InlineData(0.25)]
    [InlineData(0.50)]
    [InlineData(0.75)]
    [InlineData(0.80)]
    public async Task Surfacing_RefillsTheLungBeforeItHandsBackAReplan(double offset)
    {
        const int Bell = 20;
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartLidHoleTrenchAsync(length: 44, bellAtX: Bell);
        await using (loop)
        {
            var goal = new GoalNear(45, FloorY, 0, 0);
            PlanCapture? planned = holder.CapturePlan(goal);
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            // Under the bell, 0.25 off its centre, which is where a body that walked the lane arrives: the 0.6-wide box then overlaps the lid cell next door and cannot rise past it.
            harness.State.Self.Position = new Vec3d(Bell + offset, FloorY, 0.5);
            harness.State.Self.Velocity = Vec3d.Zero;
            harness.State.Self.AirSupply = 20;
            holder.BeginNavigation();
            holder.ResyncPosition();
            holder.TickIdle();
            Assert.True(holder.EngineState!.Value.IsUnderWater, "the body must start submerged");

            int surfacingTicks = 0;
            int lowest = harness.State.Self.AirSupply;
            bool released = false;
            for (int tick = 0; tick < 400 && !released; tick++)
            {
                NavigationTickOutcome outcome = holder.TickNavigation(executor);
                switch (outcome.Preemption)
                {
                    case NavigationPreemption.Surfaced:
                        released = true;
                        break;
                    case NavigationPreemption.Surfacing:
                        surfacingTicks++;
                        break;
                    default:
                        Assert.True(
                            surfacingTicks == 0,
                            $"the supervisor let go mid-hold: {outcome.Preemption} on tick {tick}");
                        break;
                }

                harness.State.Self.AirSupply = holder.EngineState!.Value.IsUnderWater
                    ? Math.Max(0, harness.State.Self.AirSupply - 1)
                    : Math.Min(harness.State.Self.MaxAirSupply, harness.State.Self.AirSupply + 4);
                lowest = Math.Min(lowest, harness.State.Self.AirSupply);
            }

            Assert.True(released, "the surfacing never released inside the window");

            int air = harness.State.Self.AirSupply;
            _output.WriteLine(
                $"surfaced after {surfacingTicks} ticks at {holder.EngineState!.Value.Position} "
                + $"pose={holder.EngineState!.Value.Pose} air {20} -> {air} (lowest {lowest})");

            Assert.True(surfacingTicks > 0, "the supervisor must actually have taken the engine");

            // The navigator's immediate replan must not be refused on breath.
            PlanCapture? after = holder.CapturePlan(goal);
            Assert.NotNull(after);
            Assert.NotNull(
                holder.BuildExecutor(after.Value, PathfinderOptions.Default, CancellationToken.None));

            // And the reason it is not refused, so a pass cannot come from the route having got shorter.
            Assert.True(air > 20, $"the hold bought no air at a real air source: air 20 -> {air}");
        }
    }

    /// <summary>Every surfacing reports itself, once at the trip and once at the release, and the release says which of the three exits ended it.</summary>
    /// <remarks>The reports are consumed when read, so one event yields one line even if the driver polls again.</remarks>
    [Fact]
    public async Task EverySurfacing_ReportsItsTripAndItsRelease()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartLidHoleTrenchAsync();
        await using (loop)
        {
            PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            PlanCapture? planned = holder.CapturePlan(new GoalNear(TrenchLength + 2, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            var supervisor = new LifeSafetySupervisor(profile);
            supervisor.Reset(harness.State.Self.Health);
            Assert.False(supervisor.TryTakeSurfacingTrip(out _));
            Assert.False(supervisor.TryTakeSurfacingRelease(out _));

            harness.State.Self.AirSupply = 100;
            Assert.Equal(
                LifeSafetyAction.Surfacing,
                supervisor.Evaluate(holder.EngineState!.Value, harness.State.Self, planned.Value.Planning, executor));

            Assert.True(supervisor.TryTakeSurfacingTrip(out SurfacingTrip trip));
            Assert.False(supervisor.TryTakeSurfacingTrip(out _));
            Assert.Equal(100, trip.Air);
            Assert.True(trip.Air < trip.Threshold, $"the trip must be under its own bar: {trip}");
            Assert.True(trip.ResumeAir > trip.Air, $"a trip must aim above the lung it fired on: {trip}");
            Assert.False(string.IsNullOrWhiteSpace(trip.Why));

            // A lung that only falls: the stall window is what ends this one, and it must say so.
            int held = 1;
            while (supervisor.Evaluate(
                       holder.EngineState!.Value, harness.State.Self, planned.Value.Planning, executor)
                   == LifeSafetyAction.Surfacing)
            {
                held++;
                harness.State.Self.AirSupply = Math.Max(0, harness.State.Self.AirSupply - 1);
                Assert.False(supervisor.TryTakeSurfacingRelease(out _));
            }

            Assert.True(supervisor.TryTakeSurfacingRelease(out SurfacingRelease release));
            Assert.False(supervisor.TryTakeSurfacingRelease(out _));
            _output.WriteLine($"{release}");
            Assert.Equal(100, release.EntryAir);
            Assert.Equal(held, release.Ticks);
            Assert.Equal(LifeSafetySupervisor.MaxSurfacingTicks, release.Ticks);
            Assert.True(release.ExitAir < release.EntryAir, $"this hold bought nothing: {release}");
            Assert.Contains("stopped buying air", release.Why, StringComparison.Ordinal);
        }
    }

    /// <summary>And the ceiling is real: a hold whose lung rises too slowly ever to arrive is still bounded, so "still rising" cannot be a licence to hold the engine forever.</summary>
    /// <remarks>One tick of air every twenty ticks resets the stall window on schedule and never approaches the target, which is the adversarial shape the stall window alone cannot bound.</remarks>
    [Fact]
    public async Task Surfacing_IsStillBoundedWhileTheAirIsRisingTooSlowlyToArrive()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartLidHoleTrenchAsync();
        await using (loop)
        {
            PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            PlanCapture? planned = holder.CapturePlan(new GoalNear(TrenchLength + 2, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            var supervisor = new LifeSafetySupervisor(profile);
            supervisor.Reset(harness.State.Self.Health);
            harness.State.Self.AirSupply = 20;

            int held = CountHoldTicks(
                supervisor, holder, harness, planned.Value, executor, airStep: 0, limit: 600, risePeriod: 20);

            Assert.Equal(LifeSafetySupervisor.MaxSurfacingTicksTotal, held);
        }
    }

    /// <summary>Runs a hold against a hand-fed air series and returns how many ticks the supervisor kept the engine. The physics state never moves, so the only thing under test is the release arithmetic.</summary>
    private static int CountHoldTicks(
        LifeSafetySupervisor supervisor,
        PhysicsEngineHolder holder,
        ApplierHarness harness,
        PlanCapture planned,
        PathExecutor executor,
        int airStep,
        int limit,
        int risePeriod = 0)
    {
        int held = 0;
        for (int tick = 0; tick < limit; tick++)
        {
            LifeSafetyAction action = supervisor.Evaluate(
                holder.EngineState!.Value, harness.State.Self, planned.Planning, executor);
            if (action == LifeSafetyAction.Surfaced)
                return held;

            Assert.Equal(LifeSafetyAction.Surfacing, action);
            held++;
            int delta = airStep;
            if (risePeriod > 0 && held % risePeriod == 0)
                delta = 1;

            harness.State.Self.AirSupply = Math.Max(0, harness.State.Self.AirSupply + delta);
        }

        return held;
    }

    /// <summary>
    /// The one-model-two-consumers rule, stated directly. The planner's breath validation approved this route on a 204-tick submerged run, so a player that starts it with a full lung must be able to execute the whole thing without the supervisor ever taking it away. A supervisor that re-applied the escape safety factor to a number the validator had already accepted would pre-empt every route the planner approved, on its own first tick.
    /// <para>The breathable collar is there so this row measures the ARITHMETIC and not the "a hold that can buy no air never starts" gate: with a surfacing available, the only reason the supervisor stays quiet is that the numbers say so. See <c>Supervisor_NeverPreEmptsAnApprovedRoute_OnAnyStartingLung</c> for the same invariant read at lungs that are not full, which is the shape this row could not see.</para>
    /// </summary>
    [Fact]
    public async Task Supervisor_DoesNotPreEmptARouteTheValidatorApproved()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(startX: 0, breatheHoleAtX: 0);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, out _);
            harness.State.Self.AirSupply = harness.State.Self.MaxAirSupply;
            holder.BeginNavigation();

            for (int tick = 0; tick < 60; tick++)
            {
                NavigationTickOutcome outcome = holder.TickNavigation(executor);
                AssertNotPreEmpted(outcome);
                if (outcome.State != PathExecutorState.InProgress)
                    break;

            }
        }
    }

    /// <summary>Health is the other life-safety trip and pre-empts identically. It is edge-triggered, so a navigation begun by an already-hurt player is not an event.</summary>
    [Fact]
    public async Task Supervisor_FiresOnAHealthCrossing()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(startX: 18, breatheHoleAtX: 18);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, out _);
            harness.State.Self.AirSupply = 300;
            harness.State.Self.Health = 20f;
            holder.BeginNavigation();

            Assert.Equal(NavigationPreemption.None, holder.TickNavigation(executor).Preemption);

            harness.State.Self.Health = 5f;

            Assert.Equal(NavigationPreemption.Surfacing, holder.TickNavigation(executor).Preemption);
        }
    }

    [Fact]
    public async Task Supervisor_DoesNotFireForAPlayerThatWasAlreadyHurt()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartAsync(startX: 18, breatheHoleAtX: 18);
        await using (loop)
        {
            PathExecutor executor = Navigate(holder, out _);
            harness.State.Self.AirSupply = 300;
            harness.State.Self.Health = 4f;
            holder.BeginNavigation();

            Assert.Equal(NavigationPreemption.None, holder.TickNavigation(executor).Preemption);
            Assert.Equal(NavigationPreemption.None, holder.TickNavigation(executor).Preemption);
        }
    }

    /// <summary>A surfacing is not a failure, so it is paid for out of its own budget. A route with three legitimate air stops must still arrive at its first real deviation with every replan intact.</summary>
    [Fact]
    public void SurfacingRecovery_DoesNotConsumeTheReplanBudget()
    {
        var budget = new NavigationBudget(PathfinderOptions.Default, breathingStops: 0);
        int replansAtStart = budget.ReplansLeft;

        Assert.True(budget.TrySurface());
        Assert.True(budget.TrySurface());
        Assert.True(budget.TrySurface());

        Assert.Equal(0, budget.SurfacingsLeft);
        Assert.False(budget.TrySurface());
        Assert.Equal(replansAtStart, budget.ReplansLeft);

        // And the replans really are still spendable, not merely still counted.
        for (int i = 0; i < replansAtStart; i++)
            Assert.True(budget.TryReplan());

        Assert.False(budget.TryReplan());
    }

    /// <summary>The surfacing count comes from the ROUTE: one recovery per air stop it plans to make, plus one surprise, never below the old flat floor.</summary>
    /// <remarks>
    /// <para>The flat three was the wrong shape rather than the wrong number. The supervisor spends one recovery per surfacing, so a route that legitimately breathes five times reached its fifth honest breath with nothing left and the navigation threw - the same ending E19 got for a route that breathed nothing at all. A counter that only counts cannot tell those apart; a counter sized by the route can.</para>
    /// <para>The expectations are a literal table, not the formula re-evaluated: 0, 1 and 2 stops all sit under the floor and must not lower it, and 3 upward must raise it by exactly one over the route's own count.</para>
    /// </remarks>
    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(5, 6)]
    [InlineData(11, 12)]
    public void SurfacingBudget_IsSizedByTheRoutesOwnAirStops(int breathingStops, int expectedSurfacings)
    {
        var budget = new NavigationBudget(PathfinderOptions.Default, breathingStops);

        Assert.Equal(expectedSurfacings, budget.SurfacingsLeft);
        for (int i = 0; i < expectedSurfacings; i++)
            Assert.True(budget.TrySurface(), $"surfacing {i + 1} of {expectedSurfacings} was refused");

        Assert.False(budget.TrySurface());
        Assert.Equal(0, budget.SurfacingsLeft);
    }

    /// <summary>The replan count must come from <see cref="PathfinderOptions.MaxReplans"/>.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void ReplanBudget_ComesFromTheOptions(int maxReplans)
    {
        var budget = new NavigationBudget(PathfinderOptions.Default with { MaxReplans = maxReplans }, breathingStops: 0);

        Assert.Equal(maxReplans, budget.ReplansLeft);
        for (int i = 0; i < maxReplans; i++)
            Assert.True(budget.TryReplan());

        Assert.False(budget.TryReplan());
        Assert.Equal(NavigationBudget.MaxSurfacingRecoveries, budget.SurfacingsLeft);
    }

    /// <summary>A navigation that ends while standing in a current may leave a station anchor for idle steering. Beginning the next navigation must clear that prior anchor before the lease is released.</summary>
    [Fact]
    public async Task BeginNavigation_ClearsTheStationHoldTheLastNavigationLeft()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync(startX: 0, lid: false);
        await using (loop)
        {
            _ = harness;
            PlanCapture? planned = holder.CapturePlan(new GoalNear(2, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            holder.BeginNavigation();
            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 400 && state == PathExecutorState.InProgress; tick++)
                state = holder.TickNavigation(executor).State;

            Assert.Equal(PathExecutorState.Complete, state);
            Assert.True(holder.HasStationHold, "an arrival in water arms the station hold");

            holder.BeginNavigation();

            Assert.False(holder.HasStationHold);
        }
    }

    /// <summary>The same route is judged against the player's current air rather than a full lung.</summary>
    /// <remarks>
    /// <para>The fixture is a lidded flooded lane whose only air is at the head of a shaft thirteen blocks away. It starts with a partially depleted lung.</para>
    /// <para>The two sides read different air by construction. <see cref="BreathValidator.Validate"/> starts its deficit at zero and caps it at <see cref="BreathModel.FullLungTicks"/>, so it approves any route under 300 ticks NO MATTER WHAT THE PLAYER HAS LEFT; <see cref="LifeSafetySupervisor"/> fires when the live <c>AirSupply</c> is below <c>RouteThreshold</c> of the same route. So a route of D ticks is approved at <c>D &lt;= 300</c> and pre-empted at <c>air &lt; D + 10</c>, and every lung between those two numbers is a guaranteed pre-emption of an approved route. The existing invariant test sits at a full lung on a 204-tick route, 86 ticks clear of the bar, so it cannot see this.</para>
    /// <para>The assertion is the invariant itself and not a verdict on any one lung: a refusal is a legitimate answer to a route the player cannot afford. What is never legitimate is approving the route and then pre-empting it.</para>
    /// </remarks>
    [Theory]
    [InlineData(300)]
    [InlineData(260)]
    [InlineData(220)]
    [InlineData(200)]
    [InlineData(180)]
    [InlineData(140)]
    public async Task Supervisor_NeverPreEmptsAnApprovedRoute_OnAnyStartingLung(int startingAir)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartFloodedShaftAsync();
        await using (loop)
        {
            harness.State.Self.AirSupply = startingAir;

            PlanCapture? planned = holder.CapturePlan(new GoalNear(LaneLength + 3, ShaftTopY + 1, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;

            if (executor is null)
            {
                _output.WriteLine($"air={startingAir}: route REFUSED by the validator (a legitimate answer)");
                return;
            }

            double ticks = RouteTicksToAir(executor, planned.Value.Planning);
            _output.WriteLine(
                $"air={startingAir}: APPROVED, {executor.TotalSegments} segments, "
                + $"ticksToAir={ticks:F1}, routeThreshold={BreathModel.RouteThreshold(ticks)}");

            holder.BeginNavigation();
            for (int tick = 0; tick < 1500; tick++)
            {
                NavigationTickOutcome outcome = holder.TickNavigation(executor);
                AssertNotPreEmpted(outcome);
                if (outcome.State != PathExecutorState.InProgress)
                    break;

                // The air-supply rule the session loop would be running: one tick a tick while the eyes are under, four back a tick once they are out.
                harness.State.Self.AirSupply = holder.EngineState!.Value.IsUnderWater
                    ? Math.Max(0, harness.State.Self.AirSupply - 1)
                    : Math.Min(harness.State.Self.MaxAirSupply, harness.State.Self.AirSupply + 4);
            }
        }
    }

    /// <summary>The supervisor must stand its breath arm down while the executor is spending a pause the PLAN scheduled, instead of surfacing the body off the air it is standing in.</summary>
    /// <remarks>
    /// <para><b>Measured live on course rows E31 and E32, first run.</b> Both arrived at their bell and were taken away from the schedule on the spot:</para>
    /// <code>
    /// Segment 24/91 completed (Swim) in 5 ticks at (790.6466, 100.6185, 840.4888). Navigation resumed after surfacing; 4 surfacing recoveries and 5 replans left. Plan refused on breath: peakDeficit=157.97 ticks against a 70-tick budget (air 80), ...
    /// </code>
    /// <para><b>The mechanism.</b> <c>PhysicsEngineHolder</c> evaluates the supervisor BEFORE it ticks the executor, deliberately. <c>PathExecutor.AdvanceToNextSegment</c> arms the pause on the tick the segment carrying it completes, so by the supervisor's next look <c>CurrentIndex</c> has already moved past the breathing cell. <see cref="LifeSafetySupervisor"/>'s route arm walks forward from there, prices the leg to the NEXT breathing node, and compares it against a lung that is by definition at its lowest - the body has just finished a submerged leg. So the arm fires at exactly the cell where the plan was about to refill it, one tick before the refill.</para>
    /// <para><b>Standing down is safe, and it is not a weakening.</b> <c>TickBreathHold</c> carries three air clocks of its own - the hard floor at <c>air 0</c>, gained-then-lost, and no gain inside <c>BreathHoldGraceTicks</c> - so a pause at a cell that is not an air source abandons itself in about two dozen ticks and hands the navigator a replan. That is tighter than <see cref="LifeSafetySupervisor.MaxSurfacingTicks"/>, so the backstop is not the faster of the two here. The health arm is untouched and still pre-empts a hold.</para>
    /// <para>The fixture is E30's bell in miniature: a sealed lidded lane with a one-cell hole at the start and a second one part-way along, and a submerged run to the mouth that is long enough that the leg AFTER the bell prices a threshold above the lung the body reaches the bell on.</para>
    /// </remarks>
    [Fact]
    public async Task Supervisor_DoesNotSurfaceABodyThatIsSpendingAScheduledBreathHold()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartLidHoleTrenchAsync(length: 44, bellAtX: 20);
        await using (loop)
        {
            PlanCapture? planned = holder.CapturePlan(new GoalNear(45, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            double scheduled = executor.Segments.Sum(s => s.BreathHoldTicks);
            _output.WriteLine(
                $"{executor.TotalSegments} segments, "
                + $"{executor.Segments.Count(s => s.BreathHoldTicks > 0.0)} scheduled pauses, "
                + $"{scheduled:F1} ticks banked");
            Assert.True(scheduled > 0.0, "the fixture must plan a pause, or the row measures nothing");

            int breathing = 0;
            int surfacing = 0;
            holder.BeginNavigation();
            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 3000 && state == PathExecutorState.InProgress; tick++)
            {
                NavigationTickOutcome outcome = holder.TickNavigation(executor);
                state = outcome.State;
                if (outcome.Preemption == NavigationPreemption.Breathing)
                    breathing++;

                if (outcome.Preemption is NavigationPreemption.Surfacing or NavigationPreemption.Surfaced)
                    surfacing++;

                harness.State.Self.AirSupply = holder.EngineState!.Value.IsUnderWater
                    ? Math.Max(0, harness.State.Self.AirSupply - 1)
                    : Math.Min(harness.State.Self.MaxAirSupply, harness.State.Self.AirSupply + 4);
            }

            _output.WriteLine($"breathing={breathing} surfacing={surfacing} air={harness.State.Self.AirSupply} state={state} seg={executor.CurrentIndex}/{executor.TotalSegments}");
            Assert.True(breathing > 0, "the scheduled pause must be SPENT, not pre-empted");
            Assert.Equal(0, surfacing);
        }
    }

    /// <summary>A body that has just breathed at a one-cell head bell must be able to SETTLE back onto the bore floor and carry on, rather than hanging under the casing until its segment dies.</summary>
    /// <remarks>A sprinting body in water is not pulled down, so a submerged <c>Traverse</c> that begins above its floor cannot settle while sprint remains held. The grounded controller must release sprint while settling, just as <c>SwimTemplate.SinkingIn</c> does. The fixture is the same lidded bore as the row above, and the assertion is that navigation finishes beyond the bell.</remarks>
    [Fact]
    public async Task Executor_SettlesBackOntoTheBoreFloorAfterBreathingAtAHeadBell()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) =
            await StartLidHoleTrenchAsync(length: 44, bellAtX: 20);
        await using (loop)
        {
            PlanCapture? planned = holder.CapturePlan(new GoalNear(45, FloorY, 0, 0));
            Assert.NotNull(planned);
            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);
            Assert.True(
                executor.Segments.Any(s => s.BreathHoldTicks > 0.0),
                "the fixture must plan a pause, or the row measures nothing");

            holder.BeginNavigation();
            PathExecutorState state = PathExecutorState.InProgress;
            for (int tick = 0; tick < 3000 && state == PathExecutorState.InProgress; tick++)
            {
                state = holder.TickNavigation(executor).State;
                harness.State.Self.AirSupply = holder.EngineState!.Value.IsUnderWater
                    ? Math.Max(0, harness.State.Self.AirSupply - 1)
                    : Math.Min(harness.State.Self.MaxAirSupply, harness.State.Self.AirSupply + 4);
            }

            _output.WriteLine(
                $"state={state} at segment {executor.CurrentIndex}/{executor.TotalSegments}, "
                + $"held {executor.BreathHoldTicksPerformed} ticks, air {harness.State.Self.AirSupply}");
            Assert.True(executor.BreathHoldTicksPerformed > 0, "the pause must have been spent");
            Assert.Equal(PathExecutorState.Complete, state);
        }
    }

    /// <summary>The other half of #69: a hold that cannot buy air must never start.</summary>
    /// <remarks><see cref="SurfacingController"/> holds <c>Jump</c> and <c>Sprint</c> with the pitch straight up, so a surfacing is a vertical climb and nothing else. Under a motion-blocking lid that climb reaches no air at all, which is precisely what <see cref="BreathEscape.Unknown"/> says, and the hold then runs to <see cref="LifeSafetySupervisor.MaxSurfacingTicks"/> having achieved nothing, releases, and hands the navigator a replan that returns the same route. That loop is guaranteed to exhaust <c>NavigationBudget.MaxSurfacingRecoveries</c> and is what killed the bot on E19. The class comment already promises "an escape the model cannot see NEVER fires the trip"; it was honoured only on the fallback arm, never on the route arm.</remarks>
    [Fact]
    public async Task Surfacing_NeverStartsWhereTheClimbCanReachNoAir()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartFloodedShaftAsync();
        await using (loop)
        {
            PlanCapture? planned = holder.CapturePlan(new GoalNear(LaneLength + 3, ShaftTopY + 1, 0, 0));
            Assert.NotNull(planned);

            // The lane really is sealed: straight up from the start there is no air within the scan.
            BreathEscape vertical = BreathModel.EscapeTicks(
                planned.Value.Planning, 0, FloorY, 0, PhysicsProfile.ForProtocol(Protocol));
            Assert.False(vertical.IsKnown);

            PathExecutor? executor = holder.BuildExecutor(
                planned.Value, PathfinderOptions.Default, CancellationToken.None)?.Executor;
            Assert.NotNull(executor);

            // One tick of air: any threshold this route could produce is above it, so the ONLY thing keeping the supervisor quiet here is that surfacing would buy nothing.
            harness.State.Self.AirSupply = 1;
            holder.BeginNavigation();

            Assert.Equal(NavigationPreemption.None, holder.TickNavigation(executor).Preemption);
        }
    }

    /// <summary>The supervisor's own ticks-to-air sum, recomputed from the public model so the test measures the same number the trip does.</summary>
    private static double RouteTicksToAir(PathExecutor executor, PlanningWorldView view)
    {
        PhysicsProfile profile = PhysicsProfile.ForProtocol(Protocol);
        double ticks = 0;
        for (int i = executor.CurrentIndex; i < executor.Segments.Count; i++)
        {
            PathSegment segment = executor.Segments[i];
            bool submerged = Submerged(view, segment.Start) || Submerged(view, segment.End);
            ticks += BreathValidator.RealTicks(segment, submerged, profile, executor.AllowSprint);
            if (!Submerged(view, segment.End))
                break;

        }

        return ticks;

        static bool Submerged(PlanningWorldView view, Vec3d point) => BreathModel.IsSubmerged(
            view, (int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Floor(point.Z));
    }

    /// <summary>E5's shape reduced to its two load-bearing features: a two-cell-tall flooded lane under a solid lid, with a collared surfacing hole over the start column and an air bell at x=2, and then an unbroken submerged run from x=3 to the dry mouth at the far end.</summary>
    /// <remarks>Two separate devices, and the distinction is the point. The COLLAR over x=0 is one air cell in the lid with stone above it: a body that rises into it breathes and a surfacing can reach it, but a two-cell-tall body can never stand there, so it is what makes a trip legal without changing the route. The BELL at x=2 is a drained HEAD cell on the lane itself, so the node the route walks through has its eyes in air and IS a breathing node. The bell a few ticks from the start is what makes the trip threshold small; the unbroken eighteen-block run after it is what keeps the route's own peak deficit large.</remarks>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)>
        StartCollaredTrenchAsync()
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
        int waterState = water.MinStateId;

        // The mass, tall enough that an escape scan up a lidded column meets stone rather than the top of the fixture.
        for (int x = -4; x <= TrenchLength + 8; x++)
            for (int y = FloorY - 2; y <= FloorY + 8; y++)
                for (int z = -2; z <= 2; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        // The flooded lane, two cells tall, lid left solid.
        for (int x = 0; x <= TrenchLength; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), waterState);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), waterState);
        }

        // The air bell at x=2: the HEAD cell is drained, so a body standing on the bed there has its eyes in air and the node is a breathing one. The lid over it is untouched, so it is a breath and not an exit.
        world.SetBlockStateId(new BlockPos(2, FloorY + 1, 0), 0);

        // The start collar, one air cell in the lid with stone above it: this is what makes a surfacing from the start column able to reach air at all, which is the precondition for a trip firing.
        world.SetBlockStateId(new BlockPos(0, FloorY + 2, 0), 0);

        // The dry mouth: two cells of air standing on the lane bed, so the route ends breathing.
        for (int x = TrenchLength + 1; x <= TrenchLength + 3; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), 0);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), 0);
        }

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        holder.TickIdle();
        Assert.True(holder.EngineState!.Value.IsUnderWater, "the fixture must start the player submerged");
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }

    /// <summary>E5's LID HOLE with nothing else on the route: a two-cell-tall flooded lane under a solid lid, one collared hole over the start column, and then an unbroken submerged run to the dry mouth.</summary>
    /// <remarks>The difference from <see cref="StartCollaredTrenchAsync"/> is the missing air bell, and it is the whole reason this fixture exists. The bell gives the route a breathing node a few ticks in, which drops its peak deficit to 235.71 and makes a partial refill enough; without one the peak is 261.22 and the hold's frozen target is a FULL LUNG, so the row measures whether a hold can actually reach what it aims at rather than whether it aimed high enough. Both readings are needed: the previous aim and arrival are independent requirements.</remarks>
    /// <param name="length">Cells of flooded lane. Defaults to the shared <see cref="TrenchLength"/>, which is what the two rows that only need the hole use. The route-sized-target row overrides it, because the peak deficit of the continuation is what sizes the hold, and once the plan stopped charging the exit segment for a breathing pause that row's target fell inside a partial refill.</param>
    /// <summary>Asserts the supervisor did not take the navigation away on this tick.</summary>
    /// <remarks>
    /// <para>Not <c>Assert.Equal(None)</c>, because <c>None</c> is narrower than "was not pre-empted". A PRE-EMPTION is the supervisor killing the executor and driving the engine itself - <see cref="NavigationPreemption.Surfacing"/>, <see cref="NavigationPreemption.Surfaced"/>, <see cref="NavigationPreemption.Retreating"/>, <see cref="NavigationPreemption.Retreated"/> - and that is what these rows exist to forbid. <see cref="NavigationPreemption.Interaction"/> and <see cref="NavigationPreemption.Breathing"/> are the two SCHEDULED pauses: the executor is alive, it asked to wait, and it carries on afterwards.</para>
    /// <para>Widened when the plan learned to schedule breathing pauses. These fixtures carry a breathable collar precisely so a route through them has somewhere to breathe, so the plan now schedules a hold there and the driver spends it - which is the feature working, not the supervisor firing. The invariant each row is actually about is unchanged and still asserted: the supervisor never takes an approved route away.</para>
    /// </remarks>
    private static void AssertNotPreEmpted(NavigationTickOutcome outcome)
        => Assert.True(
            outcome.Preemption is NavigationPreemption.None
                or NavigationPreemption.Interaction
                or NavigationPreemption.Breathing,
            $"the supervisor pre-empted an approved route: {outcome.Preemption}");

    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)>
        StartLidHoleTrenchAsync(int length = TrenchLength, int? bellAtX = null)
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
        int waterState = water.MinStateId;

        // The mass, tall enough that an escape scan up a lidded column meets stone rather than the top of the fixture.
        for (int x = -4; x <= length + 8; x++)
            for (int y = FloorY - 2; y <= FloorY + 8; y++)
                for (int z = -2; z <= 2; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        // The flooded lane, two cells tall, lid left solid.
        for (int x = 0; x <= length; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), waterState);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), waterState);
        }

        // The hole: one air cell in the lid over the start column, with stone above it. A body that rises into it breathes and a surfacing can reach it, but a two-cell-tall body can never stand there, so it is a breath and not an exit and the route is unchanged.
        world.SetBlockStateId(new BlockPos(0, FloorY + 2, 0), 0);

        // A second hole of the identical shape part-way along, which is course row E30's bell. The start hole is a breath the body already has; this one is a breath it has to TRAVEL to and then STOP at, which is the only way to get an executor into Phase.AwaitingBreath with a long submerged leg still ahead of it - and that state is what the supervisor was mispricing.
        if (bellAtX is { } bell)
            world.SetBlockStateId(new BlockPos(bell, FloorY + 2, 0), 0);

        // The dry mouth: two cells of air standing on the lane bed, so the route ends breathing.
        for (int x = length + 1; x <= length + 3; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), 0);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), 0);
        }

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        holder.TickIdle();
        Assert.True(holder.EngineState!.Value.IsUnderWater, "the fixture must start the player submerged");
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }

    /// <summary>E19's geometry, scaled to the same numbers: a two-cell-tall flooded lane under solid stone from x=0 to x=12, a one-cell flooded shaft at x=13 rising ten blocks, and a dry corridor off its head. Nowhere on the lane can a vertical climb reach air; the route is the only escape.</summary>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)>
        StartFloodedShaftAsync()
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
        int waterState = water.MinStateId;

        // The mass, carved afterwards. Tall enough that the escape scan up any lane column runs into stone rather than off the top of the fixture.
        for (int x = -4; x <= LaneLength + 6; x++)
            for (int y = FloorY - 2; y <= ShaftTopY + 4; y++)
                for (int z = -2; z <= 2; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        // The lane: feet and head cells both water, lid immediately above.
        for (int x = 0; x < LaneLength; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), waterState);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), waterState);
        }

        // The shaft: water to its top cell, air above it, so a swimmer surfaces at ShaftTopY.
        for (int y = FloorY; y <= ShaftTopY; y++)
            world.SetBlockStateId(new BlockPos(LaneLength, y, 0), waterState);

        // The dry corridor off the shaft head, standing on the ShaftTopY plane.
        for (int x = LaneLength; x <= LaneLength + 4; x++)
        {
            world.SetBlockStateId(new BlockPos(x, ShaftTopY + 1, 0), 0);
            world.SetBlockStateId(new BlockPos(x, ShaftTopY + 2, 0), 0);
        }

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        holder.TickIdle();
        Assert.True(holder.EngineState!.Value.IsUnderWater, "the fixture must start the player submerged");
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }

    private static PathExecutor Navigate(PhysicsEngineHolder holder, out PlanCapture capture)
    {
        PlanCapture? planned = holder.CapturePlan(new GoalNear(TrenchLength + 1, FloorY, 0, 0));
        Assert.NotNull(planned);
        capture = planned.Value;
        PathExecutor? executor = holder.BuildExecutor(capture, PathfinderOptions.Default, CancellationToken.None)?.Executor;
        Assert.NotNull(executor);
        return executor;
    }

    /// <summary>A two-cell-tall corridor through solid stone, flooded from x=0 to x=19 under a solid lid and dry at both mouths, with the player standing submerged at <paramref name="startX"/>.</summary>
    /// <param name="startX">The lane cell the player stands in.</param>
    /// <param name="lid">Whether the trench is roofed at all.</param>
    /// <param name="breatheHoleAtX">Optional: turn the lid cell over this column into air, which makes the column BREATHABLE without making it an exit. A swimmer that rises one block has its head in that cell and its lung refills; the cell above is still stone, so a body (two cells tall) can never occupy it and the planner cannot route through it. That is E5's collared surface hole in one cell, and it is what a surfacing needs to exist for a trip to be allowed to fire at all - see <c>Surfacing_NeverStartsWhereTheClimbCanReachNoAir</c>. Without it the whole trench is sealed and a hold could only run to <see cref="LifeSafetySupervisor.MaxSurfacingTicks"/> having bought nothing.</param>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder, IAsyncDisposable Loop)> StartAsync(
        int startX, bool lid = true, int? breatheHoleAtX = null)
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
        int waterState = water.MinStateId;

        int top = lid ? FloorY + 3 : FloorY - 1;
        for (int x = -4; x <= TrenchLength + 3; x++)
            for (int y = FloorY - 1; y <= top; y++)
                for (int z = -2; z <= 2; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        for (int x = -3; x <= TrenchLength + 2; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), 0);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), 0);
        }

        for (int x = 0; x < TrenchLength; x++)
        {
            world.SetBlockStateId(new BlockPos(x, FloorY, 0), waterState);
            world.SetBlockStateId(new BlockPos(x, FloorY + 1, 0), waterState);
        }

        if (breatheHoleAtX is { } holeX)
            world.SetBlockStateId(new BlockPos(holeX, FloorY + 2, 0), 0);

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(startX + 0.5, FloorY, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();

        // The engine senses fluids during a step, not during a reset, so one idle tick is what makes PhysicsState.IsUnderWater true for a player that is standing in water to begin with.
        holder.TickIdle();
        Assert.True(holder.EngineState!.Value.IsUnderWater, "the fixture must start the player submerged");
        await Task.CompletedTask.ConfigureAwait(false);
        return (harness, holder, scheduler);
    }
}
