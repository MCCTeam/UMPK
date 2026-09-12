using Umpk.Geometry;
using Xunit;

namespace Umpk.Physics.Tests.Parity;

/// <summary>Smoke tests over the course fixture geometry.</summary>
public sealed class CourseSmokeTests
{
    [Fact]
    public void FlatSprint_TraversesStartSection()
    {
        FixtureWorldRunner runner = FixtureWorldRunner.OnCourse(new Vec3d(Pr3076Course.OX + 0.5, Pr3076Course.OY + 1, Pr3076Course.OZ + 0.5), yaw: -90f);
        // yaw -90 faces +X (east), the course direction.
        runner.Run(new MovementInput { Forward = true, Sprint = true }, 60);
        Assert.True(runner.State.Position.X > Pr3076Course.OX + 3, $"did not sprint east, x={runner.State.Position.X}");
        Assert.True(runner.State.OnGround);
    }

    [Fact]
    public void SlimePit_ReboundsPlayer()
    {
        // Drop into the slime pit at x=91.5 (pit bottom slime at OY-2).
        FixtureWorldRunner runner = FixtureWorldRunner.OnCourse(new Vec3d(91.5, Pr3076Course.OY + 3, Pr3076Course.OZ + 0.5), yaw: 0f);
        bool bounced = false;
        for (int i = 0; i < 40; i++)
        {
            StepResult r = runner.Step(MovementInput.None);
            if (r.Events.Bounced)
                bounced = true;

        }

        Assert.True(bounced, "slime pit did not bounce the player");
    }

    [Fact]
    public void CrawlTunnel_ForcesSwimPose()
    {
        FixtureWorldRunner runner = FixtureWorldRunner.OnCourse(new Vec3d(103.5, Pr3076Course.OY + 8, Pr3076Course.OZ + 0.5), yaw: -90f);
        StepResult r = runner.Step(MovementInput.None);
        Assert.Equal((int)Umpk.Game.Entities.EntityPose.Swimming, (int)r.State.Pose);
    }

    private sealed class FixtureWorldRunner
    {
        private readonly PlayerPhysics _engine;

        private FixtureWorldRunner(PlayerPhysics engine) => _engine = engine;

        public PhysicsState State => _engine.State;

        public static FixtureWorldRunner OnCourse(Vec3d start, float yaw)
        {
            var world = Pr3076Course.Build();
            var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
            engine.SetConditions(PhysicsConditions.Default);
            engine.Reset(start, yaw, 0f);
            engine.SetRotation(yaw, 0f);
            return new FixtureWorldRunner(engine);
        }

        public StepResult Step(MovementInput input) => _engine.Step(input);

        public void Run(MovementInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                _engine.Step(input);

        }
    }
}
