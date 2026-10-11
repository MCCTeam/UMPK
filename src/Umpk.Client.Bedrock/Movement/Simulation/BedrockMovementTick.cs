namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>A single locomotion tick supplied by the session clock with an explicit caller duration.</summary>
/// <remarks>The duration range is local safety policy and 0.05 remains an unmeasured assumption.</remarks>
internal readonly record struct BedrockMovementTick(ulong TickIndex, double DeltaSeconds)
{
    /// <summary>Validates that the tick duration is finite and in (0, 1] seconds.</summary>
    internal void Validate()
    {
        if (double.IsNaN(DeltaSeconds) || double.IsInfinity(DeltaSeconds) || DeltaSeconds <= 0.0 || DeltaSeconds > 1.0)
            throw new ArgumentOutOfRangeException(nameof(DeltaSeconds), "The tick duration must be a finite value in (0, 1] seconds.");
    }
}
