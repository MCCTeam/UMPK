using Umpk.Geometry;

namespace Umpk.Pathfinding.Execution;

/// <summary>What kind of interaction a segment needs before its body may cross.</summary>
public enum InteractionKind
{
    /// <summary>No interaction. The default of a segment nobody attached one to.</summary>
    None = 0,

    /// <summary>A bare-handed <c>use_item_on</c> against the barrier itself: every wooden and copper door and trapdoor, and every fence gate that can be opened by hand.</summary>
    OpenByHand = 1,

    /// <summary>A bare-handed <c>use_item_on</c> against a BUTTON or LEVER that powers the barrier, because the barrier itself refuses a hand: <c>iron_door</c> and <c>iron_trapdoor</c>. The witness is then the door rather than the switch, and a button carries a window the crossing has to fit. See <see cref="Umpk.Pathfinding.Moves.DoorActivation"/>.</summary>
    PressActivator = 2,

    /// <summary>The same bare-handed <c>use_item_on</c> as <see cref="OpenByHand"/>, sent the other way: the barrier is OPEN and the plan needs it SHUT.</summary>
    /// <remarks>
    /// <para>The toggle is symmetric, so this is the same packet against the same block. What differs is verification: the witness's <c>open</c> has to come back <c>false</c>, which is why <see cref="InteractionRequirement.ExpectedOpen"/> exists rather than the driver hard-coding <c>true</c>.</para>
    /// <para>Only one geometry asks for it, and it is the only one where closing HELPS: an open trapdoor with <c>half=bottom</c>. Open, its shape is the vertical 3/16 panel keyed on <c>facing</c>; closed, <c>half=bottom</c> selects <c>[[0,0,0,1,0.1875,1]]</c> (ref 53), a floor slab a body steps onto and walks over. A DOOR's closed shape is the panel across the crossing axis and a <c>half=top</c> trapdoor's is a plate at the cell's ceiling, so closing either of those replaces one obstacle with another. See <c>MoveHelper.ClosesIntoAFloor</c>.</para>
    /// </remarks>
    CloseByHand = 3,

    /// <summary>No packet at all. The barrier is powered by a PRESSURE PLATE and the body is standing on it, so the "interaction" is a wait for the door the plate drives to be observed open.</summary>
    /// <remarks>
    /// <para>A pressure plate does not consume a <c>use_item_on</c> action, so the held item receives it. Against a plate that means trying to place whatever the bot happens to be carrying. The bot's hands are usually empty and vanilla additionally refuses a placement that intersects the body, so the damage is bounded - but "bounded" is not a reason to send a packet that means something else.</para>
    /// <para>There is a second, sharper consequence. <c>InteractionActions.UseBlockVerifiedAsync</c> refuses outright while the body is sneaking with a held item. A plate routed down the press path would therefore report a refusal and burn an interaction budget on a door that was already open under the bot's feet.</para>
    /// </remarks>
    StandOnPlate = 4,
}

/// <summary>What a segment has to make happen in the world before the body may cross it, and from where.</summary>
/// <remarks>
/// <para>The executor cannot send packets - <c>Umpk.Pathfinding</c> does not reference <c>Umpk.Client</c>, and the reason is the same one <c>LifeSafetySupervisor</c>'s class comment gives at length - so this is a REQUEST the executor surfaces and a driver above it satisfies. It carries everything that request needs and nothing that could go stale: block positions, not states.</para>
/// <para><see cref="From"/> is the cell the body is standing in when it acts, which is the segment's own start cell. It is here rather than being re-derived because the driver has to check reach from somewhere, and the only cell the plan can promise the body will be in is the one the plan put it in.</para>
/// </remarks>
/// <param name="Target">The block to send the <c>use_item_on</c> against.</param>
/// <param name="Kind">What kind of interaction it is.</param>
/// <param name="From">The cell the body stands in while it acts: the segment's start cell.</param>
/// <param name="Witness">The block whose <c>open</c> property is read back to decide whether the interaction worked. Equal to <see cref="Target"/> for a hand-opened barrier; for an activator it is the DOOR rather than the switch, because the door updates <c>POWERED</c> and <c>OPEN</c> together from the neighbour signal and is therefore the only authoritative witness that the press did anything.</param>
/// <param name="WindowTicks">How many ticks the interaction's effect lasts before it reverses itself, or <c>0</c> for an effect that does not reverse. A hand-opened barrier is always <c>0</c> because it does not close on a timer.</param>
public readonly record struct InteractionRequirement(
    BlockPos Target,
    InteractionKind Kind,
    BlockPos From,
    BlockPos Witness,
    int WindowTicks = 0)
{
    /// <summary>What the witness's <c>open</c> property has to read before the interaction counts as done.</summary>
    /// <remarks><c>true</c> for every kind that opens a barrier and <c>false</c> for <see cref="InteractionKind.CloseByHand"/>. It is a property of the REQUIREMENT rather than a constant in the driver because the toggle is one packet in both directions and only the expected answer distinguishes them; a driver that verified <c>open == true</c> after a close would read every successful close as a refusal and burn the whole interaction budget on a barrier that had already done what was asked.</remarks>
    public bool ExpectedOpen => Kind != InteractionKind.CloseByHand;

    /// <summary>Whether satisfying this requirement means sending a <c>use_item_on</c> at all.</summary>
    /// <remarks>False for exactly one kind, <see cref="InteractionKind.StandOnPlate"/>, whose remarks give the reason. It is a property of the REQUIREMENT rather than an <c>if</c> in the driver for the same reason <see cref="ExpectedOpen"/> is: the driver would otherwise carry a second copy of the distinction, and the two copies would eventually disagree.</remarks>
    public bool SendsAUse => Kind != InteractionKind.StandOnPlate;

    /// <summary>A hand-opened barrier: the target is its own witness and there is no window.</summary>
    /// <param name="target">The barrier to open.</param>
    /// <param name="from">The cell the body stands in while it opens it.</param>
    public static InteractionRequirement OpenByHandAt(BlockPos target, BlockPos from)
        => new(target, InteractionKind.OpenByHand, from, target);

    /// <summary>A hand-closed barrier: the same target, the same witness, and no timed re-open window.</summary>
    /// <param name="target">The barrier to close.</param>
    /// <param name="from">The cell the body stands in while it closes it.</param>
    public static InteractionRequirement CloseByHandAt(BlockPos target, BlockPos from)
        => new(target, InteractionKind.CloseByHand, from, target);

    /// <summary>A switch that powers a barrier: the switch is used, the barrier is read, and the window - zero for a lever - is how long the barrier stays open afterwards.</summary>
    /// <param name="activator">The button or lever to use.</param>
    /// <param name="barrier">The door or trapdoor whose <c>open</c> is the answer.</param>
    /// <param name="from">The cell the body stands in while it presses.</param>
    /// <param name="windowTicks">The button's press duration, or 0 for a lever.</param>
    public static InteractionRequirement PressAt(
        BlockPos activator, BlockPos barrier, BlockPos from, int windowTicks)
        => new(activator, InteractionKind.PressActivator, from, barrier, windowTicks);

    /// <summary>A pressure plate that powers a barrier: nothing is sent, the barrier is read, and the window is how long the plate holds its signal after the body steps off it.</summary>
    /// <remarks><see cref="Target"/> and <see cref="From"/> are both the plate, and that is not redundancy: the target names the thing that does the work, which for every other kind is a different block from the one the body stands in, and here happens to be the same one. Keeping the field means a log line or a future driver reads identically across all four kinds.</remarks>
    /// <param name="plate">The plate the body stands on, which is also the cell it stands in.</param>
    /// <param name="barrier">The door whose <c>open</c> is the answer.</param>
    /// <param name="windowTicks">The plate's press duration: 20, or 10 for a weighted plate.</param>
    public static InteractionRequirement StandOnAt(BlockPos plate, BlockPos barrier, int windowTicks)
        => new(plate, InteractionKind.StandOnPlate, plate, barrier, windowTicks);
}
