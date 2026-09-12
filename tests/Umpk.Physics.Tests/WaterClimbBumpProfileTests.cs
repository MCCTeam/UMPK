using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>
/// Era coverage for the in-water climbable bump: the water arm sets the vertical delta movement to <c>0.2</c> when the player is horizontally colliding AND on a climbable block, immediately before the <c>(slowDown, 0.8F, slowDown)</c> damping multiply. It is what makes a ladder or vine usable while swimming.
///
/// <para>The clause was added in 1.14. Earlier eras go directly from movement to the three damping multiplies; every supported era from 1.14 onward includes the clause.</para>
///
/// <para>The boundary is protocol 404 to 477 (1.13.2 to 1.14).</para>
/// </summary>
public sealed class WaterClimbBumpProfileTests
{
    // Vanilla's own literals, transcribed. The bump value, then the water Y damping it is multiplied by on the very next statement.
    private const double VanillaBump = 0.2;
    private const double VanillaWaterYDamping = 0.8f;

    /// <summary>Deep still water against a solid wall, with or without a ladder at the player's own block. Deep on purpose: a box raised by the ledge-hop probe is still inside liquid, so The fluid ledge hop cannot fire and mask the bump.</summary>
    private static PlayerPhysics NewEngine(int protocol, bool withLadder)
    {
        var world = new FixtureWorld()
            .Fill(-2, 60, -2, 2, 70, 0, BlockKind.Water)
            .Fill(-2, 60, 1, 2, 70, 2, BlockKind.Stone);

        if (withLadder)
            world.Set(0, 64, 0, BlockKind.Ladder);

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 64.0, 0.69), 0f, 0f);
        engine.SetRotation(0f, 0f);
        return engine;
    }

    private static readonly MovementInput PressForward = new() { Forward = true };

    private static double[] VerticalTrack(int protocol, bool withLadder, int ticks)
    {
        PlayerPhysics engine = NewEngine(protocol, withLadder);
        var track = new double[ticks];
        for (int i = 0; i < ticks; i++)
        {
            engine.Step(PressForward);
            track[i] = engine.State.Velocity.Y;
        }

        return track;
    }

    // (a) LEGACY BAND: vanilla has no clause at all, so a ladder in the water must not
    //     perturb a single tick of the trajectory.
    [Theory]
    [InlineData(47)]    // 1.8.9
    [InlineData(110)]   // 1.9.4
    [InlineData(210)]   // 1.10.2
    [InlineData(316)]   // 1.11.2
    [InlineData(340)]   // 1.12.2
    [InlineData(393)]   // 1.13
    [InlineData(401)]   // 1.13.1
    [InlineData(404)]   // 1.13.2, the LAST protocol without the bump
    public void LegacyWater_ClimbableMakesNoDifference(int protocol)
    {
        // Sanity: the fixture really is the scenario the clause tests for.
        PlayerPhysics probe = NewEngine(protocol, withLadder: true);
        probe.Step(PressForward);
        Assert.True(probe.State.InWater);
        Assert.True(probe.State.OnClimbable);
        Assert.True(probe.State.HorizontalCollision);

        double[] withoutLadder = VerticalTrack(protocol, withLadder: false, 20);
        double[] withLadder = VerticalTrack(protocol, withLadder: true, 20);

        Assert.Equal(withoutLadder, withLadder);
    }

    // (b) MODERN BAND (477+): the clause is real, and its first-tick result is vanilla's own
    //     arithmetic - the vertical delta is REPLACED by 0.2, then multiplied by 0.8F.
    [Theory]
    [InlineData(477)]   // 1.14, the FIRST protocol with the bump
    [InlineData(498)]   // 1.14.4
    [InlineData(754)]   // 1.16.5
    [InlineData(770)]   // 1.21.5
    [InlineData(776)]   // 26.2
    public void ModernWater_ClimbableBumpsUpward(int protocol)
    {
        PlayerPhysics engine = NewEngine(protocol, withLadder: true);

        engine.Step(PressForward);

        Assert.True(engine.State.OnClimbable);
        Assert.True(engine.State.HorizontalCollision);

        // 0.2 -> multiplied by the 0.8F Y damping -> then the sprint-aware vertical tail, which for a non-sprinting player with default gravity subtracts gravity/16 = 0.005.
        double expected = (VanillaBump * VanillaWaterYDamping) - (0.08 / 16.0);
        Assert.Equal(expected, engine.State.Velocity.Y);

        double[] withoutLadder = VerticalTrack(protocol, withLadder: false, 20);
        double[] withLadder = VerticalTrack(protocol, withLadder: true, 20);
        Assert.NotEqual(withoutLadder, withLadder);
    }

    // (c) The profile breakpoint itself, pinned independently of the dataset (the dataset side is
    //     pinned by Umpk.Client.Tests.PhysicsProfileConformanceTests.
    //     DatasetWaterClimbBump_MatchesVanilla_ForEveryProtocol).
    [Theory]
    [InlineData(47, false)]
    [InlineData(107, false)]
    [InlineData(340, false)]
    [InlineData(393, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]   // last protocol without the bump
    [InlineData(477, true)]    // first protocol with the bump
    [InlineData(498, true)]
    [InlineData(770, true)]
    [InlineData(776, true)]
    public void ForProtocol_PinsTheWaterClimbBumpBreakpoint(int protocol, bool expected)
    {
        Assert.Equal(expected, PhysicsProfile.ForProtocol(protocol).WaterClimbBumpAvailable);
    }

    // (d) The two axes are NOT the same boundary. 393/401/404 are sprint-aware water travel AND
    //     bump-free; this is the whole reason waterClimbBump is its own axis rather than a widening
    //     of waterTravel.
    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    public void The1_13Band_IsSprintAwareButBumpFree(int protocol)
    {
        PhysicsProfile profile = PhysicsProfile.ForProtocol(protocol);

        Assert.Equal(WaterTravelEra.SprintAware, profile.WaterTravel);
        Assert.False(profile.WaterClimbBumpAvailable);
    }
}
