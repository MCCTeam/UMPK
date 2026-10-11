namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Immutable physical state with caller-interpreted position in blocks and velocity in blocks per second.</summary>
/// <remarks>The scaffold treats position as feet, which is an assumption without controlled-trace proof.</remarks>
internal readonly record struct BedrockPhysicalState(
    double PositionX,
    double PositionY,
    double PositionZ,
    double VelocityX,
    double VelocityY,
    double VelocityZ,
    float YawDegrees,
    float PitchDegrees,
    bool OnGround,
    ulong TickIndex)
{
    /// <summary>Validates finite position, velocity, and rotation values.</summary>
    internal void Validate()
    {
        if (double.IsNaN(PositionX) || double.IsInfinity(PositionX) || double.IsNaN(PositionY) || double.IsInfinity(PositionY) || double.IsNaN(PositionZ) || double.IsInfinity(PositionZ))
            throw new ArgumentException("Position must hold finite values.");
        if (double.IsNaN(VelocityX) || double.IsInfinity(VelocityX) || double.IsNaN(VelocityY) || double.IsInfinity(VelocityY) || double.IsNaN(VelocityZ) || double.IsInfinity(VelocityZ))
            throw new ArgumentException("Velocity must hold finite values.");
        if (float.IsNaN(YawDegrees) || float.IsInfinity(YawDegrees) || float.IsNaN(PitchDegrees) || float.IsInfinity(PitchDegrees))
            throw new ArgumentException("Rotation must hold finite values.");
    }
}
