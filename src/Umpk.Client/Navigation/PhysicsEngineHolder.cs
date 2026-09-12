using Microsoft.Extensions.Logging;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;

namespace Umpk.Client.Navigation;

/// <summary>Owns the local-player <see cref="PlayerPhysics"/> engine and the physics-condition push contract. The client pushes conditions computed from self state (abilities, effects, and game mode) whenever that state changes. All engine calls run on the session loop.</summary>
internal sealed class PhysicsEngineHolder
{
    private readonly ClientSessionServices _services;
    private readonly PhysicsProfile _profile;
    private readonly IBlockShapeSource _shapes;
    private readonly ILogger _logger;
    private PlayerPhysics? _engine;
    private WorldPhysicsView? _view;
    private Umpk.Game.World.World? _installedWorld;
    private readonly MovingPistonTracker _pistons;

    /// <summary>The held key record requested by the controller on the most recent physical tick.</summary>
    internal MovementInput LastRequestedInput { get; private set; }

    /// <summary>The input actually applied after entity sprint eligibility was resolved.</summary>
    internal MovementInput LastAppliedInput { get; private set; }

    /// <summary>Monotonic count of local-player input steps, used to enforce one step per client tick.</summary>
    internal long StepSequence { get; private set; }

    /// <summary>The squared horizontal distance from the anchor below which the station hold presses nothing. A deadband, so the hold does not oscillate about the anchor.</summary>
    /// <remarks>The cell-centre anchor keeps any valid submerged arrival inside its goal cell. This radial deadband allows small drift without oscillation; <see cref="HoldStationAxisDeadband"/> remains the tighter per-axis limit.</remarks>
    private const double HoldStationToleranceSq = 0.04;

    /// <summary>Ticks a breathing pause may run without the lung rising before it is abandoned as a bad cell.</summary>
    /// <remarks>
    /// <para>This tests the OUTCOME, not the geometry, and that is the whole point of it. The cell predicate cannot tell "there is air above this cell" from "the body is actually breathing": the body may be a fraction of a block too low, the pocket may have been filled in since the world was captured, or the surface may be occupied. A rising lung is the only observation that distinguishes all of those with one test, and it costs nothing.</para>
    /// <para>Twenty-four ticks. Sized above the four ticks a pitch swing takes and the handful a body needs to settle at an opening, and far below <c>LifeSafetySupervisor.MaxSurfacingTicks</c>, so a cell that is not working is abandoned long before the reactive arm would have to rescue it.</para>
    /// </remarks>
    internal const int BreathHoldGraceTicks = 24;

    private BreathHold? _breathHold;

    /// <summary>What one running breathing pause has done so far, for the abandon rules and the log.</summary>
    /// <param name="Anchor">The cell the plan scheduled the pause at.</param>
    /// <param name="EntryAir">The lung the pause began on. Reported, and the log's <c>from</c>.</param>
    /// <param name="BestAir">The highest lung seen. Reported on an abandon, where it is the number that says WHICH way the pause was going wrong.</param>
    /// <param name="Ticks">Total ticks this pause has run.</param>
    /// <param name="WindowTicks">Ticks into the CURRENT judgement window.</param>
    /// <param name="WindowStartAir">The lung at the start of the current judgement window, which is what the trend is measured against.</param>
    private readonly record struct BreathHold(
        Vec3d Anchor, int EntryAir, int BestAir, int Ticks, int WindowTicks, int WindowStartAir);

    /// <summary>The per-axis magnitude below which a correction axis is not pressed at all.</summary>
    private const double HoldStationAxisDeadband = 0.05;

    /// <summary>Set when navigation finishes with the player hanging on a climbable, so the idle tick keeps holding instead of undoing the arrival. See <see cref="IdleInput"/>.</summary>
    private bool _holdClimbable;

    /// <summary>The life-safety check that runs before every executor tick. It is per-holder rather than per-navigation so its health edge and any running surfacing survive a replan; the navigator re-arms it at the start of each navigation through <see cref="BeginNavigation"/>.</summary>
    private readonly LifeSafetySupervisor _supervisor;

    /// <summary>The CENTRE of the cell a completed navigation ended in while standing in a current, or null. See <see cref="IdleInput"/>: without it the idle tick presses nothing and the flow carries the player away from the arrival the caller was just told about.</summary>
    /// <remarks>The cell centre rather than the arrival position is required because <see cref="HoldStationInput"/> parks the body a fixed offset downstream of this point, so an anchor near the low edge of the goal cell parks the body outside it. See the assignment site.</remarks>
    private Vec3d? _holdAgainstCurrent;

    /// <summary>Whether a completed navigation left a station hold for the idle tick. For tests.</summary>
    public bool HasStationHold => _holdAgainstCurrent is not null;

    /// <summary>The station hold's anchor, or null when no hold is armed. Read-only, and internal rather than public because it exists for exactly one reason: WHERE the hold anchors is the only part of it that cannot be observed from outside, and it is the part that was wrong.</summary>
    internal Vec3d? StationHoldAnchor => _holdAgainstCurrent;

    public PhysicsEngineHolder(ClientSessionServices services, IBlockShapeSource shapes, ILogger logger)
    {
        _services = services;
        _shapes = shapes;
        _logger = logger;
        _profile = PhysicsProfileFactory.FromJavaVersion(services.Version);
        _supervisor = new LifeSafetySupervisor(_profile);

        // The piston table is looked up here rather than threaded through UmpkClient: it is a pure function of the protocol, and a version the dataset never measured yields a source whose HasData is false, which MovingPistonTracker reads as "model the head and base only". The world-border containment era is the same kind of pure protocol function (see WorldBorderState.ContainmentEraForProtocol), computed here for the same reason.
        _pistons = new MovingPistonTracker(
            shapes,
            Umpk.Data.Java.JavaGameData.BlockPushData(services.Version.Version.Protocol),
            () => _services.State.WorldOrNull,
            Umpk.Game.World.WorldBorderState.ContainmentEraForProtocol(services.Version.Version.Protocol));
    }

    /// <summary>Ensures the engine exists once the world is available, and rebuilds it whenever a DIFFERENT world has been installed; resets it at the current position either way.</summary>
    /// <remarks>The guard is world IDENTITY, not "is there an engine". <c>WorldPhysicsView</c> closes over the world instance it was built from, so a respawn or dimension change (which calls <c>ClientState.InstallWorld</c> with a brand new <c>World</c>) would otherwise leave the engine colliding against the world the session left behind: solid where the new dimension is air and air where it is solid. This runs on the idle tick and on both navigation paths, so it stays a null check plus a reference comparison in the steady state.</remarks>
    public void EnsureEngine()
    {
        Umpk.Game.World.World? world = _services.State.WorldOrNull;
        if (world is null)
            return;

        if (_engine is not null && ReferenceEquals(_installedWorld, world))
            return;

        _installedWorld = world;
        _holdClimbable = false;
        _holdAgainstCurrent = null;
        LastRequestedInput = MovementInput.None;
        LastAppliedInput = MovementInput.None;

        // A respawn or dimension change replaces the world; a piston that was mid-extension in the world the session left behind must not keep pushing in the new one.
        _pistons.Clear();
        _view = new WorldPhysicsView(world, _shapes);
        _engine = new PlayerPhysics(_view, _profile);
        SelfState self = _services.State.Self;
        _engine.Reset(self.Position, self.Yaw, self.Pitch);
        _engine.SetVelocity(self.Velocity);
        PushConditions();
    }

    /// <summary>Recomputes and pushes physics conditions from tracked self state.</summary>
    public void PushConditions()
    {
        if (_engine is null)
            return;

        SelfState self = _services.State.Self;
        var conditions = new PhysicsConditions
        {
            CreativeFlying = self.Flying,
            MayFly = self.MayFly,
            GameMode = self.GameMode,
            BaseMovementSpeedAttribute = ReadMovementSpeed(),
            FlyingSpeed = self.FlyingSpeed,
            HasJumpBoost = TryEffect(ConditionEffect.JumpBoost, out int jump),
            JumpBoostAmplifier = jump,
            HasSlowFalling = TryEffect(ConditionEffect.SlowFalling, out _),
            HasLevitation = TryEffect(ConditionEffect.Levitation, out int lev),
            LevitationAmplifier = lev,
            HasDolphinsGrace = TryEffect(ConditionEffect.DolphinsGrace, out _),
            WaterMovementEfficiency = ReadWaterMovementEfficiency(),
            SneakingSpeedFactor = ReadSneakingSpeed(),
            MovementEfficiency = ReadMovementEfficiency(),
            SoulSpeedLevel = ReadSoulSpeedLevel(),
            PowderSnowWalkable = ReadPowderSnowWalkable(),
            ElytraEquipped = false,
            ElytraFlying = false,
            UltraWarmDimension = IsUltraWarm(),
        };

        _engine.SetConditions(in conditions);
    }

    /// <summary>Whether the installed world's dimension TYPE is the one vanilla marks <c>ultrawarm</c>, which is the only thing the fluid-push scale needs from the dimension (<c>water-contact processing</c>: 0.007 there, 0.0023333333333333335 elsewhere).</summary>
    /// <remarks>This reads the dimension-type KEY, not an <c>ultrawarm</c> field, because the registry decode does not carry that field into <c>DimensionTypeDefinition</c> yet. Vanilla ships exactly one ultra-warm dimension type, <c>minecraft:the_nether</c>, so this is exact for a vanilla server and conservative (reports false) for a datapack type that sets the flag itself.</remarks>
    private bool IsUltraWarm()
    {
        Umpk.Game.World.World? world = _services.State.WorldOrNull;
        return world is not null && world.Dimension.Type.Id == NetherDimensionType;
    }

    private static readonly Identifier NetherDimensionType = Identifier.Minecraft("the_nether");

    /// <summary>Advances air supply from the engine's current water sensing. Called once per client tick, whether or not a movement lease is held.</summary>
    /// <remarks>
    /// <para>Order matters: air supply reads the water state left by the previous tick, so this runs before the idle physics step.</para>
    /// <para>The engine supplies <see cref="PhysicsState.IsUnderWater"/>. Without an engine, the tracked value remains the last server value and air supply is metadata-only.</para>
    /// </remarks>
    public void TickAirSupply()
    {
        EnsureEngine();
        if (_engine is null)
            return;

        SelfState self = _services.State.Self;
        self.AirSupply = AirSupplyRule.Next(self.AirSupply, EyeIsDrowningIn(_engine.State), IsDrowningImmune(self));
    }

    /// <summary>Whether the eye is in water that DROWNS, which is not the same question as whether the eye is in water.</summary>
    /// <remarks>
    /// <para>A bubble column remains water for buoyancy and drag, but an eye inside one refills air at four units per tick instead of draining it. The exemption is evaluated at the eye cell.</para>
    /// <para>This matches <c>BreathModel.IsSubmerged</c>, keeping planning and execution consistent.</para>
    /// </remarks>
    private bool EyeIsDrowningIn(in PhysicsState state)
    {
        if (!state.IsUnderWater)
            return false;

        if (_view is null)
            return true;

        Vec3d eye = state.EyePosition;
        return !Umpk.Pathfinding.Moves.MoveHelper.IsBubbleColumn(_view.GetBlock(BlockPos.Containing(eye)));
    }

    /// <summary>Whether the local player is immune to drowning through creative flight, water breathing, or conduit power.</summary>
    /// <remarks>
    /// Players are not in the <c>can_breathe_under_water</c> entity-type tag, so only creative invulnerability and the two tracked effects matter here.
    /// <para>This guard is not cosmetic. It covers exactly the cases where the SERVER's own value does not move either, so no <c>set_entity_data</c> is sent and there is nothing to correct a wrong prediction with: an unguarded client would drain a creative-mode or water-breathing player to the drowning point and leave it there for as long as it stayed submerged.</para>
    /// </remarks>
    private bool IsDrowningImmune(SelfState self) => self.Invulnerable || HasWaterBreathing(self);

    /// <summary>Whether <c>minecraft:water_breathing</c> or <c>minecraft:conduit_power</c> is active.</summary>
    private bool HasWaterBreathing(SelfState self) =>
        TryEffect(ConditionEffect.WaterBreathing, out _) || TryEffect(ConditionEffect.ConduitPower, out _);

    /// <summary>Runs a single idle physics step (no input), syncing the result into self state.</summary>
    public void TickIdle()
    {
        EnsureEngine();
        if (_engine is null)
            return;

        SelfState self = _services.State.Self;
        _engine.SetRotation(self.Yaw, self.Pitch);
        ApplyInput(IdleInput(_engine.State));
    }

    /// <summary>The idle tick's input. Nothing pressed, which on solid ground is exactly "stay where you are", except when a navigation ended with the player hanging on a climbable: descent is clamped to -0.15 and pinned to zero only while sneaking, so releasing every input on a rung slides the player back down the whole shaft. Without this, <c>move</c> to a ladder rung reports the arrival it is about to undo: measured 2 blocks of slide in 0.7 seconds, and the server's copy of the player back on the floor by the time anything reads it.</summary>
    /// <remarks>The latch is deliberately narrow. It is set only by a navigation that COMPLETED while off the ground on a climbable, and it clears the moment the player is grounded, is no longer on a climbable, or is moved by anything else (teleport, world rebuild). Everywhere else the idle tick stays byte-for-byte vanilla: a player who falls into a ladder shaft still slides to the bottom.</remarks>
    private MovementInput IdleInput(in PhysicsState state)
    {
        if (_holdClimbable)
        {
            if (!state.OnClimbable || state.OnGround || InScaffolding(state))
            {
                _holdClimbable = false;
                return MovementInput.None;
            }

            return new MovementInput { Sneak = true };
        }

        return HoldStationInput(state);
    }

    /// <summary>Whether the body's feet cell is scaffolding, which is the one block where a <c>Sneak</c> press means "sink" rather than "hold".</summary>
    /// <remarks>
    /// <para>Read through <see cref="_installedWorld"/> rather than through a new <c>PhysicsState</c> bit: the holder keeps that field reference-identical to the world the engine was built from (<see cref="EnsureEngine"/>), so it is the same world the engine's own feet-cell read used, and a public-API addition to <c>Umpk.Physics</c> whose only consumer is this file would be the larger of the two changes. Null-checked because the field is null until a world is installed.</para>
    /// <para>The climb clamp does not pin descent in scaffolding, and the decision uses the floored feet cell.</para>
    /// </remarks>
    private bool InScaffolding(in PhysicsState state)
        => _installedWorld is { } world
        && world.GetBlock(
            Umpk.Geometry.BlockPos.Containing(state.Position.X, state.Position.Y, state.Position.Z)).IsScaffolding;

    /// <summary>The idle input that keeps a player standing in flowing water on the block a navigation just put it on, by walking back toward the arrival position.</summary>
    /// <remarks>
    /// <para>This is the water counterpart of the climbable hold above, and it exists for the same reason: the idle tick releasing every input is "stay where you are" ONLY on still ground. Vanilla pushes an input-less entity with the current every tick (<c>fluid-contact processing</c> / <c>fluid-flow calculation</c>, ported in <c>PlayerPhysics.ApplyFluidPushing</c> at <c>WaterPushScale</c> 0.014), so a bot that arrives in a stream and then presses nothing is carried straight back out of it. A human player standing in a stream walks against it; so does this.</para>
    /// <para>The latch is as narrow as the climbable one: set only by a navigation that COMPLETED in water, cleared the moment the player leaves the water, is moved by anything else (teleport, world rebuild), or the next navigation starts. Outside that window the idle tick stays byte-for-byte vanilla, so a player who merely falls into a river still drifts with it.</para>
    /// </remarks>
    /// <summary>One tick of a scheduled breathing pause: hold station at the cell the plan named, watch the lung, and decide whether to carry on holding, release, or abandon.</summary>
    /// <remarks>
    /// <para><b>The input is NOT <c>SurfacingController.Next</c>.</b> That controller is an emergency vertical CLIMB - it latches <c>Sprint</c> for lift and deliberately never presses <c>Forward</c>, because in a shaft lateral drift is a wall. A scheduled pause is the opposite problem: the body is already at the cell it wants and the job is to STAY there, against a current if there is one. So this reuses <see cref="InputToward"/>, the same primitive <see cref="HoldStationInput"/> station-keeps a completed navigation with, and never presses <c>Sprint</c>. <c>Jump</c> is added while the body is in water, which is what keeps a floating body's head up in the pocket rather than letting it sink out of it.</para>
    /// <para><b>Water breathing short-circuits the wait.</b> Either <c>water_breathing</c> or <c>conduit_power</c> prevents lung depletion, and while either is held the lung does not deplete at all - so there is nothing to refill and the pause is unnecessary. This is a RELEASE, not an abandon: the route is fine, it simply does not need to stop. It also has to be handled explicitly, because an immune body's lung never rises, so the grace window below would otherwise abandon a perfectly good route on the strength of a lung that was never going to move. The same predicate is used as everywhere else, deliberately: a second, narrower test that asked only about the potion would silently drop conduit power.</para>
    /// <para><b>Four ways out, and each answers a different failure.</b> The lung reaching full is the ordinary one. The other three are all air clocks rather than wall clocks, which is the point: a body holding at a cell that turns out to be wet drains at one a tick, so a hold entered on air 40 reaches the drowning point on tick 60 with any wall-clock cap of 80 or 150 none the wiser.</para>
    /// <list type="number">
    /// <item><description><b>The hard floor.</b> The lung reaching zero abandons, unconditionally.</description></item>
    /// <item><description><b>Gained, then lost.</b> A lung that rose and has started falling again says
    /// the cell WAS working and stopped - the pocket was taken, or the body drifted off it.</description></item>
    /// <item><description><b>Never gained.</b> No rise at all inside the grace window says the cell was
    /// never an air source, whatever its geometry claimed.</description></item>
    /// </list>
    /// <para>Immediate abandonment on the first air decrease is too strict because the body may arrive submerged and lose one or two ticks before reaching the pocket. The grace window bounds that initial loss while keeping the limit tied to air supply rather than wall-clock time.</para>
    /// <para>The engine IS stepped here, unlike the interaction hold, and for the reason that hold does not step it: a door is opened on dry land where an idle tick would move a body that should stand still, while a breath hold is taken in water, where pressing nothing is exactly what lets a current carry the body off the air it is standing on.</para>
    /// </remarks>
    private NavigationTickOutcome TickBreathHold(
        PathExecutor executor, BreathHoldRequirement breath, bool deviation)
    {
        SelfState self = _services.State.Self;
        int air = self.AirSupply;

        BreathHold hold = _breathHold is { } running
            ? running with
            {
                BestAir = Math.Max(running.BestAir, air),
                Ticks = running.Ticks + 1,
                WindowTicks = running.WindowTicks + 1,
            }
            : new BreathHold(breath.Cell, air, air, 1, 1, air);
        _breathHold = hold;

        // Water breathing or conduit power: nothing to refill, so nothing to wait for.
        if (HasWaterBreathing(self))
            return ReleaseBreathHold(executor, hold, air, "the lung is suspended", deviation);

        // The hard floor. Whatever else is true, a hold must never ride the lung down to drowning.
        if (air <= 0)
            return AbandonBreathHold(hold, air, "the lung reached the drowning point");

        // THE ONE JUDGEMENT: over a whole window, is the lung higher than it was at the window's start?
        //
        // This replaces two per-tick rules, "the lung fell back from its best" and "no gain in the grace window", and it replaces them because the live course proved a per-tick rule cannot be written honestly at all. Four runs of E30, E31 and E32, at bells that filled the lung to 300 on other runs of the same rows from the same anchors:
        //
        //   entry 181, best 184, air 179     entry  83, best  87, air  82
        //   entry 160, best 164, air 159     entry  51, best  54, air  48
        //   entry 150, best 154, air 153     entry  64, best  68, air  59
        //
        // The last of those is the one that ends the argument. Nine of lung disappeared inside six ticks, and vanilla drains one a tick, so that number cannot have come from the lung: it is the CLIENT's own prediction being overwritten by a server frame. A body at a one-cell head bell sits with its eye within hundredths of the water line, the client's eye test and the server's disagree for a few ticks at a time, and the server wins - in jumps. Any fixed per-tick bound large enough to absorb that is large enough to rubber-stamp a cell that is genuinely failing.
        //
        // A window has no such problem. Jitter cancels over twenty-four ticks; a trend does not. A cell that is working climbs at a measured 3.2 to 3.5 a tick and clears the bar by seventy; a cell that is not drains one a tick and fails it by twenty-four. Nothing in between needs deciding, because the bar is asked once a window rather than once a tick.
        //
        // It is bounded and it is safe. The worst a bad cell can cost is one window of drain, the hard floor above still catches a lung that runs out inside a window, and the plan's own tick count still bounds a cell that merely breaks even. The sealed-lid pin is unchanged at 23 ticks, because that is precisely the case the grace window was already sized for.
        if (hold.WindowTicks >= BreathHoldGraceTicks)
        {
            if (air <= hold.WindowStartAir)
                return AbandonBreathHold(hold, air, $"no net gain in {BreathHoldGraceTicks} ticks");

            hold = hold with { WindowTicks = 0, WindowStartAir = air };
            _breathHold = hold;
        }

        if (air >= self.MaxAirSupply)
            return ReleaseBreathHold(executor, hold, air, "the lung is full", deviation);

        MovementInput input = BreathHoldInput(_engine!.State, hold.Anchor);
        ApplyInput(input);
        return new NavigationTickOutcome(
            PathExecutorState.InProgress, deviation, NavigationPreemption.Breathing);
    }

    private NavigationTickOutcome ReleaseBreathHold(
        PathExecutor executor, BreathHold hold, int air, string why, bool deviation)
    {
        _logger.LogDebug(
            "Breath hold at {Anchor} released ({Why}): held {Ticks} ticks, air {From} -> {To}.",
            hold.Anchor, why, hold.Ticks, hold.EntryAir, air);
        _breathHold = null;
        executor.NotifyBreathSatisfied();
        return new NavigationTickOutcome(
            PathExecutorState.InProgress, deviation, NavigationPreemption.Breathing);
    }

    /// <summary>Gives up on a breathing pause. The executor is discarded and the navigator replans from where the body actually is, on the lung it actually has.</summary>
    /// <remarks><see cref="NavigationPreemption.Surfaced"/> is reused rather than given a new value, because it already means exactly this to the navigator: "replan to the same goal from where the player now is". The one thing worth writing down is that the DRIVER may now raise it, where before only the life-safety supervisor could. The replan is judged by <c>BreathValidator</c> against the lung the body actually held, so it refuses rather than carrying on - which is the correct answer for a body standing at a broken air source with a partial lung.</remarks>
    private NavigationTickOutcome AbandonBreathHold(BreathHold hold, int air, string why)
    {
        // The BEST air is reported too, and only on this path. It is the number that separates the two ways an abandon can be right from the one way it can be wrong: a cell that never worked reads best == entry, a cell that was taken reads best well above the current air, and a single sample of network jitter reads best barely above it. Without it an abandon says only that one happened, without the information needed to diagnose the hold.
        _logger.LogDebug(
            "Breath hold at {Anchor} ABANDONED ({Why}): held {Ticks} ticks, air {From} -> {To} (best {Best}).",
            hold.Anchor, why, hold.Ticks, hold.EntryAir, air, hold.BestAir);
        _breathHold = null;
        return new NavigationTickOutcome(
            PathExecutorState.InProgress, DeviationExceeded: false, NavigationPreemption.Surfaced);
    }

    /// <summary>The input one tick of a breathing pause presses: hold the named cell, and in water hold <c>Jump</c> so the head stays up in the pocket. Never <c>Sprint</c>.</summary>
    /// <remarks>Internal rather than private for the reason <see cref="InputToward"/> is: it is the input of both a scheduled pause and the second phase of a surfacing, so a fixture that reproduces the driver's navigation tick has to be able to press the same thing the driver presses, rather than restating it in the test's own words and drifting.</remarks>
    internal static MovementInput BreathHoldInput(in PhysicsState state, Vec3d anchor)
    {
        double dx = anchor.X - state.Position.X;
        double dz = anchor.Z - state.Position.Z;
        MovementInput lateral = (dx * dx) + (dz * dz) < HoldStationToleranceSq
            ? MovementInput.None
            : InputToward(dx, dz, state.Yaw);

        return state.InWater ? lateral with { Jump = true } : lateral;
    }

    private MovementInput HoldStationInput(in PhysicsState state)
    {
        if (_holdAgainstCurrent is not { } anchor)
            return MovementInput.None;

        if (!state.InWater)
        {
            _holdAgainstCurrent = null;
            return MovementInput.None;
        }

        double dx = anchor.X - state.Position.X;
        double dz = anchor.Z - state.Position.Z;
        if ((dx * dx) + (dz * dz) < HoldStationToleranceSq)
        {
            // Inside the deadband: pressing nothing here is what stops the hold oscillating around the anchor, and the drift that matters is always larger than this before it is worth correcting.
            return MovementInput.None;
        }

        return InputToward(dx, dz, state.Yaw);
    }

    /// <summary>The horizontal centre of the cell a position sits in, keeping the position's own Y.</summary>
    /// <remarks>The Y is carried through rather than floored because the hold is purely horizontal - only X and Z are read - and a floored Y would be a number that means nothing to anyone reading the anchor back.</remarks>
    private static Vec3d ArrivalCellCentre(in Vec3d position)
        => new(Math.Floor(position.X) + 0.5, position.Y, Math.Floor(position.Z) + 0.5);

    /// <summary>The movement input whose world-space direction points along (<paramref name="dx"/>, <paramref name="dz"/>) for a player facing <paramref name="yaw"/>.</summary>
    /// <remarks><c>PlayerPhysics.GetInputVector</c> rotates the input axes (xxa = Left positive, zza = Forward positive) BY the yaw to get world motion, so recovering the input that produces a world direction is the same rotation by -yaw. Internal rather than private so the sign convention, which is the only part of the station hold that can be silently backwards, is directly testable.</remarks>
    internal static MovementInput InputToward(double dx, double dz, float yaw)
    {
        double sin = Math.Sin(yaw * (Math.PI / 180.0));
        double cos = Math.Cos(yaw * (Math.PI / 180.0));
        double xxa = (dx * cos) + (dz * sin);
        double zza = (dz * cos) - (dx * sin);

        return new MovementInput
        {
            Forward = zza > HoldStationAxisDeadband,
            Back = zza < -HoldStationAxisDeadband,
            Left = xxa > HoldStationAxisDeadband,
            Right = xxa < -HoldStationAxisDeadband,
        };
    }

    /// <summary>Records one block event, which is the ONLY notice a client gets that a piston is moving: <c>piston structure movement</c> writes the <c>moving_piston</c> blocks without <c>UPDATE_CLIENTS</c>, so they are never sent. See <see cref="MovingPistonTracker"/>.</summary>
    public void OnBlockEvent(BlockPos position, int action, int param) =>
        _pistons.OnBlockEvent(position, action, param);

    /// <summary>Ticks the live moving pistons and publishes any displacement into self state.</summary>
    /// <remarks>Called after the tick's movement send because entity movement precedes block-entity piston motion. Putting it before the send would report the push one tick earlier than vanilla does, which is a divergence even though it would look like an improvement against a server that is racing the same two ticks.</remarks>
    public void TickPistons()
    {
        if (_engine is null || _pistons.ActiveCount == 0)
            return;

        _pistons.Tick(_engine);
        SyncSelf(_engine.State);
    }

    /// <summary>The number of moving pistons currently simulated. Exposed for tests.</summary>
    public int ActivePistonCount => _pistons.ActiveCount;

    /// <summary>Whether a body could occupy <paramref name="cell"/> at all: the same occupancy question <see cref="Umpk.Pathfinding.Core.AStarPathFinder"/> asks before it will expand a <see cref="GoalBlock"/>, answered on demand for a caller that has to REPORT the answer rather than search on it. Runs on the session loop, like every other read here.</summary>
    /// <remarks>
    /// <para><b>False means "the loaded world says no body fits here".</b> Everything else answers true: no engine, no world, an unloaded column. That polarity is deliberate and it is the whole contract - the one caller uses a false to tell the user "that block cannot be stood in", and saying that about a column nobody has loaded would be an assertion about terrain the client has never seen. <c>IsGoalReachableFootPosition</c> takes the same view for the same reason.</para>
    /// <para>The region is captured on demand at margin 2, which is the reach of the predicate itself (<c>y - 1</c> for the floor, <c>y + 1</c> for the head, and the swim column's own head read), so this costs at most the sections those few reads fault in rather than a plan-sized box.</para>
    /// </remarks>
    /// <param name="cell">The cell to test.</param>
    /// <param name="options">The search options the asking navigation runs under. They are not decoration: <c>AllowSwim</c> decides whether a water cell is occupiable at all, and <c>AvoidBlocks</c> can make an otherwise ordinary cell a hazard. Answering under <c>PathfinderOptions.Default</c> instead would let the report disagree with the plan that produced it.</param>
    public bool CanStandAt(BlockPos cell, PathfinderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureEngine();
        Umpk.Game.World.World? world = _services.State.WorldOrNull;
        if (_view is null || _engine is null || world is null)
            return true;

        // The WORLD's own column, not CalculationContext.IsChunkLoaded. That one asks whether the captured REGION covers the column, and a region captured around this very cell always does, so it is dead code here: every column nobody has ever loaded would come back "nothing can stand there" on the strength of a snapshot that reads unloaded space as air. Caught by the predicate test rather than by reasoning, which is why that test asks about a column 4000 blocks out.
        if (world.GetColumn(cell) is null)
            return true;

        PlanningWorldView planning = PlanningWorldView.CaptureOnDemand(world, _shapes, cell, cell, margin: 2);
        var ctx = new CalculationContext(
            planning,
            options,
            CapabilityCapture.From(
                _services.State,
                Umpk.Data.Java.JavaGameData.LegacyItemBridgeEra,
                Umpk.Data.Java.JavaGameData.LegacyItemBridgeSource));

        return Umpk.Pathfinding.Moves.MoveHelper.CanStandAt(ctx, cell.X, cell.Y, cell.Z);
    }

    /// <summary>Bounds the planning region for a goal and reads the start position and physics conditions. Returns null when the engine or world is not yet available.</summary>
    /// <remarks>This method only bounds the box and reads <c>Self.Position</c>, the goal hint, and the engine's conditions. <see cref="PlanningWorldView.CaptureOnDemand"/> materialises a section on its first search read, avoiding an O(volume) copy and large-object allocation for each plan and replan.</remarks>
    public PlanCapture? CapturePlan(IGoal goal)
    {
        EnsureEngine();
        if (_view is null || _engine is null)
            return null;

        Umpk.Game.World.World? world = _services.State.WorldOrNull;
        if (world is null)
            return null;

        BlockPos start = BlockPos.Containing(_services.State.Self.Position);
        BlockPos goalHint = ApproximateGoal(goal, start);
        PlanningWorldView planning = PlanningWorldView.CaptureOnDemand(world, _shapes, start, goalHint, margin: 24);

        // The player half of the snapshot, read here for the same reason the region and the air supply are: everything the off-loop search may price has to be observed once, on the loop.
        Umpk.Pathfinding.Core.PathfinderCapabilities capabilities = CapabilityCapture.From(
            _services.State,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeEra,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeSource);

        return new PlanCapture(
            planning, start, goal, _engine.Conditions, _services.State.Self.AirSupply, capabilities);
    }

    /// <summary>The message template for the one per-plan statistics line. Every plan attempt emits it exactly once, INCLUDING the attempts that return null, so a failed navigation is as measurable as a successful one. The rendered shape is stable and machine-readable (<c>Plan computed: status=Success nodes=12345 planMs=87 regionCells=190000 segments=14</c>): the pathfinding harness greps the client debug log for it, so the token names and their order are a contract, not a formatting preference.</summary>
    private const string PlanStatsTemplate =
        "Plan computed: status={Status} nodes={Nodes} planMs={PlanMs} regionCells={RegionCells} segments={Segments}";

    /// <summary>The second per-plan line: what the region actually cost, as opposed to how big it was.</summary>
    /// <remarks>This stays on a separate line because the pathfinding harness and <c>PlanStatsLoggingTests</c> consume the exact token set. <c>regionCells</c> is the captured box, while <c>sections</c> is the number of 16x16x16 sections that the search copied.</remarks>
    private const string PlanRegionTemplate =
        "Plan region: cells={RegionCells} sections={Sections} demandFaulted={DemandFaulted}";

    /// <summary>Runs the A* search over a previously captured region and builds the executor (planning, replanning, and lookahead run on the thread pool). Pure over the immutable capture and profile, so it is safe to invoke off the session loop via <c>Task.Run</c>. Returns null when no path is found.</summary>
    public PlannedRoute? BuildExecutor(PlanCapture capture, PathfinderOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The two-pass, effect-aware plan rather than a bare search: it takes the status-effect arms the capture supports, prices the finished route in real ticks, and drops any arm the effect cannot pay for. With no effects captured it is exactly the one-pass search it replaces.
        EffectAwarePlan plan = EffectAwarePlanner.Plan(
            capture.Planning, options, capture.Start, capture.Goal, capture.Capabilities, _profile, null, ct);
        PathResult result = plan.Result;

        // The region cell count is the size of the box the search was handed, which the search never sees; the node count and the elapsed span come off the search's diagnostics. Together they are what separates "the planner was slow" from "the planner was handed a huge region".
        long regionCells = capture.Planning.Region.CellCount;

        // Read AFTER the search, because on a demand-faulted region this is a running total and the search is what runs it up.
        _logger.LogDebug(
            PlanRegionTemplate,
            regionCells,
            capture.Planning.Region.SectionsCaptured,
            capture.Planning.Region.IsDemandFaulted);

        if (result.Status == PathStatus.Failed || result.Path.Count == 0)
        {
            _logger.LogDebug(
                PlanStatsTemplate,
                result.Status,
                result.Diagnostics.NodesExplored,
                result.Diagnostics.ElapsedMilliseconds,
                regionCells,
                0);
            return null;
        }

        // Against the SAME frozen capture the search ran on, so a segment's resolved elevation comes off exactly the terrain that produced the node it belongs to.
        IReadOnlyList<PathSegment> segments = plan.Segments;
        _logger.LogDebug(
            PlanStatsTemplate,
            result.Status,
            result.Diagnostics.NodesExplored,
            result.Diagnostics.ElapsedMilliseconds,
            regionCells,
            segments.Count);

        if (segments.Count == 0)
            return null;

        if (plan.FireHazardsCleared || plan.BreathSuspended || plan.Searches > 1)
            _logger.LogDebug(
                "Plan effects: fireHazardsCleared={Cleared} breathSuspended={Suspended} searches={Searches} "
                + "dependsOn=[{Depends}]",
                plan.FireHazardsCleared,
                plan.BreathSuspended,
                plan.Searches,
                string.Join(", ", plan.DependsOnEffects));

        // Breath feasibility, before the executor exists. This runs on a PARTIAL result as well as a successful one, deliberately: a search that exhausts its node budget returns the best node it reached and the navigator executes that partial route and reports success, so a partial is exactly as capable of drowning the player as a complete one, and the ocean case that produces partials is the case where drowning is likely. A refusal returns no executor, which the navigator surfaces as the ordinary "no path" failure.
        //
        // It is SKIPPED, and only skipped, under a breath suspension the two-pass plan granted against this route's own real-tick cost. Expressing the suspension as a very large airTicks instead was considered and rejected: BreathModel.RouteBudget and the peak-deficit accounting would still produce numbers, and a number that means "infinity" is a bug waiting to be printed in the one place the operator reads.
        int breathingStops = 0;
        if (!plan.BreathSuspended)
        {
            BreathValidation breath = BreathValidator.Validate(
                segments, capture.Planning, _profile, options.AllowSprint, capture.AirSupply);
            if (!breath.IsSurvivable)
            {
                _logger.LogDebug(
                    "Plan refused on breath: peakDeficit={Deficit} ticks against a {Budget}-tick budget "
                    + "(air {Air}), first violation at segment {Segment} of {Segments}.",
                    breath.PeakDeficitTicks,
                    breath.BudgetTicks,
                    capture.AirSupply,
                    breath.FirstViolationSegment,
                    segments.Count);
                return null;
            }

            breathingStops = breath.BreathingStops;
        }

        // AllowSprint travels with the plan: the search that produced these segments already refused move families and charged WalkOneBlock on the strength of it, so the executor has to honour the same flag or it runs a plan its own planner did not price. The effect arms travel with it for the same reason, and additionally because a flag stashed on this holder would outlive the plan that earned it.
        var context = new PathExecutionContext(
            capture.Planning,
            _profile,
            capture.Conditions,
            options.AllowSprint,
            capture.Capabilities,
            plan.BreathSuspended,
            plan.DependsOnEffects,
            capture.Capabilities.PowderSnowWalkable && StandsOnPowderSnow(segments, capture.Planning));
        // Route navigation telemetry through the host ILogger pipeline. The validation's breathing-stop count travels with the executor rather than being stashed on this holder: BuildExecutor runs off the session loop, and a field would be written on the thread pool and read on the loop.
        return new PlannedRoute(
            new PathExecutor(context, segments, new LoggerPathExecutionObserver(_logger)),
            breathingStops,
            plan.BreathSuspended);
    }

    /// <summary>The ground-friction chain for a default block is <c>blockFriction 0.6 * FrictionMultiplier 0.91 = 0.546</c>. A player that releases every input then travels <c>v + v*f + v*f^2 + ... = v / (1 - f)</c> before it stops, so the coast from the current velocity is that velocity times this factor. Used only to decide WHEN to stop pressing forward; the approach re-reads the real position every tick, so a block with a different friction costs a few extra ticks rather than accuracy.</summary>
    private const double GroundCoastFactor = 1.0 / (1.0 - (0.6 * Umpk.Physics.PhysicsConstants.FrictionMultiplier));

    /// <summary>The velocity below which the player is treated as stopped, in blocks per tick.</summary>
    private const double ApproachSettledSpeed = 0.01;

    /// <summary>One tick of a SUB-BLOCK approach to an exact point, for a destination inside the block the player already occupies. Returns true when the approach has finished.</summary>
    /// <remarks>
    /// The planner and the executor are both block-granular: A* is satisfied by the start node, and every completion predicate in <c>SegmentGeometry</c> (<c>IsFootprintInsideTargetBlock</c>, <c>IsCenterInsideTargetBlock</c>, <c>IsSettledAtEnd</c>) is a test against <c>Math.Floor(target)</c>. So neither layer can express "move 0.38 of a block", which is what <c>move center</c> asks for, and the command reported an arrival while the player had not moved at all.
    /// <para>This is a closed loop on the real engine, not a teleport: it faces the target, holds forward while the coast would still fall short, and releases so vanilla's own friction carries the player in.</para>
    /// </remarks>
    public bool TickApproach(Vec3d target, double tolerance)
    {
        EnsureEngine();
        if (_engine is null)
            return true;

        PhysicsState state = _engine.State;
        double dx = target.X - state.Position.X;
        double dz = target.Z - state.Position.Z;
        double remaining = Math.Sqrt((dx * dx) + (dz * dz));
        double speed = Math.Sqrt((state.Velocity.X * state.Velocity.X) + (state.Velocity.Z * state.Velocity.Z));
        bool settled = speed <= ApproachSettledSpeed;

        if (settled && remaining <= tolerance)
            return true;

        // How much of the remaining distance the CURRENT velocity is already spending, signed: after an overshoot it is negative, and a press then has to undo it as well.
        double towards = remaining > 0.0 ? (((state.Velocity.X * dx) + (state.Velocity.Z * dz)) / remaining) : 0.0;

        // One press is a whole tick of input and cannot be made smaller, so the decision is not "am I still short of the point" but "which of the three inputs vanilla has leaves me CLOSEST to it": coast, a sneaking press, or a walking press. Pressing whenever short is what made the first version of this loop oscillate: from 0.283 away it pressed twice, overshot, turned round and burned its whole budget swinging, and the arrival then drifted 0.105 of a block once the idle tick took over.
        double accel = _engine.Conditions.BaseMovementSpeedAttribute * Umpk.Physics.PhysicsConstants.InputFriction;
        double errorCoast = Math.Abs(remaining - (towards * GroundCoastFactor));
        // The crouch factor MUST be the same value the engine will actually use, not a copy of vanilla's 0.3 default. This term is a PREDICTION of what the engine does with a sneaking press (PlayerPhysics.MapInput scales the raw impulse by PhysicsConditions.SneakingSpeedFactor), and the two diverging does not degrade gracefully: under swift sneak III the engine's factor is 0.75, a controller predicting 0.3 would think a sneaking press lands at 40% of where it actually lands, and the coast/sneak/walk ranking below would pick the wrong input every time.
        double sneakFactor = _engine.Conditions.SneakingSpeedFactor;
        double errorSneak = Math.Abs(remaining - ((towards + (accel * sneakFactor)) * GroundCoastFactor));
        double errorWalk = Math.Abs(remaining - ((towards + accel) * GroundCoastFactor));

        bool walk = errorWalk < errorSneak && errorWalk < errorCoast;
        bool sneak = !walk && errorSneak < errorCoast;

        if (settled && !walk && !sneak)
        {
            // Stopped as close as vanilla movement can get. The smallest press it has is a whole tick of sneaking input, worth about 0.065 of a block once friction has finished with it AT THE DEFAULT 0.3 CROUCH FACTOR - swift sneak raises the factor and therefore coarsens this floor, to about 0.162 of a block at level III, which is why the ranking above reads the resolved factor rather than assuming the default. From here every available input would end further away than standing still does.
            return true;
        }

        float yaw = (float)(Math.Atan2(-dx, dz) * 180.0 / Math.PI);
        _engine.SetRotation(yaw, state.Pitch);

        // Inside a scaffolding column a sneaking press is not a shorter step, it is a descent: vanilla's climb clamp exempts scaffolding, so the bit that trims 0.065 of a block on ordinary ground costs 0.15 of altitude here. Fall back to the walking press, which is the ranking's other option and cannot move the body vertically.
        bool sneakHere = sneak && !InScaffolding(state);
        ApplyInput(new MovementInput { Forward = walk || sneak, Sneak = sneakHere });
        return false;
    }

    /// <summary>Re-arms the navigation-scoped state at the start of a navigation: any station hold left behind by the previous one, and the life-safety supervisor's health edge.</summary>
    /// <remarks><c>_holdAgainstCurrent</c> is set when navigation ends in a current, and the idle tick steers back to it while it remains set. Clearing it here prevents a new navigation from inheriting a stale anchor when its lease is released.</remarks>
    public void BeginNavigation()
    {
        _holdClimbable = false;
        _holdAgainstCurrent = null;
        LastRequestedInput = MovementInput.None;
        LastAppliedInput = MovementInput.None;
        _reclose = null;
        _supervisor.Reset(_services.State.Self.Health);
    }

    /// <summary>The barrier a crossing in progress depends on, and the segment index that crossing belongs to. Null whenever no interaction has been satisfied for the segment currently executing.</summary>
    private (BlockPos Witness, int SegmentIndex)? _reclose;

    /// <summary>Releases an executor's interaction hold, having read the barrier's REAL panel side out of the live world, and arms the reclose watch for the crossing that follows.</summary>
    /// <remarks>
    /// <para>The panel side has to come from here rather than from the executor, because the executor's world is the frozen plan capture in which the door is still shut - and a closed door's panel is ninety degrees off the one the body is about to squeeze past. This is the live view, on the loop, after the open has been observed.</para>
    /// <para>It is also where the reclose watch is armed, for the segment that enters the doorway and the one that leaves it - the same pair <c>BarrierCrossing</c> is attached to, and for the same reason: the body's footprint is beside the panel for the whole width of the cell.</para>
    /// </remarks>
    /// <param name="executor">The executor holding for the interaction.</param>
    /// <param name="witness">The barrier whose <c>open</c> the interaction was verified against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="executor"/> is null.</exception>
    public void CompleteInteraction(PathExecutor executor, BlockPos witness)
    {
        ArgumentNullException.ThrowIfNull(executor);

        BarrierCrossing? observed = null;
        if (_view is not null
            && BarrierCrossing.TryResolve(_view, witness.X, witness.Y, witness.Z, out BarrierCrossing crossing))
            observed = crossing;

        _reclose = (witness, executor.CurrentIndex);
        executor.NotifyInteractionSatisfied(observed);
    }

    /// <summary>Whether an effect the running plan was priced on is no longer held.</summary>
    /// <remarks>
    /// <para>Read off the live effect table rather than off a <c>remove_mob_effect</c> subscription, and the reason is that this is the same question asked on every tick anyway. The table holds a handful of entries and <c>DependsOnEffects</c> holds at most two, so the check is a couple of dictionary probes on a loop that is already doing a physics step; a subscription would buy nothing and would add a lifetime to manage across replans. It also catches every way an effect can leave - the removal packet, a respawn that clears the table (<c>SelfState.ClearEffects</c>), a world rebuild - rather than only the one that has a packet.</para>
    /// <para>An effect the plan does not lean on is not watched at all, which is what keeps this from replanning every time any potion in the hotbar runs out.</para>
    /// </remarks>
    private bool LostALoadBearingEffect(PathExecutor executor)
    {
        IReadOnlyList<Identifier> depends = executor.Context.DependsOnEffects;
        if (depends.Count == 0)
            return false;

        RegistryAccess? registries = _services.State.Registries;
        if (registries is null)
            return false;

        for (int i = 0; i < depends.Count; i++)
            if (registries.MobEffects.TryGetNetworkId(depends[i], out int networkId)
                && !_services.State.Self.ActiveEffects.ContainsKey(networkId))
                return true;

        return false;
    }

    /// <summary>Whether any segment of this route rests a body on powder snow, which is what makes the route depend on the leather boots the capture found.</summary>
    /// <remarks>Resolved ONCE, here, rather than per tick: it is a scan of the segment list against the frozen capture, and both are immutable for the life of the executor. The support cell is read under the segment's own <c>EndFeetY</c>, which is the planner's integer feet cell and therefore names the column the body's weight is on, not the resolved elevation.</remarks>
    private static bool StandsOnPowderSnow(IReadOnlyList<PathSegment> segments, IPhysicsWorldView world)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            PathSegment segment = segments[i];
            if (SupportIsPowderSnow(world, segment.End, segment.EndFeetY)
                || (i == 0 && SupportIsPowderSnow(world, segment.Start, segment.StartFeetY)))
                return true;

        }

        return false;
    }

    private static bool SupportIsPowderSnow(IPhysicsWorldView world, Vec3d at, int feetY)
        => world.GetBlock(
            new BlockPos((int)Math.Floor(at.X), feetY - 1, (int)Math.Floor(at.Z))).IsPowderSnow;

    /// <summary>The leather boots a route that stands on powder snow was planned on are no longer worn. The mirror of <see cref="LostALoadBearingEffect"/>, watched beside it and raising the same pre-emption.</summary>
    /// <remarks>
    /// <para>This is the one capability in the capture whose loss removes a FLOOR rather than a cost saving. <c>PhysicsConditions</c> is pushed on change, so the engine stops handing the body a cube the moment the boots come off, while the plan still names the lane as walkable; the body then drops into a freezing pit on a route that no longer exists. Replanning bootless refuses that lane, which is the honest answer.</para>
    /// <para>Scoped by <c>DependsOnPowderSnowBoots</c>, so a route that never stands on snow is not watched at all and taking boots off costs it nothing.</para>
    /// </remarks>
    private bool LostThePowderSnowBoots(PathExecutor executor)
        => executor.Context.DependsOnPowderSnowBoots && !ReadPowderSnowWalkable();

    /// <summary>Prints the one line a surfacing trip and the one line its release are owed, if either happened on the tick just evaluated.</summary>
    /// <remarks>The report states what fired the hold, how long it ran, and whether it gained air. The breathing schedule uses the same shape (<see cref="TickBreathHold"/>); this is the corresponding report for the backstop, and the two are deliberately worded alike so one reading of a transcript covers both.</remarks>
    private void ReportSurfacing()
    {
        if (_supervisor.TryTakeSurfacingTrip(out SurfacingTrip trip))
            _logger.LogDebug(
                "Surfacing began ({Why}): air {Air} under a {Threshold}-tick bar; releasing at {Resume} "
                + "(route bar {Bar}).",
                trip.Why, trip.Air, trip.Threshold, trip.ResumeAir, trip.Bar);

        if (_supervisor.TryTakeSurfacingArrival(out Vec3d anchor))
            _logger.LogDebug(
                "Surfacing reached air at {Anchor}; holding there for the lung rather than climbing on.",
                anchor);

        if (_supervisor.TryTakeSurfacingRelease(out SurfacingRelease release))
            _logger.LogDebug(
                "Surfacing released ({Why}): held {Ticks} ticks, air {From} -> {To} (best {Best}), "
                + "wanted {Resume}.",
                release.Why, release.Ticks, release.EntryAir, release.ExitAir, release.BestAir, release.ResumeAir);

    }

    /// <summary>Ticks the executor once and steps the engine; returns the navigation outcome.</summary>
    public NavigationTickOutcome TickNavigation(PathExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        EnsureEngine();
        if (_engine is null || _view is null)
            return new NavigationTickOutcome(PathExecutorState.Failed, DeviationExceeded: false, NavigationPreemption.None);

        // A load-bearing effect that has gone. This runs FIRST, before even life safety, because the plan the supervisor is about to price the remaining route against is a plan whose premise has just been withdrawn: the route it would judge was chosen on the strength of a potion that no longer exists. The body is not stepped and the executor is not ticked - the honest next move is a new plan made with the capabilities the player actually has, which is the navigator's job. The supervisor is the in-flight backstop for the ticks in between, and it does not need the replan to have happened: its breath arm reads the LIVE effect, so it is already awake.
        if (LostALoadBearingEffect(executor) || LostThePowderSnowBoots(executor))
            return new NavigationTickOutcome(
                PathExecutorState.InProgress, DeviationExceeded: false, NavigationPreemption.CapabilityLost);

        // Life safety runs BEFORE the executor, because the point of it is that the executor is about to do something that kills the player. Once it has stepped the engine even once the executor's one-tick deviation baseline is stale and private, so the executor is dead by contract and the navigator has to replan; that is what the Surfaced outcome says.
        switch (_supervisor.Evaluate(
            _engine.State, _services.State.Self, _view, executor, HasWaterBreathing(_services.State.Self)))
        {
            case LifeSafetyAction.Surfaced:
                ReportSurfacing();
                return new NavigationTickOutcome(
                    PathExecutorState.InProgress, DeviationExceeded: false, NavigationPreemption.Surfaced);

            case LifeSafetyAction.Surfacing:
                ReportSurfacing();
                (MovementInput surfaceInput, float surfaceYaw, float surfacePitch) = SurfacingController.Next(_engine.State);
                _engine.SetRotation(surfaceYaw, surfacePitch);
                ApplyInput(surfaceInput);
                return new NavigationTickOutcome(
                    PathExecutorState.InProgress, DeviationExceeded: false, NavigationPreemption.Surfacing);

            case LifeSafetyAction.SurfaceBreathing:
                // The climb has arrived, so the job stops being a climb and becomes the one the driver already knows how to do: hold the cell the air is over and let the lung fill. This is BreathHoldInput, the same primitive a scheduled pause holds a bell with - steering to the cell centre, Jump in water, and never Sprint.
                //
                // The rotation is left where the climb put it, exactly as a scheduled pause leaves it: with no Sprint the body is not swimming, so PlayerPhysics takes the ordinary input vector, which reads the yaw and never the pitch.
                ReportSurfacing();
                ApplyInput(
                    BreathHoldInput(
                        _engine.State,
                        _supervisor.SurfacingAnchor ?? ArrivalCellCentre(_engine.State.Position)));
                return new NavigationTickOutcome(
                    PathExecutorState.InProgress, DeviationExceeded: false, NavigationPreemption.Surfacing);

            case LifeSafetyAction.Retreated:
                return new NavigationTickOutcome(
                    PathExecutorState.InProgress, DeviationExceeded: false, NavigationPreemption.Retreated);

            case LifeSafetyAction.Retreating:
                // The target is read back off the supervisor rather than recomputed, because the hold owns which waypoint is current and advanced it on this very tick. Null is the arrival, and the controller answers it by letting go so the body settles onto the cell it came back to - which is what the hold's settle window is there to watch.
                (MovementInput retreatInput, float retreatYaw, float retreatPitch) =
                    RetreatController.Next(_engine.State, _supervisor.RetreatTarget);
                _engine.SetRotation(retreatYaw, retreatPitch);
                ApplyInput(retreatInput);
                return new NavigationTickOutcome(
                    PathExecutorState.InProgress, DeviationExceeded: false, NavigationPreemption.Retreating);

            default:
                break;
        }

        // The reclose watch. A button-held iron door shuts itself again when the signal releases (button activation schedules its own release), and the two sides of BarrierCrossing.CommitOffset are different failures. Before the commit point the body is still outside the doorway, nothing is trapped, and letting the ordinary machinery notice the wall is the cheap answer. After it the body is past the point where stopping helps, so the crossing has to be abandoned NOW and replanned from where the body is - which is survivable precisely because the panel never overlaps a centred body.
        if (_reclose is { } watch && executor.CurrentIndex <= watch.SegmentIndex + 1)
        {
            Umpk.Game.Blocks.BlockState barrier = _view.GetBlock(watch.Witness);
            bool stillOpen = barrier.TryGetProperty("open", out string open) && open == "true";
            if (!stillOpen && executor.HasCommittedToCrossing)
            {
                _reclose = null;
                _logger.LogDebug(
                    "Crossing abandoned: {Witness} closed with the body already past the commit point.",
                    watch.Witness);
                return new NavigationTickOutcome(
                    PathExecutorState.Failed, DeviationExceeded: false, NavigationPreemption.None);
            }
        }
        else
            _reclose = null;

        PathExecutorTick tick = executor.Tick(_engine.State);
        if (tick.PendingBreathHold is { } breath)
            return TickBreathHold(executor, breath, tick.DeviationExceeded);

        if (_breathHold is { } finished)
        {
            // The executor released on the plan's OWN tick count rather than on anything the driver decided, so this is the last chance to report the hold. Without it a pause that ran exactly as planned would otherwise be the one case that logged nothing.
            _logger.LogDebug(
                "Breath hold at {Anchor} completed (the plan's own count): held {Ticks} ticks, air {From} -> {To}.",
                finished.Anchor, finished.Ticks, finished.EntryAir, _services.State.Self.AirSupply);
            _breathHold = null;
        }

        if (tick.PendingInteraction is { } pending)
        {
            // The executor is holding in front of a door. It pressed nothing and it did not advance, so the engine is NOT stepped: stepping it would apply an idle tick the executor did not ask
            // for and would move a body that is deliberately standing still inside a switch's reach.
            return new NavigationTickOutcome(
                PathExecutorState.InProgress, tick.DeviationExceeded, NavigationPreemption.Interaction, pending);
        }

        if (tick.State != PathExecutorState.InProgress)
        {
            // A path that ENDED hanging on a climbable has to keep holding on, or the idle tick lets go and the player slides back down the shaft it just climbed. See IdleInput.
            _holdClimbable = tick.State == PathExecutorState.Complete
                && _engine.State.OnClimbable
                && !_engine.State.OnGround;

            // Same shape, different medium: a path that ENDED standing in water has to keep holding station, or the current pushes the player off the block the caller was just told it reached.
            //
            // The anchor is the CENTRE of the cell the navigation completed in, and NOT the position it completed at. See StationHoldInput: the hold does not converge on its anchor, it parks the body a fixed 0.1932..0.2215 blocks downstream of it, because HoldStationToleranceSq is a radial deadband and the current carries the body to that circle's downstream edge. So the anchor has to sit far enough inside the goal cell to absorb that offset, and the arrival position does not: a SUBMERGED arrival completes on SegmentGeometry.IsCenterInsideTargetBlock
            // - the body is buoyant, OnGround is false, and GroundedSegmentController.ShouldComplete takes
            // its water arm - which guarantees only cellX + 0.0. Anchored at the arrival, every arrival under cellX + 0.1932 parks the body in the cell below the one the caller was told about.
            //
            // The cell centre is safe by construction as well as sufficient: the body already occupies that cell at the same Y, so its centre is passable, and the park lands at cellX + 0.5 - 0.2215..0.1932 = cellX + 0.278..0.307 for EVERY arrival rather than for a high one only.
            _holdAgainstCurrent = tick.State == PathExecutorState.Complete && _engine.State.InWater
                ? ArrivalCellCentre(_engine.State.Position)
                : null;
            return new NavigationTickOutcome(tick.State, tick.DeviationExceeded, NavigationPreemption.None);
        }

        TemplateOutput output = tick.Output;
        _engine.SetRotation(output.TargetYaw, output.TargetPitch);
        ApplyInput(output.Input);
        return new NavigationTickOutcome(PathExecutorState.InProgress, tick.DeviationExceeded, NavigationPreemption.None);
    }

    /// <summary>Re-seeds the engine position AND velocity after a server teleport. Called from the player-position apply path with self state already holding the absolute post-teleport position, rotation and delta movement. Creates the engine first when the world is available but no engine exists yet, so a teleport that lands before the first tick seeds the engine at the server's position rather than at the origin.</summary>
    /// <remarks><see cref="PlayerPhysics.Reset(Vec3d, float, float)"/> zeroes the velocity (it models a spawn), so the resolved delta movement is pushed back afterwards. Position correction preserves the server-reported velocity on every protocol.</remarks>
    public void ResyncPosition()
    {
        EnsureEngine();
        if (_engine is null)
            return;

        SelfState self = _services.State.Self;
        // A server teleport ends any hold: the player is no longer where it was holding station.
        _holdClimbable = false;
        _holdAgainstCurrent = null;
        _engine.Reset(self.Position, self.Yaw, self.Pitch);
        _engine.SetVelocity(self.Velocity);
        PushConditions();
    }

    /// <summary>Pushes the tracked self velocity into the engine and changes nothing else. Called from the <c>set_entity_motion</c> apply path once a server-applied velocity (knockback, explosion, piston, bounce) has landed in self state.</summary>
    /// <remarks>
    /// This is deliberately NOT <see cref="ResyncPosition"/>. That models a teleport and calls <see cref="PlayerPhysics.Reset(Vec3d, float, float)"/>, which clears fall distance, ground state, pose, the collision flags and the water/lava sensing. A server velocity packet changes only velocity, so a knockback must not reset the fall the player was already in.
    /// <para>The velocity does not need to be re-read here on the next tick: <see cref="PlayerPhysics.Step"/> consumes and updates the engine's own velocity field, and <see cref="SyncSelf"/> publishes the result back. Creating the engine first matters for a velocity that arrives before the first tick; <see cref="EnsureEngine"/> seeds it from the same self state.</para>
    /// </remarks>
    public void ApplyVelocity()
    {
        EnsureEngine();
        _engine?.SetVelocity(_services.State.Self.Velocity);
    }

    /// <summary>The engine's current snapshot, or null when no engine exists yet. Exposed because the engine holds state the client does not surface on <c>SelfState</c> (fall distance, collision flags, pose), and that is precisely the state a test needs to tell a velocity PUSH apart from an engine RESET.</summary>
    public PhysicsState? EngineState => _engine?.State;

    /// <summary>The conditions currently pushed into the engine, or null before it exists.</summary>
    public PhysicsConditions? EngineConditions => _engine?.Conditions;

    /// <summary>Advances the local body exactly once and publishes both sides of the input contract: the keys the controller held and the entity sprint state that vanilla's start/stop rules actually permit.</summary>
    private void ApplyInput(MovementInput requested)
    {
        if (_engine is null)
            return;

        SelfState self = _services.State.Self;
        requested = requested with
        {
            Sneak = requested.Sneak || self.Sneaking,
            Sprint = requested.Sprint || self.SprintRequested,
        };
        LastRequestedInput = requested;
        MovementInput applied = requested with { Sprint = ResolveActualSprint(requested, _engine.State) };
        LastAppliedInput = applied;
        StepResult result = _engine.Step(applied);
        StepSequence++;
        SyncSelf(result.State);
    }

    /// <summary>The supported subset of vanilla's entity sprint gate. Direction, hunger/flight and a blocking horizontal collision are all represented in tracked state. Item-use slowdown is not currently modeled by SelfState, so no synthetic answer is invented for it.</summary>
    private bool ResolveActualSprint(in MovementInput requested, in PhysicsState state)
    {
        SelfState self = _services.State.Self;
        return requested.Sprint
            && (requested.Forward || state.InWater)
            && (self.Food > 6 || self.MayFly)
            && !requested.Sneak
            && !state.HorizontalCollision;
    }

    /// <summary>The LIVE physics view of the installed world, or null before one exists. This is the view the life-safety supervisor is evaluated against - as opposed to a plan's frozen capture - so a test that drives the supervisor by hand has to read the same one the navigation tick does.</summary>
    public IPhysicsWorldView? WorldView => _view;

    private void SyncSelf(PhysicsState state)
    {
        SelfState self = _services.State.Self;
        self.Position = state.Position;
        self.Velocity = state.Velocity;
        self.Yaw = state.Yaw;
        self.Pitch = state.Pitch;
        self.OnGround = state.OnGround;
    }

    /// <summary>The second corner of the planning region: where the goal is trying to get to.</summary>
    /// <remarks>
    /// <para>Ask the goal first because it knows its destination whenever it was built from one. The fallback samples a sparse lattice and can miss an exact goal, which would otherwise collapse the captured region to a box around the start and leave distant terrain unread.</para>
    /// <para>The probe stays as the fallback for a goal with no concrete destination (a column goal, or an external implementation that takes the interface default).</para>
    /// </remarks>
    private static BlockPos ApproximateGoal(IGoal goal, BlockPos start)
    {
        if (goal.TryGetTargetHint(out BlockPos hint))
            return hint;

        // Probe outward for a block the goal accepts, to bound the region capture. Falls back to start.
        for (int r = 0; r <= 48; r += 4)
            for (int dx = -r; dx <= r; dx += Math.Max(1, r))
                for (int dz = -r; dz <= r; dz += Math.Max(1, r))
                    for (int dy = -r; dy <= r; dy += Math.Max(1, r))
                    {
                        var candidate = new BlockPos(start.X + dx, start.Y + dy, start.Z + dz);
                        if (goal.IsInGoal(candidate))
                            return candidate;

                    }

        return start;
    }

    /// <summary>The transient sprint modifier's id, which this read must exclude; see the method.</summary>
    private static readonly Identifier SprintingModifierId = Identifier.Minecraft("sprinting");

    /// <summary>The movement-speed attribute WITHOUT the sprint modifier, which is what <see cref="PhysicsConditions.BaseMovementSpeedAttribute"/> is defined to carry: the engine applies the sprint multiply itself, from the tick's input.</summary>
    /// <remarks>
    /// <para><c>movement_speed</c> is syncable, so after a client announces sprint the server can echo a resolved attribute containing <c>minecraft:sprinting</c>. Reading <c>Value</c> would multiply that modifier twice, producing 1.69x instead of 1.3x. <c>AttributeModifierIds</c> maps the legacy UUID to the identifier excluded here.</para>
    /// <para>The local player is not a member of <c>EntityStore</c>, so this reads <c>SelfState.Attributes</c>. Its quiet-session seed is the 0.1 value from <c>player-attribute defaults</c>; received Speed, armour, or soul-speed modifiers therefore affect the result while a session with no <c>movement_speed</c> frame retains the default.</para>
    /// </remarks>
    private float ReadMovementSpeed()
    {
        double v = _services.State.Self.Attributes.ValueExcluding(
            MovementSpeedAttributeId, SprintingModifierId, fallback: 0.1);
        return v > 0 ? (float)v : 0.1f;
    }

    /// <summary>The canonical id of the movement-speed attribute. Canonical because <c>SelfState.Attributes</c> is keyed that way: the registry names it <c>minecraft:generic.movement_speed</c> on 766/767 and <c>minecraft:movement_speed</c> from 768, and <c>Umpk.Game.Entities.AttributeIds</c> collapses the two at apply time.</summary>
    private static readonly Identifier MovementSpeedAttributeId = Identifier.Minecraft("movement_speed");

    /// <summary>The canonical id of the crouch-speed attribute (1.21+).</summary>
    private static readonly Identifier SneakingSpeedAttributeId = Identifier.Minecraft("sneaking_speed");

    /// <summary>The swift-sneak enchantment, read off the leggings on 1.19-1.20.6.</summary>
    private static readonly Identifier SwiftSneakId = Identifier.Minecraft("swift_sneak");

    /// <summary>The player-window (menu-space) slot index of the leggings/legs armor slot. Menu 5-8 is the armor block HEAD FIRST (<see cref="Umpk.Client.Internal.PlayerInventorySlotMap"/>'s layout doc), so it runs HEAD 5, CHEST 6, LEGS 7, FEET 8 while the INVENTORY space runs the other way round from 36. <c>SwiftSneakPushTests.LeggingsMenuSlot_IsSeven</c> pins it, the way <c>InventoryPipelineTests</c> pins boots = 8 - that boots pin exists because the naive index was wrong once already.</summary>
    private const int LeggingsMenuSlot = 7;

    /// <summary>The attribute registry the era gate below was last answered against.</summary>
    private Registry<Umpk.Game.Registries.AttributeDefinition>? _gatedAttributeRegistry;

    /// <summary>Canonical attribute ids this session's registry carries, cached per registry instance.</summary>
    private readonly HashSet<Identifier> _sessionAttributeIds = [];

    /// <summary>Whether this session's <c>minecraft:attribute</c> registry declares the given canonical attribute.</summary>
    /// <remarks>
    /// <para>This is the ERA GATE, and it is the dataset's answer rather than a protocol literal. Vanilla moved the crouch factor and the soul-speed bypass from client-side computation onto attributes at 1.21, and the dataset says exactly that: 766's registry has neither <c>sneaking_speed</c> nor <c>movement_efficiency</c>, 767's has both. So "does the session know this attribute" IS "is this the attribute era", with no <c>if (protocol &gt;= 767)</c> anywhere and no maintenance when a new protocol lands.</para>
    /// <para>The scan is over at most 40 entries and the answer is cached per registry INSTANCE, so it costs one pass per session (or per config-phase registry swap), not one per capture. It reads the registry rather than <c>SelfState.Attributes</c> deliberately: the map is empty when the session cannot observe attributes at all, and falling back to the enchantment arm on 1.21+ because entity tracking happens to be off would be a wrong answer rather than a conservative one.</para>
    /// </remarks>
    private bool SessionCarriesAttribute(Identifier canonical)
    {
        Registry<Umpk.Game.Registries.AttributeDefinition>? registry = _services.State.Registries?.Attributes;
        if (!ReferenceEquals(registry, _gatedAttributeRegistry))
        {
            _gatedAttributeRegistry = registry;
            _sessionAttributeIds.Clear();
            if (registry is not null)
                foreach (RegistryEntry<Umpk.Game.Registries.AttributeDefinition> entry in registry)
                    _sessionAttributeIds.Add(Umpk.Game.Entities.AttributeIds.Canonical(entry.Id));

        }

        return _sessionAttributeIds.Contains(canonical);
    }

    /// <summary>The factor a crouching tick scales the raw movement impulse by, in [0,1]: <see cref="PhysicsConditions.SneakingSpeedFactor"/>.</summary>
    /// <remarks>
    /// <para>Two arms, gated by <see cref="SessionCarriesAttribute"/> rather than by a protocol number.</para>
    /// <para><b>1.21+ (the attribute era).</b> The client reads the server-owned sneaking-speed attribute and does not consult the leggings directly. Computing the value here would be a divergence dressed up as an improvement. The <c>(float)</c> narrowing is reproduced at the read because it is load bearing: swift sneak's amount is a <c>float</c> <c>0.15F</c> widened for the attribute API, so three levels resolve in double to <c>0.7500000178813935</c>, not <c>0.75</c>.</para>
    /// <para><b>1.19-1.20.6 (the enchantment era).</b> Vanilla's client computes it itself: <c>clamp(0.3F + level * 0.15F, 0.0F, 1.0F)</c>. The clamp matters: a level-5 stack is constructible through NBT and must saturate at 1.0, which is also what the 1.21 attribute's own <c>[0,1]</c> range does for free.</para>
    /// <para>Below 1.19 no gate is needed at all. Those protocols' enchantment registries carry no <c>minecraft:swift_sneak</c>, so the lookup returns level 0 and the formula degenerates to the 0.3 vanilla has always used. Reading the level through <see cref="ItemStack.TryGetEnchantmentLevel"/> rather than the bare <c>Enchantments</c> property is what makes the legacy band work at all: every pre-766 item codec stuffs its NBT into <c>DataComponents.LegacyNbt</c> and never touches the component.</para>
    /// </remarks>
    private float ReadSneakingSpeed()
    {
        if (SessionCarriesAttribute(SneakingSpeedAttributeId))
            return (float)_services.State.Self.Attributes.Value(
                SneakingSpeedAttributeId, PhysicsConstants.DefaultSneakingSpeedFactor);

        InventoryState? inventory = _services.State.InventoryOrNull;
        if (inventory is null)
            return PhysicsConstants.DefaultSneakingSpeedFactor;

        ItemStack leggings = inventory.PlayerSlots[LeggingsMenuSlot];
        int level = leggings.TryGetEnchantmentLevel(
            SwiftSneakId,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeEra,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeSource,
            out int found)
            ? found
            : 0;

        return Math.Clamp(PhysicsConstants.DefaultSneakingSpeedFactor + (SwiftSneakBonusPerLevel * level), 0f, 1f);
    }

    /// <summary>Swift sneak's per-level contribution to the crouch factor, <c>0.15F</c>. Identical on both eras: The level is multiplied by it, and 1.21's data-driven definition feeds <c>per-level scaling</c> into <c>SNEAKING_SPEED</c> as <c>ADD_VALUE</c>.</summary>
    private const float SwiftSneakBonusPerLevel = 0.15f;

    /// <summary>The canonical id of the movement-efficiency attribute (1.21+).</summary>
    private static readonly Identifier MovementEfficiencyAttributeId = Identifier.Minecraft("movement_efficiency");

    /// <summary>The soul-speed enchantment, read off the boots on 1.16-1.20.6.</summary>
    private static readonly Identifier SoulSpeedId = Identifier.Minecraft("soul_speed");

    /// <summary>The movement-efficiency blend, in [0,1], which the engine lerps the block speed factor toward 1 with. Non-zero only on the ATTRIBUTE era; below it the bypass rides <see cref="ReadSoulSpeedLevel"/> instead and this stays 0.</summary>
    /// <remarks>
    /// <para>From 1.21 the bypass is an attribute the server resolves and syncs. Movement efficiency interpolates the block speed factor toward 1.0, and soul speed's data-driven definition adds <c>MOVEMENT_EFFICIENCY = 1.0</c> through a <c>LOCATION_CHANGED</c> effect while the wearer stands on a soul-speed block.</para>
    /// <para>The positional arm is deliberately not used on this era, even though it could avoid broadcast latency at a lane entrance. Protocol 1.21 reports the value through the synchronized attribute; predicting it would diverge from the server-driven one-or-two-tick boundary lag.</para>
    /// </remarks>
    private float ReadMovementEfficiency()
    {
        if (!SessionCarriesAttribute(MovementEfficiencyAttributeId))
            return 0f;

        return (float)_services.State.Self.Attributes.Value(MovementEfficiencyAttributeId, 0.0);
    }

    /// <summary>The soul-speed enchantment level on the boots, or 0. Non-zero only on the ENCHANTMENT era; from 1.21 the same fact arrives through <see cref="ReadMovementEfficiency"/> and this stays 0.</summary>
    /// <remarks>
    /// <para>On 1.16-1.20.6 the client resolves the bypass every tick from the boots and the block underfoot: a positive soul-speed level on a soul-speed block yields a speed factor of <c>1.0F</c>.</para>
    /// <para>A LEVEL is pushed rather than a resolved efficiency because the resolved answer depends on the block below the feet, which changes every tick, while the equipment does not. Pushing a per-tick value into a snapshot that is frozen until the next invalidation would either be stale at every lane entrance or force an invalidation every tick, and the second defeats the snapshot's whole purpose. The engine does the positional half; see <c>PlayerPhysics.ApplyBlockSpeedFactor</c>.</para>
    /// <para>This is the bypass only. The boost has been server-side on every protocol from 1.16, so it arrives as an <c>update_attributes</c> modifier on <c>movement_speed</c> and is already folded into <c>PhysicsConditions.BaseMovementSpeedAttribute</c>. Synthesising it here would apply it twice.</para>
    /// <para>So does the DURABILITY cost: boots take 4% damage per application, and the client observes it only as a <c>set_slot</c>, which the inventory already tracks - the level drops to zero by itself when the boots break and this returns 0 on the next capture. Modelling that RNG client-side would be a prediction no packet corrects, which is the discipline <c>SelfState.ActiveEffect</c>'s own remarks already forbid.</para>
    /// </remarks>
    private int ReadSoulSpeedLevel()
    {
        if (SessionCarriesAttribute(MovementEfficiencyAttributeId))
            return 0;

        InventoryState? inventory = _services.State.InventoryOrNull;
        if (inventory is null)
            return 0;

        ItemStack boots = inventory.PlayerSlots[BootsMenuSlot];
        return boots.TryGetEnchantmentLevel(
            SoulSpeedId,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeEra,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeSource,
            out int level)
            ? level
            : 0;
    }

    /// <summary>Whether the local player wears leather boots in the feet slot, which enables walking on powder snow.</summary>
    /// <remarks>
    /// <para>An ITEM IDENTITY, not a material class and not an armour slot in general. Netherite boots do nothing; leather boots in the hand do nothing; the other four leather wearables prevent FREEZING (<c>ItemTags.FREEZE_IMMUNE_WEARABLES</c>) but not sinking, which is a different tag and a different rule.</para>
    /// <para>No era gate: the method is byte-for-byte identical from 1.17 to 26.2, and on a pre-1.17 dataset there is no <c>minecraft:powder_snow</c> for the engine's shape rule to match, so this answering true costs nothing there. The reciprocal read for the PLANNER is <c>PathfinderCapabilities.PowderSnowWalkable</c>, off the same slot; the two are separate for the reason <see cref="ReadSoulSpeedLevel"/> is separate from the capture's own soul-speed level - the engine reads no game state of its own and the capture is frozen at plan time.</para>
    /// <para>Unknown reads as FALSE, which keeps the body on the conservative side: with no inventory tracking the engine gives no cube, the body sinks exactly as it does today, and the planner - which reads the same unknown the same way - never plans a route that needs one.</para>
    /// </remarks>
    private bool ReadPowderSnowWalkable()
    {
        InventoryState? inventory = _services.State.InventoryOrNull;
        return inventory is not null
            && inventory.PlayerSlots[BootsMenuSlot].Item.Id == LeatherBootsId;
    }

    /// <summary>The one item vanilla lets a player walk on powder snow in.</summary>
    private static readonly Identifier LeatherBootsId = Identifier.Minecraft("leather_boots");

    /// <summary>The attribute vanilla replaced the depth-strider enchantment with at 1.21, and the era gate for <see cref="ReadWaterMovementEfficiency"/>.</summary>
    /// <remarks>There is deliberately no protocol constant beside this. The era question is whether the session's registry carries the attribute, which <see cref="SessionCarriesAttribute"/> answers from the dataset. Protocol 766 carries a fixed <c>minecraft:enchantment</c> registry containing <c>depth_strider</c> and no water-movement-efficiency attribute, while protocol 767 and later carry the attribute (spelled <c>generic.water_movement_efficiency</c> until vanilla's 1.21.2 rename, which <c>AttributeIds.Canonical</c> absorbs) and no fixed enchantment registry. That is the same rule <see cref="ReadSneakingSpeed"/> follows, and it means a new protocol needs no maintenance here.</remarks>
    private static readonly Identifier WaterMovementEfficiencyAttributeId =
        Identifier.Minecraft("water_movement_efficiency");

    /// <summary>The player-window (menu-space) slot index of the boots/feet armor slot. See <see cref="Umpk.Client.Internal.PlayerInventorySlotMap"/>'s layout doc ("5-8 armor, head first") and <c>InventoryPipelineTests</c>, which pins this exact index ("FEET -&gt; menu 8, not menu 5").</summary>
    private const int BootsMenuSlot = 8;

    private static readonly Identifier DepthStriderId = Identifier.Minecraft("depth_strider");

    /// <summary>The water-movement-efficiency blend fraction, in [0,1], exactly as <see cref="PlayerPhysics"/>'s <c>TravelInWater</c> consumes it directly (not yet halved for off-ground; the engine halves it itself, per tick, since on-ground state changes every tick while equipment does not).</summary>
    /// <remarks>
    /// The method always returns the normalized fraction consumed by <see cref="PlayerPhysics"/>. Before the attribute era, it reads depth strider from the boots and divides the capped level by three. <see cref="ItemStack.TryGetEnchantmentLevel"/> handles legacy numeric ids, namespaced ids, and protocol 766's component holder ids. From protocol 767, the server supplies the normalized <c>minecraft:water_movement_efficiency</c> attribute directly.
    /// <para>The era table is:</para>
    /// <list type="table">
    /// <item><description><b>47-476</b> boots NBT, legacy numeric <c>ench</c> ids (depth strider = 8),
    /// divided by 3.</description></item>
    /// <item><description><b>477-765</b> boots NBT, namespaced string ids, divided by 3.</description></item>
    /// <item><description><b>766</b> component era with a static enchantment table, divided by 3.</description></item>
    /// <item><description><b>767+</b> <c>SelfState.Attributes</c>, used directly.</description></item>
    /// </list>
    /// <para><b>Fallback polarity.</b> A session that carries the attribute but never received a value answers the map's fallback <c>0.0</c>. This is conservative because it under-states the bot's speed, so a plan is over-priced rather than under-priced, and a bot that arrives early is safe where one that arrives late may have drowned. There is deliberately NO enchantment fallback on the attribute arm: <see cref="ReadSneakingSpeed"/> already rules that out in the same words: the server owns the number, and computing it here would be a divergence dressed up as an improvement. Reading the REGISTRY rather than the attribute map to decide the era is what keeps the two apart: an empty map means "not observed", and falling back to the boots on 1.21+ because entity tracking happens to be off would be a wrong answer rather than a careful one.</para>
    /// </remarks>
    private float ReadWaterMovementEfficiency()
    {
        if (SessionCarriesAttribute(WaterMovementEfficiencyAttributeId))
            return (float)_services.State.Self.Attributes.Value(WaterMovementEfficiencyAttributeId, 0.0);

        InventoryState? inventory = _services.State.InventoryOrNull;
        if (inventory is null)
            return 0f;

        ItemStack boots = inventory.PlayerSlots[BootsMenuSlot];
        return boots.TryGetEnchantmentLevel(
            DepthStriderId,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeEra,
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeSource,
            out int level)
            ? Math.Min(level, 3) / 3.0f
            : 0f;
    }

    /// <summary>The mob effects this holder derives something from. The enum value is the index into <see cref="ConditionEffectNames"/> and into the resolved <see cref="_effectIds"/>, so the two arrays below must stay in this order.</summary>
    private enum ConditionEffect
    {
        JumpBoost,
        Levitation,
        SlowFalling,
        DolphinsGrace,
        WaterBreathing,
        ConduitPower,
    }

    /// <summary>Parallel to <see cref="ConditionEffect"/>, indexed by it.</summary>
    private static readonly Identifier[] ConditionEffectNames =
    [
        Identifier.Minecraft("jump_boost"),
        Identifier.Minecraft("levitation"),
        Identifier.Minecraft("slow_falling"),
        Identifier.Minecraft("dolphins_grace"),
        Identifier.Minecraft("water_breathing"),
        Identifier.Minecraft("conduit_power"),
    ];

    /// <summary>The session's wire ids for <see cref="ConditionEffectNames"/>, resolved once; <see cref="EffectAbsent"/> where this protocol's registry does not carry the effect.</summary>
    private int[]? _effectIds;

    private const int EffectAbsent = -1;

    /// <summary>Whether the LOCAL player currently has the given effect, and at what amplifier.</summary>
    /// <remarks>
    /// <para>Reads <see cref="SelfState.ActiveEffects"/>, not the entity store. The server sends <c>update_mob_effect</c> for the player itself and <c>EntityApplier.ApplyEffectAsync</c>'s <c>isSelf</c> arm puts it there precisely because self is NOT a member of the shared <c>EntityStore</c>; a store lookup by the self entity id misses on every protocol.</para>
    /// <para>The wire id comes from the SESSION's own <c>minecraft:mob_effect</c> registry rather than from a literal, because numbering shifted from 1-based to 0-based at protocol 764 (1.20.2), moving every effect down one. <c>WireDecodedMobEffectTests</c> pins that boundary end to end. A one-based literal on a zero-based protocol names the NEXT effect: 8 is <c>minecraft:nausea</c>, 25 is <c>minecraft:luck</c>, 28 is <c>minecraft:conduit_power</c>, 30 is <c>minecraft:bad_omen</c>.</para>
    /// <para>Protocols 107-404 carry no <c>minecraft:mob_effect</c> table. Resolution returns <see cref="EffectAbsent"/> there and the condition flags remain false; tests pin that limitation.</para>
    /// </remarks>
    private bool TryEffect(ConditionEffect effect, out int amplifier)
    {
        amplifier = 0;
        SelfState self = _services.State.Self;
        if (self.ActiveEffects.Count == 0)
        {
            // The steady state: nothing to match, so nothing to resolve.
            return false;
        }

        int[]? ids = _effectIds;
        if (ids is null)
        {
            RegistryAccess? registries = _services.State.Registries;
            if (registries is null)
            {
                // The registries arrive in the configuration phase; caching an empty answer from before that would make every one of these reads permanently blind for the rest of the session.
                return false;
            }

            ids = _effectIds = new int[ConditionEffectNames.Length];
            for (int i = 0; i < ConditionEffectNames.Length; i++)
                ids[i] = registries.MobEffects.TryGetNetworkId(ConditionEffectNames[i], out int networkId)
                    ? networkId
                    : EffectAbsent;

        }

        int id = ids[(int)effect];
        if (id == EffectAbsent)
            return false;

        if (self.ActiveEffects.TryGetValue(id, out State.ActiveEffect? active))
        {
            amplifier = active.Amplifier;
            return true;
        }

        return false;
    }
}

/// <summary>An on-loop snapshot of everything the off-loop A* search needs: the captured planning region, the start position, the goal, and the physics conditions at capture time. Immutable, so the search runs safely off the session loop.</summary>
/// <param name="Planning">The captured planning region.</param>
/// <param name="Start">The block the player is standing in.</param>
/// <param name="Goal">The goal predicate the search is solving.</param>
/// <param name="Conditions">The engine's physics conditions at capture time.</param>
/// <param name="AirSupply">The breath the player holds at capture time, in ticks. Captured HERE, on the loop, with everything else the off-loop search needs: the breath validator judges the route against the lung the player actually has, and reading <c>Self.AirSupply</c> from the search's thread would be reading live session state off the loop.</param>
/// <param name="Capabilities">The player's effects and carried items at capture time. Same staleness contract as <paramref name="Conditions"/> and for the same reason as <paramref name="AirSupply"/>: it is the instant the plan was captured, and a replan is what refreshes it. An effect that expires or an item that is consumed mid-route is invisible to the running executor.</param>
internal readonly record struct PlanCapture(
    PlanningWorldView Planning,
    BlockPos Start,
    IGoal Goal,
    PhysicsConditions Conditions,
    int AirSupply,
    Umpk.Pathfinding.Core.PathfinderCapabilities Capabilities);
