using Umpk.Geometry;

namespace Umpk.Physics.Tests.Parity;

/// <summary>Records and replays characterization traces. Recording runs UMPK's own engine over an input script on a course world and captures per-tick state. Replay drives the same input script through <see cref="PhysicsSimulator"/> against the same geometry and compares within epsilon. Because both sides are UMPK, a zero-deviation replay certifies determinism/regression stability of UMPK's engine, not agreement with vanilla. The same pipeline will validate a future vanilla-recorded trace unchanged, at which point it becomes a real parity oracle.</summary>
public static class TraceReplay
{
    /// <summary>Records a characterization trace by running the engine over the input script.</summary>
    public static PhysicsTrace Record(
        IPhysicsWorldView world,
        PhysicsProfile profile,
        PhysicsConditions conditions,
        Vec3d start,
        float yaw,
        float pitch,
        IReadOnlyList<MovementInput> inputs,
        string profileName)
    {
        var engine = new PlayerPhysics(world, profile);
        engine.SetConditions(conditions);
        engine.Reset(start, yaw, pitch);
        engine.SetRotation(yaw, pitch);

        var ticks = new List<PhysicsTrace.TraceTick>(inputs.Count);
        foreach (MovementInput input in inputs)
        {
            PhysicsState s = engine.Step(input).State;
            ticks.Add(new PhysicsTrace.TraceTick(
                PhysicsTrace.EncodeInput(input),
                s.Position,
                s.Velocity,
                (int)s.Pose,
                s.OnGround));
        }

        return new PhysicsTrace
        {
            Profile = profileName,
            StartPosition = start,
            StartYaw = yaw,
            StartPitch = pitch,
            Ticks = ticks,
        };
    }

    /// <summary>Replays a committed trace against the given world/profile and returns the max per-tick position and velocity deviation observed. A green parity run keeps both under the epsilon.</summary>
    public static ReplayResult Replay(
        PhysicsTrace trace,
        IPhysicsWorldView world,
        PhysicsProfile profile,
        PhysicsConditions conditions)
    {
        var engine = new PlayerPhysics(world, profile);
        engine.SetConditions(conditions);
        engine.Reset(trace.StartPosition, trace.StartYaw, trace.StartPitch);
        engine.SetRotation(trace.StartYaw, trace.StartPitch);

        double maxPosDev = 0;
        double maxVelDev = 0;
        int poseMismatches = 0;

        foreach (PhysicsTrace.TraceTick expected in trace.Ticks)
        {
            MovementInput input = PhysicsTrace.DecodeInput(expected.InputBits);
            PhysicsState actual = engine.Step(input).State;

            maxPosDev = Math.Max(maxPosDev, Distance(actual.Position, expected.Position));
            maxVelDev = Math.Max(maxVelDev, Distance(actual.Velocity, expected.Velocity));
            if ((int)actual.Pose != expected.Pose)
                poseMismatches++;

        }

        return new ReplayResult(maxPosDev, maxVelDev, poseMismatches, trace.Ticks.Count);
    }

    private static double Distance(Vec3d a, Vec3d b) => a.Subtract(b).Length();

    /// <summary>The outcome of a replay: max deviations and pose mismatch count.</summary>
    public readonly record struct ReplayResult(double MaxPositionDeviation, double MaxVelocityDeviation, int PoseMismatches, int TickCount);
}
