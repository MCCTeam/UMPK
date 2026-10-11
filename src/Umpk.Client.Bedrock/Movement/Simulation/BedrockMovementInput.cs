namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Immutable one-tick input with the horizontal wish expressed in world space as scaffold policy so no yaw-axis claim is needed.</summary>
internal readonly record struct BedrockMovementInput(
    double WishX,
    double WishZ,
    float YawDegrees,
    float PitchDegrees,
    bool WantJump,
    bool WantSprint,
    bool WantCrouch)
{
    /// <summary>Validates finite rotation values and a finite horizontal wish with each component in [-1, 1].</summary>
    internal void Validate()
    {
        if (double.IsNaN(WishX) || double.IsInfinity(WishX) || double.IsNaN(WishZ) || double.IsInfinity(WishZ))
            throw new ArgumentException("The horizontal wish must hold finite values.");
        if (Math.Abs(WishX) > 1.0 || Math.Abs(WishZ) > 1.0)
            throw new ArgumentOutOfRangeException(nameof(WishX), "Each horizontal wish component must stay in [-1, 1].");
        if (float.IsNaN(YawDegrees) || float.IsInfinity(YawDegrees) || float.IsNaN(PitchDegrees) || float.IsInfinity(PitchDegrees))
            throw new ArgumentException("Rotation must hold finite values.");
    }

    /// <summary>Creates an idle input with zero wish and no requested actions.</summary>
    internal static BedrockMovementInput Idle(float yawDegrees = 0f, float pitchDegrees = 0f) => new(0.0, 0.0, yawDegrees, pitchDegrees, false, false, false);
}
