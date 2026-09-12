using Umpk.Pathfinding.Core;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>The shared, immutable context an <see cref="PathExecutor"/> hands to every action template: the frozen planning world view, the version physics profile, and the physics conditions to forward simulate under. Templates use it to query terrain and to run <see cref="PhysicsSimulator"/> lookaheads; they never touch a live engine. Session-free and reusable across segments.</summary>
public sealed class PathExecutionContext
{
    /// <summary>The frozen terrain the executor plans and simulates over.</summary>
    public IPhysicsWorldView World { get; }

    /// <summary>The version physics profile.</summary>
    public PhysicsProfile Profile { get; }

    /// <summary>The physics conditions used for forward-simulation lookahead.</summary>
    public PhysicsConditions Conditions { get; }

    /// <summary>Whether the executor may emit <see cref="MovementInput.Sprint"/>. Mirrors <c>PathfinderOptions.AllowSprint</c>, which the caller passed to the search that produced these segments.</summary>
    /// <remarks>The flag has to reach execution because the planner it came from already priced the path as if it were honoured: <c>CalculationContext.CanSprint</c> charges <c>ActionCosts.WalkOneBlock</c> instead of <c>SprintOneBlock</c> and refuses whole move families outright (<c>MoveSprintDescend</c>, <c>JumpExpander</c>'s jump family, <c>JumpFeasibility</c>'s sprint-jump arms). Executing that plan at sprint speed makes the run 30% faster than its own plan, and the braking lookahead picks brake points for a bot that does not exist.</remarks>
    public bool AllowSprint { get; }

    /// <summary>How long each segment is allowed to take and how slowly its executor may move before it counts as stuck, derived from the plan's own charge and the medium rather than from a literal inside a template.</summary>
    public SegmentBudgetPolicy Budget { get; }

    /// <summary>The player's capabilities at capture time (effects, carried items). Frozen exactly like <see cref="Conditions"/> and for the same reason: this is the instant the plan was captured, not a live read, so an effect that expires mid-route is invisible until a replan. <see cref="PathfinderCapabilities.None"/> when nobody captured any.</summary>
    public PathfinderCapabilities Capabilities { get; }

    internal TransitionBrakingPlanner Braking { get; }

    /// <summary>Whether this route was planned with the search's breath dimension suspended, because the player held water breathing (or conduit power) that covered it.</summary>
    /// <remarks>
    /// <para>It rides on the PLAN, not on the holder that ran it, and that is deliberate. The life-safety supervisor is per-holder and outlives any one navigation, so a flag set on it has to be re-armed at the start of every navigation AND after every replan or a suspension leaks into a route that never earned one. Carried here it cannot leak: a replan produces a new context, and a second navigation produces a new context, so the flag is re-derived for free exactly as <see cref="AllowSprint"/> is.</para>
    /// <para><b>It is half of the answer, never the whole of it.</b> This is a statement about the CAPTURE - the potion the player held when the route was priced - and a potion can be revoked mid-route. The supervisor suspends its breath arm only while this is true AND the effect is still live, so the arm comes back on the tick <c>remove_mob_effect</c> lands rather than on the tick a replan gets round to noticing.</para>
    /// </remarks>
    public bool BreathSuspended { get; }

    /// <summary>The status effects this route LEANS ON: the ones whose disappearance invalidates the plan rather than merely changing what the next one would look like. Empty for every route that would be identical without them.</summary>
    public IReadOnlyList<Identifier> DependsOnEffects { get; }

    /// <summary>Whether this route STANDS on powder snow somewhere, and therefore leans on the leather boots the capture found. False for every route that would be identical without them.</summary>
    /// <remarks>
    /// <para>The same scoping <see cref="DependsOnEffects"/> has, and for the same reason: a plan that never touches powder snow must not spend a replan when the player takes their boots off in a lava field. It is resolved once, when the executor is built, because the alternative is rescanning the segment list every tick.</para>
    /// <para><b>What it is for.</b> <see cref="Capabilities"/> is frozen at capture and <see cref="Conditions"/> is pushed on change, so boots removed mid-route stop the ENGINE giving the cube while this plan still believes the lane is floor - a body dropping into a freezing pit on a route that no longer exists. The holder watches this flag beside the effect arm and raises the same <c>CapabilityLost</c> pre-emption, and the replan that follows is taken with the capture the player actually has.</para>
    /// </remarks>
    public bool DependsOnPowderSnowBoots { get; }

    /// <summary>Creates an execution context.</summary>
    /// <param name="world">The frozen terrain the executor plans and simulates over.</param>
    /// <param name="profile">The version physics profile.</param>
    /// <param name="conditions">The physics conditions to forward-simulate under.</param>
    /// <param name="allowSprint">Whether the executor may emit <see cref="MovementInput.Sprint"/>.</param>
    /// <param name="capabilities">The player's capabilities at capture time, or null for <see cref="PathfinderCapabilities.None"/>.</param>
    /// <param name="breathSuspended">Whether the plan was made with the breath dimension suspended; see <see cref="BreathSuspended"/>.</param>
    /// <param name="dependsOnEffects">The effects the route leans on, or null for none; see <see cref="DependsOnEffects"/>.</param>
    /// <param name="dependsOnPowderSnowBoots">Whether the route stands on powder snow; see <see cref="DependsOnPowderSnowBoots"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public PathExecutionContext(
        IPhysicsWorldView world,
        PhysicsProfile profile,
        PhysicsConditions conditions,
        bool allowSprint = true,
        PathfinderCapabilities? capabilities = null,
        bool breathSuspended = false,
        IReadOnlyList<Identifier>? dependsOnEffects = null,
        bool dependsOnPowderSnowBoots = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        World = world;
        Profile = profile;
        Conditions = conditions;
        AllowSprint = allowSprint;
        Capabilities = capabilities ?? PathfinderCapabilities.None;
        BreathSuspended = breathSuspended;
        DependsOnEffects = dependsOnEffects ?? [];
        DependsOnPowderSnowBoots = dependsOnPowderSnowBoots;
        Budget = new SegmentBudgetPolicy(world, profile, allowSprint);
        Braking = new TransitionBrakingPlanner(world, profile, conditions, allowSprint);
    }

    /// <summary>Creates an execution context with survival-default conditions.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public PathExecutionContext(IPhysicsWorldView world, PhysicsProfile profile)
        : this(world, profile, PhysicsConditions.Default)
    {
    }
}
