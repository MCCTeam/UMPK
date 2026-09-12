using Umpk.Client.Navigation;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The yaw inversion behind the water station hold. This is the one part of it that can be silently backwards: a wrong sign presses the bot downstream instead of upstream and still looks like "it tried".</summary>
/// <remarks>Minecraft yaw: 0 faces +Z, 90 faces -X, 180 faces -Z, 270 faces +X. Forward is +zza, Left is +xxa (<c>MovementInput.GetMoveVector</c>), and <c>PlayerPhysics.GetInputVector</c> rotates those by the yaw. The expectations below are written out per compass direction rather than derived from that formula, so a change to either one has to be looked at.</remarks>
public sealed class StationHoldInputTests
{
    // Facing +Z is facing SOUTH, and from there east (+X) is on the player's LEFT, not its right. That is the whole reason this helper is tested: the intuitive reading is backwards.
    [Theory]
    [InlineData(0f, 0.0, 1.0, true, false, false, false)]    // want +Z -> Forward
    [InlineData(0f, 0.0, -1.0, false, true, false, false)]   // want -Z -> Back
    [InlineData(0f, 1.0, 0.0, false, false, true, false)]    // want +X (east) -> Left
    [InlineData(0f, -1.0, 0.0, false, false, false, true)]   // want -X (west) -> Right
    // Facing +X (yaw 270) is facing EAST; from there north (-Z) is on the player's left.
    [InlineData(270f, 1.0, 0.0, true, false, false, false)]  // want +X -> Forward
    [InlineData(270f, -1.0, 0.0, false, true, false, false)] // want -X -> Back
    [InlineData(270f, 0.0, -1.0, false, false, true, false)] // want -Z (north) -> Left
    [InlineData(270f, 0.0, 1.0, false, false, false, true)]  // want +Z (south) -> Right
    // Facing -Z (yaw 180): everything mirrors.
    [InlineData(180f, 0.0, -1.0, true, false, false, false)] // want -Z -> Forward
    [InlineData(180f, 0.0, 1.0, false, true, false, false)]  // want +Z -> Back
    public void InputToward_PressesTheAxisThatMovesTowardTheDelta(
        float yaw, double dx, double dz, bool forward, bool back, bool left, bool right)
    {
        MovementInput input = PhysicsEngineHolder.InputToward(dx, dz, yaw);

        Assert.Equal(forward, input.Forward);
        Assert.Equal(back, input.Back);
        Assert.Equal(left, input.Left);
        Assert.Equal(right, input.Right);
    }

    /// <summary>The upstream case that started this: the bot arrived at x=1011 in a current flowing toward -X and was facing +X. Holding station means pressing Forward, into the flow, not drifting with it.</summary>
    [Fact]
    public void InputToward_UpstreamAnchor_PressesIntoTheFlow()
    {
        // Pushed downstream to x=1010.6, anchor at x=1011.1: the correction is +X, and facing +X that is Forward.
        MovementInput input = PhysicsEngineHolder.InputToward(dx: 0.5, dz: 0.0, yaw: 270f);

        Assert.True(input.Forward);
        Assert.False(input.Back);
    }

    /// <summary>A delta inside the per-axis deadband presses nothing, so the hold cannot oscillate.</summary>
    [Fact]
    public void InputToward_TinyDelta_PressesNothing()
    {
        MovementInput input = PhysicsEngineHolder.InputToward(dx: 0.01, dz: 0.01, yaw: 0f);

        Assert.False(input.Forward);
        Assert.False(input.Back);
        Assert.False(input.Left);
        Assert.False(input.Right);
    }
}
