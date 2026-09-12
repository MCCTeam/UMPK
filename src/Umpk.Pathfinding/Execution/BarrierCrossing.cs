using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Pathfinding.Moves;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>The doorway a segment passes through, and which side of it the panel stands on.</summary>
/// <remarks>
/// <para>An open door does not empty its cell; it rotates the 3/16 panel onto one face. A 0.6-wide body centred in the cell therefore has <see cref="PanelSideClearance"/> = <b>0.0125</b> blocks of room on the panel side and 0.2 on the far side, and that asymmetry is the whole content of this type: the executor must not aim at the cell centre, it must aim off the panel.</para>
/// <para>In a walled-lane measurement, a body passes the doorway from a lateral offset of 0.5 through 0.7 and stops at 0.45 and 0.75. <see cref="FreeSideBias"/> puts the aim in the middle of that band. two degrees of error TOWARD the panel already stops the body, while the same error away from it does not, so there is no symmetric tolerance to lean on and the bias has to be a real offset rather than a tie-break.</para>
/// <para>An open fence gate is not a crossing. Its collision shape is empty when open, so its cell is as empty as air and there is nothing to scrape.</para>
/// </remarks>
/// <param name="PanelX">The X component of the unit direction from the cell centre TOWARD the panel.</param>
/// <param name="PanelZ">The Z component of that direction.</param>
public readonly record struct BarrierCrossing(int PanelX, int PanelZ)
{
    /// <summary>The clearance a centred 0.6-wide body has on the panel side of an open door, in blocks: <c>0.5 - 0.3 - 0.1875</c>.</summary>
    public const double PanelSideClearance = 0.0125;

    /// <summary>How far off the cell centre, toward the FREE side, the executor aims while crossing. The measured pass band is a lateral offset of 0.5 to 0.7, so 0.1 is its midpoint and leaves the same 0.1 of slack on either side of the aim.</summary>
    public const double FreeSideBias = 0.1;

    /// <summary>How far past the doorway's entry face, along the direction of travel, a body's CENTRE has to be before the crossing is committed: <c>0.4875</c>.</summary>
    /// <remarks>
    /// <para>It is <see cref="MoveHelper.PlayerHalfWidth"/> 0.3 plus the panel's own 0.1875 depth, and what it marks is the point past which the closed panel no longer overlaps the body: a body whose centre is at <c>c</c> has its trailing face at <c>c - 0.3</c>, the panel occupies <c>[0, 0.1875]</c> of the cell along the crossing axis, and the two stop touching at exactly <c>c = 0.4875</c>.</para>
    /// <para><b>What the executor does with it.</b> An iron door held open by a button closes again when the button releases, and the two sides of this point are different failures. BEFORE it, a reclose is benign: the body is still outside the doorway, nothing is trapped, and the crossing can simply be abandoned and replanned. AFTER it, the body is committed - it is past the point where stopping helps - and a door observed closing is a verify-fail that has to replan from where the body now is. The arithmetic above is what makes the second case survivable rather than fatal: the panel never overlaps a centred body, so a replan from inside the doorway is a replan and not a rescue.</para>
    /// </remarks>
    public const double CommitOffset = 0.4875;

    /// <summary>How far ahead along the segment heading the executor's lane hold aims, in blocks. A pure-pursuit target rather than the segment's end point, so it travels with the body and cannot invert the moment the body crosses the end plane; the distance is what sets how hard the lane is held, since the yaw error it asks for is <c>atan(FreeSideBias / this)</c>.</summary>
    public const double LaneLookaheadBlocks = 0.5;

    /// <summary>The X component of the unit direction from the cell centre toward the FREE side.</summary>
    public int FreeX => -PanelX;

    /// <summary>The Z component of the unit direction from the cell centre toward the FREE side.</summary>
    public int FreeZ => -PanelZ;

    /// <summary>The crossing for a body cell, when either it or the head cell above it holds an open door or trapdoor panel.</summary>
    /// <remarks>
    /// The head cell counts because an open trapdoor overhead is the same vertical panel at head height, and a body that clips it with its shoulders is stopped exactly as one that clips a door with its hip.
    /// <para>The side is read off the SHAPE rather than off <c>facing</c> and <c>hinge</c>, because the shape is what the body collides with and it is one rule for doors, trapdoors, every facing and both hinges. A panel that hugs no single face is not recognised, and the caller gets no crossing rather than a guessed side.</para>
    /// </remarks>
    /// <param name="world">The world the plan was built against.</param>
    /// <param name="x">The body cell's X.</param>
    /// <param name="y">The body cell's Y (the feet cell).</param>
    /// <param name="z">The body cell's Z.</param>
    /// <param name="crossing">The resolved crossing.</param>
    /// <returns>True when the column holds a panel whose side could be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static bool TryResolve(IPhysicsWorldView world, int x, int y, int z, out BarrierCrossing crossing)
    {
        ArgumentNullException.ThrowIfNull(world);
        return TryResolveCell(world, x, y, z, out crossing) || TryResolveCell(world, x, y + 1, z, out crossing);
    }

    /// <summary>Where a CLOSED trapdoor's panel will stand once it is opened, worked out from <c>facing</c> before anything has been opened.</summary>
    /// <remarks>
    /// <para><b>Why a prediction is needed at all.</b> A crossing read off the shape (<see cref="TryResolve"/>) can only see a panel that is already there, and for a horizontal doorway that is enough: the walk into the doorway has lateral authority and the executor can be handed the real side the moment the door opens. A FALL cannot. <c>FallTemplate</c> presses <c>MovementInput.None</c> on every tick and has no steering at all, so a body that drops through a trapdoor it has just opened arrives wherever it was standing when the lid went - which means the 0.8125-wide band has to be satisfied BEFORE the step-off, which means before the interaction, which means from the closed state. Course rows G5a and L5 are that shape.</para>
    /// <para><b>The rule, and why only a trapdoor gets one.</b> The open shape keys directly on <c>FACING</c>, and the box is the wall opposite it: <c>facing=east</c> puts the panel on the cell's west face. A door's open side additionally depends on <c>HINGE</c>, and a fence gate has no panel at all when open, so neither is predicted here. For a door, the executor is given the observed side after opening, which is what its walk can still act on.</para>
    /// </remarks>
    /// <param name="world">The frozen world.</param>
    /// <param name="at">The trapdoor's cell.</param>
    /// <param name="crossing">The panel side the cell will hold once opened.</param>
    /// <returns>True for a closed trapdoor whose <c>facing</c> could be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static bool TryPredictAfterOpening(IPhysicsWorldView world, BlockPos at, out BarrierCrossing crossing)
    {
        ArgumentNullException.ThrowIfNull(world);
        crossing = default;

        BlockState state = world.GetBlock(at);
        if (state.IsDefault
            || !state.Block.Id.Path.EndsWith(TrapdoorSuffix, StringComparison.Ordinal)
            || !state.TryGetProperty(OpenProperty, out string open)
            || open == TrueValue
            || !state.TryGetProperty(FacingProperty, out string facing))
            return false;

        (int panelX, int panelZ) = facing switch
        {
            "north" => (0, 1),
            "south" => (0, -1),
            "west" => (1, 0),
            "east" => (-1, 0),
            _ => (0, 0),
        };

        if (panelX == 0 && panelZ == 0)
            return false;

        crossing = new BarrierCrossing(panelX, panelZ);
        return true;
    }

    private const string TrapdoorSuffix = "_trapdoor";

    private const string OpenProperty = "open";

    private const string FacingProperty = "facing";

    private const string TrueValue = "true";

    private static bool TryResolveCell(IPhysicsWorldView world, int x, int y, int z, out BarrierCrossing crossing)
    {
        if (MoveHelper.TryGetPanelSide(world, x, y, z, out int panelX, out int panelZ))
        {
            crossing = new BarrierCrossing(panelX, panelZ);
            return true;
        }

        crossing = default;
        return false;
    }
}
