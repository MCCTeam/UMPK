using Umpk.Client.Bedrock.Movement.Simulation;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Movement.Simulation;

public sealed class BedrockOneTickStepperTests
{
    // Every case below exercises synthetic scaffold policy with explicit test-only numbers, not measured Bedrock 1.26.45 behavior.
    private static readonly BedrockMovementProfile s_testProfile = new(
        walkSpeedBlocksPerSecond: 4.0,
        gravityBlocksPerSecondSquared: 20.0,
        terminalVelocityBlocksPerSecond: 40.0,
        jumpImpulseBlocksPerSecond: 7.0,
        playerWidthBlocks: 0.6,
        playerHeightBlocks: 1.8);

    private static BedrockCollisionSnapshot GroundFloor(long version = 1)
    {
        var builder = new BedrockCollisionSnapshot.Builder();
        builder.FillBox(-4, 0, -4, 4, 0, 4, BedrockCollisionCell.KnownSolid);
        builder.FillBox(-4, 1, -4, 4, 8, 4, BedrockCollisionCell.KnownAir);
        return builder.Build(version);
    }

    [Fact]
    public void StandingOnKnownGroundKeepsPositionAndReportsApplied()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        var (next, applied, collision) = BedrockOneTickStepper.Step(state, BedrockMovementInput.Idle(), new BedrockMovementTick(1, 0.05), BedrockMovementProfile.Default, snapshot);
        Assert.Equal(BedrockInputDisposition.Applied, applied.Disposition);
        Assert.Equal(BedrockCollisionOutcome.KnownFree, collision.Outcome);
        Assert.Equal(state.PositionX, next.PositionX);
        Assert.Equal(state.PositionY, next.PositionY);
        Assert.Equal(state.PositionZ, next.PositionZ);
        Assert.True(next.OnGround);
        Assert.Equal(1UL, next.TickIndex);
    }

    [Fact]
    public void StandingOverUnknownTerrainRefusesWithoutMoving()
    {
        var snapshot = new BedrockCollisionSnapshot.Builder().Set(0, 1, 0, BedrockCollisionCell.KnownAir).Build(1);
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        var (next, applied, collision) = BedrockOneTickStepper.Step(state, BedrockMovementInput.Idle(), new BedrockMovementTick(1, 0.05), BedrockMovementProfile.Default, snapshot);
        Assert.Equal(BedrockInputDisposition.RefusedUnknownTerrain, applied.Disposition);
        Assert.Equal(BedrockCollisionOutcome.RefusedUnknown, collision.Outcome);
        Assert.True(collision.UnknownCells > 0);
        Assert.Equal(state, next);
    }

    [Fact]
    public void HorizontalWishMovesByWalkSpeedAndEchoesAppliedInput()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 10);
        var input = new BedrockMovementInput(1.0, 0.0, 0f, 0f, false, false, false);
        var (next, applied, collision) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(11, 0.05), s_testProfile, snapshot);
        Assert.Equal(BedrockInputDisposition.Applied, applied.Disposition);
        Assert.Equal(1.0, applied.AppliedWishX);
        Assert.Equal(0.0, applied.AppliedWishZ);
        Assert.False(applied.AppliedJump);
        Assert.Equal(BedrockCollisionOutcome.KnownFree, collision.Outcome);
        Assert.Equal(BedrockBlockedAxes.None, collision.BlockedAxes);
        Assert.Equal(0.5 + 4.0 * 0.05, next.PositionX, 9);
        Assert.Equal(1.0, next.PositionY, 9);
    }

    [Fact]
    public void HorizontalWishIntoKnownWallCollidesAndClamps()
    {
        var builder = new BedrockCollisionSnapshot.Builder();
        builder.FillBox(-4, 0, -4, 4, 0, 4, BedrockCollisionCell.KnownSolid);
        builder.FillBox(-4, 1, -4, 4, 3, 4, BedrockCollisionCell.KnownAir);
        builder.Set(1, 1, 0, BedrockCollisionCell.KnownSolid);
        builder.Set(1, 2, 0, BedrockCollisionCell.KnownSolid);
        var snapshot = builder.Build(1);
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        var input = new BedrockMovementInput(1.0, 0.0, 0f, 0f, false, false, false);
        var (next, applied, collision) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.2), s_testProfile, snapshot);
        Assert.Equal(BedrockInputDisposition.Applied, applied.Disposition);
        Assert.Equal(BedrockCollisionOutcome.CollidedSolid, collision.Outcome);
        Assert.True((collision.BlockedAxes & BedrockBlockedAxes.X) != 0);
        Assert.Equal(1 - 0.3 - 0.0001, next.PositionX, 9);
        Assert.Equal(0.0, next.VelocityX);
    }

    [Fact]
    public void HorizontalWishIntoUnknownCellsRefuses()
    {
        var snapshot = new BedrockCollisionSnapshot.Builder().Set(0, 1, 0, BedrockCollisionCell.KnownAir).Build(1);
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, false, 0);
        var input = new BedrockMovementInput(1.0, 0.0, 0f, 0f, false, false, false);
        var (next, applied, collision) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.05), s_testProfile, snapshot);
        Assert.Equal(BedrockInputDisposition.RefusedUnknownTerrain, applied.Disposition);
        Assert.Equal(BedrockCollisionOutcome.RefusedUnknown, collision.Outcome);
        Assert.Equal(state, next);
    }

    [Fact]
    public void AirborneFallAppliesGravityWithKnownOutcome()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 5.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, false, 3);
        var (next, applied, collision) = BedrockOneTickStepper.Step(state, BedrockMovementInput.Idle(), new BedrockMovementTick(4, 0.05), s_testProfile, snapshot);
        Assert.Equal(BedrockInputDisposition.Applied, applied.Disposition);
        Assert.Equal(BedrockCollisionOutcome.KnownFree, collision.Outcome);
        Assert.Equal(-1.0, next.VelocityY, 9);
        Assert.Equal(5.0 - 0.05, next.PositionY, 9);
        Assert.False(next.OnGround);
    }

    [Fact]
    public void JumpAppliesImpulseOnlyWithMeasuredProfile()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        var input = new BedrockMovementInput(0.0, 0.0, 0f, 0f, true, false, false);
        var (next, applied, _) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.05), s_testProfile, snapshot);
        Assert.Equal(BedrockInputDisposition.Applied, applied.Disposition);
        Assert.True(applied.AppliedJump);
        Assert.Equal(7.0, next.VelocityY, 9);

        var (held, heldApplied, _) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.05), BedrockMovementProfile.Default, snapshot);
        Assert.Equal(BedrockInputDisposition.UnavailableMode, heldApplied.Disposition);
        Assert.Equal(state, held);
    }

    [Fact]
    public void SprintAndCrouchStayUnavailable()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        foreach (bool sprint in new[] { true, false })
        {
            var input = new BedrockMovementInput(0.0, 0.0, 0f, 0f, false, sprint, !sprint);
            var (next, applied, _) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.05), s_testProfile, snapshot);
            Assert.Equal(BedrockInputDisposition.UnavailableMode, applied.Disposition);
            Assert.Equal(state, next);
        }
    }

    [Fact]
    public void HorizontalWishWithoutMeasuredProfileStaysUnavailable()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        var input = new BedrockMovementInput(1.0, 0.0, 0f, 0f, false, false, false);
        var (next, applied, _) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.05), BedrockMovementProfile.Default, snapshot);
        Assert.Equal(BedrockInputDisposition.UnavailableMode, applied.Disposition);
        Assert.Equal(state, next);
    }

    [Fact]
    public void SnapshotVersionsStayImmutableAndStepsStayDeterministic()
    {
        var builder = new BedrockCollisionSnapshot.Builder();
        builder.FillBox(-4, 0, -4, 4, 0, 4, BedrockCollisionCell.KnownSolid);
        builder.FillBox(-4, 1, -4, 4, 8, 4, BedrockCollisionCell.KnownAir);
        var first = builder.Build(7);
        builder.Set(0, 5, 0, BedrockCollisionCell.KnownSolid);
        Assert.Equal(BedrockCollisionCell.KnownAir, first.GetCell(0, 5, 0));
        var second = builder.Build(8);
        Assert.Equal(8L, second.Version);

        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 10f, -5f, true, 0);
        var input = new BedrockMovementInput(0.5, -0.25, 10f, -5f, false, false, false);
        var tick = new BedrockMovementTick(1, 0.05);
        var left = BedrockOneTickStepper.Step(state, input, tick, s_testProfile, GroundFloor(7));
        var right = BedrockOneTickStepper.Step(state, input, tick, s_testProfile, GroundFloor(8));
        Assert.Equal(left.Next, right.Next);
        Assert.Equal(left.Applied, right.Applied);
        Assert.Equal(left.Collision, right.Collision);

        var walking = state;
        for (ulong tickIndex = 1; tickIndex <= 4; tickIndex++)
            walking = BedrockOneTickStepper.Step(walking, input, new BedrockMovementTick(tickIndex, 0.05), s_testProfile, GroundFloor()).Next;
        Assert.Equal(0.5 + 4 * 0.5 * 4.0 * 0.05, walking.PositionX, 9);
        Assert.Equal(4UL, walking.TickIndex);
    }

    [Fact]
    public void NonFiniteNumbersAndBadTicksFailFast()
    {
        var snapshot = GroundFloor();
        var good = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        Assert.Throws<ArgumentException>(() => BedrockOneTickStepper.Step(new BedrockPhysicalState(double.NaN, 1.0, 0.5, 0, 0, 0, 0f, 0f, true, 0), BedrockMovementInput.Idle(), new BedrockMovementTick(1, 0.05), s_testProfile, snapshot));
        Assert.Throws<ArgumentException>(() => BedrockOneTickStepper.Step(good, new BedrockMovementInput(double.PositiveInfinity, 0, 0f, 0f, false, false, false), new BedrockMovementTick(1, 0.05), s_testProfile, snapshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => BedrockOneTickStepper.Step(good, BedrockMovementInput.Idle(), new BedrockMovementTick(1, 0.0), s_testProfile, snapshot));
        Assert.Throws<ArgumentException>(() => BedrockOneTickStepper.Step(good, BedrockMovementInput.Idle(), new BedrockMovementTick(3, 0.05), s_testProfile, snapshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => BedrockOneTickStepper.Step(good, new BedrockMovementInput(2.0, 0, 0f, 0f, false, false, false), new BedrockMovementTick(1, 0.05), s_testProfile, snapshot));
    }

    [Fact]
    public void NonFiniteProfileValuesFailFast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockMovementProfile(walkSpeedBlocksPerSecond: double.NaN, playerWidthBlocks: 0.6, playerHeightBlocks: 1.8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockMovementProfile(walkSpeedBlocksPerSecond: double.PositiveInfinity, playerWidthBlocks: 0.6, playerHeightBlocks: 1.8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockMovementProfile(gravityBlocksPerSecondSquared: double.NaN, terminalVelocityBlocksPerSecond: 40.0, playerWidthBlocks: 0.6, playerHeightBlocks: 1.8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockMovementProfile(jumpImpulseBlocksPerSecond: double.PositiveInfinity, playerWidthBlocks: 0.6, playerHeightBlocks: 1.8));
    }

    [Fact]
    public void GravityWithoutExtentsStaysUnavailable()
    {
        var profile = new BedrockMovementProfile(gravityBlocksPerSecondSquared: 20.0, terminalVelocityBlocksPerSecond: 40.0);
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 5.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, false, 3);
        var (next, applied, _) = BedrockOneTickStepper.Step(state, BedrockMovementInput.Idle(), new BedrockMovementTick(4, 0.05), profile, snapshot);
        Assert.Equal(BedrockInputDisposition.UnavailableMode, applied.Disposition);
        Assert.Equal(state, next);
    }

    [Fact]
    public void JumpWithoutExtentsStaysUnavailable()
    {
        var profile = new BedrockMovementProfile(jumpImpulseBlocksPerSecond: 7.0);
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        var input = new BedrockMovementInput(0.0, 0.0, 0f, 0f, true, false, false);
        var (next, applied, _) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.05), profile, snapshot);
        Assert.Equal(BedrockInputDisposition.UnavailableMode, applied.Disposition);
        Assert.Equal(state, next);
    }

    [Fact]
    public void ResidualFallWithoutExtentsStaysUnavailable()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, -5.0, 0.0, 0f, 0f, false, 0);
        var (next, applied, _) = BedrockOneTickStepper.Step(state, BedrockMovementInput.Idle(), new BedrockMovementTick(1, 0.05), BedrockMovementProfile.Default, snapshot);
        Assert.Equal(BedrockInputDisposition.UnavailableMode, applied.Disposition);
        Assert.Equal(state, next);
    }

    [Fact]
    public void TickIndexWrapsExplicitlyFromMaxValue()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, ulong.MaxValue);
        var (next, applied, _) = BedrockOneTickStepper.Step(state, BedrockMovementInput.Idle(), new BedrockMovementTick(0, 0.05), BedrockMovementProfile.Default, snapshot);
        Assert.Equal(BedrockInputDisposition.Applied, applied.Disposition);
        Assert.Equal(0UL, next.TickIndex);
    }

    [Fact]
    public void UnavailableCollisionIsUnevaluated()
    {
        var snapshot = GroundFloor();
        var state = new BedrockPhysicalState(0.5, 1.0, 0.5, 0.0, 0.0, 0.0, 0f, 0f, true, 0);
        var input = new BedrockMovementInput(0.0, 0.0, 0f, 0f, false, true, false);
        var (next, applied, collision) = BedrockOneTickStepper.Step(state, input, new BedrockMovementTick(1, 0.05), s_testProfile, snapshot);
        Assert.Equal(BedrockInputDisposition.UnavailableMode, applied.Disposition);
        Assert.Equal(state, next);
        Assert.Equal(BedrockCollisionOutcome.KnownFree, collision.Outcome);
        Assert.Equal(0, collision.UnknownCells);
    }
}
