using Umpk.Geometry;
using Umpk.Pathfinding.Execution.Templates;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>Chooses the handoff input (carry / coast / brake) for a grounded segment approaching its exit. It forward-simulates each candidate held input through <see cref="PhysicsSimulator.Run"/> over the immutable planning view and scores the resulting state against the segment's exit hints (physics coupling only through the simulation API, one predictor not three).</summary>
internal sealed class TransitionBrakingPlanner
{
    private const double GroundSpeedThreshold = 0.025;

    private readonly IPhysicsWorldView _world;
    private readonly PhysicsProfile _profile;
    private readonly PhysicsConditions _conditions;
    private readonly bool _allowSprint;

    internal TransitionBrakingPlanner(
        IPhysicsWorldView world, PhysicsProfile profile, in PhysicsConditions conditions, bool allowSprint)
    {
        _world = world;
        _profile = profile;
        _conditions = conditions;
        _allowSprint = allowSprint;
    }

    /// <summary>The carry-momentum candidate, with the segment's sprint wish filtered through <c>PathExecutionContext.AllowSprint</c>. Every construction of that candidate goes through here, so the flag reaches both the decision this returns (which templates turn into a live input) and the candidate <see cref="Score"/> forward-simulates. Scoring a sprinting handoff the executor can never emit would pick the brake point for a faster bot than the one that arrives.</summary>
    private TransitionBrakingDecision Carry(PathSegment segment)
        => TransitionBrakingDecision.CarryMomentum(segment.PreserveSprint && _allowSprint);

    /// <summary>Plans the handoff input for the current tick.</summary>
    internal TransitionBrakingDecision Plan(PathSegment segment, in Vec3d pos, in PhysicsState physics)
    {
        if (!physics.OnGround)
        {
            if (!segment.ExitHints.AllowAirBrake)
                return Carry(segment);

            return ChooseBest(segment, pos, physics);
        }

        bool requiresJumpEntry = segment.ExitHints.RequireJumpReady
            || segment.ExitTransition == PathTransitionType.PrepareJump;
        if (requiresJumpEntry)
        {
            // A jump entry without a speed envelope carries its existing momentum. That is right for a run-up into a Parkour or an Ascend, whose hint carries MaxExitSpeed = +infinity and so declares no envelope to respect - and it is more than right, it is necessary. Scoring a candidate against an infinite cap leaves the remaining-distance term with nothing to trade against, so the planner would brake to a standstill on the launch block.
            //
            // It is wrong for the nextImmediatelyJumps Turn, which PathSegmentBuilder builds with MinExitSpeed 0.05 and MaxExitSpeed 0.16. That segment states a speed envelope and the planner carried a full sprint straight past it. On the course's D8 row - a diagonal run-up into a jump that leaves cardinal and climbs - carrying through the launch cell walked the bot off a shelf whose surface is at y = 100 and into the void at y = 96.654.
            //
            // So the test is whether the segment declared an envelope, not which transition it is. A finite MaxExitSpeed is that declaration, and it is currently that Turn hint alone.
            if (double.IsPositiveInfinity(segment.ExitHints.MaxExitSpeed))
                return Carry(segment);

            return ChooseBest(segment, pos, physics);
        }

        if (segment.ExitTransition == PathTransitionType.ContinueStraight)
            return Carry(segment);

        // An UNGROUNDED handoff has nothing to brake for and something to lose by braking. The three transitions below all mean "arrive settled on the destination block", and a segment whose hints say the body will not be on the ground at the end of it has already said that is not what happens: course row E12's entry walks into a 1x1 bubble column, and the column takes the body's feet off the floor before its centre is even inside the cell. Braking that approach sheds the one thing that carries the body in.
        //
        // Read off the HINTS rather than the transition label, so the label stays what it always was for everything calibrated against it. AllowUngrounded is set for a swim segment - whose template never consults this planner - and for the walk that hands off into a vertical one, which is the whole of the new behaviour.
        bool requiresSlowEntry = !segment.ExitHints.AllowUngrounded
            && (segment.ExitHints.RequireStableFooting
                || segment.ExitTransition is PathTransitionType.FinalStop or PathTransitionType.Turn
                || segment.ExitTransition == PathTransitionType.LandingRecovery);
        if (!requiresSlowEntry)
            return Carry(segment);

        double forwardSpeed = Math.Max(0.0, SegmentGeometry.ProjectSpeedAlongHeading(physics, segment.HeadingX, segment.HeadingZ));
        double maxExitSpeed = TargetMaxExitSpeed(segment);
        if (forwardSpeed <= Math.Max(maxExitSpeed, GroundSpeedThreshold)
            && SegmentGeometry.RemainingDistanceAlongSegment(pos, segment) > 0.0)
        {
            // Slow enough already: no need to brake, keep drifting toward the end.
            return TransitionBrakingDecision.Coast;
        }

        return ChooseBest(segment, pos, physics);
    }

    private TransitionBrakingDecision ChooseBest(PathSegment segment, in Vec3d pos, in PhysicsState physics)
    {
        Span<TransitionBrakingDecision> candidates =
        [
            Carry(segment),
            TransitionBrakingDecision.Coast,
            TransitionBrakingDecision.Brake,
        ];

        TransitionBrakingDecision best = candidates[0];
        double bestScore = double.PositiveInfinity;
        foreach (TransitionBrakingDecision candidate in candidates)
        {
            double score = Score(segment, pos, physics, candidate);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private double Score(PathSegment segment, in Vec3d pos, in PhysicsState physics, TransitionBrakingDecision candidate)
    {
        PhysicsState start = physics with { Position = pos };
        var input = new MovementInput
        {
            Forward = candidate.HoldForward,
            Back = candidate.HoldBack,
            Sprint = candidate.HoldSprint,
        };

        int horizon = Math.Max(1, segment.ExitHints.HorizonTicks);
        Span<MovementInput> inputs = horizon <= 32 ? stackalloc MovementInput[horizon] : new MovementInput[horizon];
        inputs.Fill(input);

        PhysicsState end = PhysicsSimulator.Run(start, _conditions, _world, _profile, inputs);
        var simPos = end.Position;

        double score = 0.0;
        double exitSpeed = SegmentGeometry.ProjectSpeedAlongHeading(end, DesiredHeadingX(segment), DesiredHeadingZ(segment));

        if (segment.ExitHints.RequireGrounded && !end.OnGround)
            score += 1000.0;

        if (segment.ExitHints.RequireStableFooting && !SegmentGeometry.IsSettledOnTargetBlock(simPos, segment.End, end))
            score += 1000.0;

        if (exitSpeed < segment.ExitHints.MinExitSpeed)
            score += (segment.ExitHints.MinExitSpeed - exitSpeed) * 200.0;

        if (exitSpeed > segment.ExitHints.MaxExitSpeed)
            score += (exitSpeed - segment.ExitHints.MaxExitSpeed) * 200.0;

        if (segment.ExitHints.RequireStableFooting)
        {
            double dx = segment.End.X - simPos.X;
            double dz = segment.End.Z - simPos.Z;
            score += ((dx * dx) + (dz * dz)) * 20.0;
        }
        else
            score += Math.Abs(SegmentGeometry.RemainingDistanceAlongSegment(simPos, segment)) * 10.0;

        return score;
    }

    private static int DesiredHeadingX(PathSegment segment)
    {
        SegmentGeometry.GetExitHeading(segment, out int headingX, out _);
        return headingX;
    }

    private static int DesiredHeadingZ(PathSegment segment)
    {
        SegmentGeometry.GetExitHeading(segment, out _, out int headingZ);
        return headingZ;
    }

    private static double TargetMaxExitSpeed(PathSegment segment)
    {
        if (!double.IsPositiveInfinity(segment.ExitHints.MaxExitSpeed))
            return segment.ExitHints.MaxExitSpeed;

        return segment.ExitTransition switch
        {
            PathTransitionType.FinalStop => 0.03,
            PathTransitionType.Turn or PathTransitionType.LandingRecovery => 0.035,
            _ => double.PositiveInfinity,
        };
    }
}
