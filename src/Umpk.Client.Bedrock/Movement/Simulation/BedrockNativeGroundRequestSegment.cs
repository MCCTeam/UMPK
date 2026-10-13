namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Projects one caller-qualified Ground acceleration and MoveRequest arithmetic segment without applying state.</summary>
/// <remarks>The caller supplies same-entity post-intent rows, resolved effective speed factor and post-CRT basis. Auxiliary friction, saved-position, intervening hook effects, collision feedback and complete tick/clock ownership remain caller obligations; this does not activate movement.</remarks>
internal static class BedrockNativeGroundRequestSegment
{
    internal static BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue> Compute(BedrockNativeGroundRequestInput input)
    {
        if (input.PostIntent is not { } intent || input.PriorDelta is not { } prior || input.ExistingRequest is not { } request
            || input.SpeedInput is not { } speedInput || input.ResolvedEffectiveFriction is not { } friction
            || input.Basis is not { } basis || basis.Availability == BedrockNativeRotationBasisAvailability.Unknown)
        {
            return BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        if (basis.Availability != BedrockNativeRotationBasisAvailability.ResolvedArithmeticBits)
        {
            return BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue>.Refused(BedrockNativeArithmeticDisposition.UnsupportedBranch);
        }

        BedrockNativeArithmeticResult<uint> selected = BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(speedInput);
        if (selected.Disposition != BedrockNativeArithmeticDisposition.Computed)
        {
            return BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue>.Refused(selected.Disposition);
        }

        float baseSpeed = BitConverter.Int32BitsToSingle(unchecked((int)selected.Value.GetValueOrDefault()));
        BedrockNativeArithmeticResult<float> speed = BedrockNativeGroundArithmetic.ComputeGroundSelectedSpeed(baseSpeed, friction);
        if (speed.Disposition != BedrockNativeArithmeticDisposition.Computed)
        {
            return BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue>.Refused(speed.Disposition);
        }

        BedrockNativeArithmeticResult<BedrockNativeFloat3> accelerated = BedrockNativeGroundArithmetic.AccumulateGroundDelta(intent, prior, speed.Value, basis);
        if (accelerated.Disposition != BedrockNativeArithmeticDisposition.Computed)
        {
            return BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue>.Refused(accelerated.Disposition);
        }

        BedrockNativeFloat3 delta = accelerated.Value.GetValueOrDefault();
        BedrockNativeArithmeticResult<BedrockNativeFloat3> accumulated = BedrockNativeGroundArithmetic.AccumulateMoveRequestXYZ(delta, request);
        if (accumulated.Disposition != BedrockNativeArithmeticDisposition.Computed)
        {
            return BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue>.Refused(accumulated.Disposition);
        }

        // TravelMoveRequest saves current SV delta after acceleration, before accumulating it into the existing request.
        return BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue>.FromValue(new(speed.Value.GetValueOrDefault(), delta, accumulated.Value.GetValueOrDefault(), new(delta.X, delta.Z)));
    }
}

/// <summary>Explicit caller-qualified values; null means unknown and supplies no native default.</summary>
internal readonly record struct BedrockNativeGroundRequestInput(
    BedrockNativeFloat3? PostIntent,
    BedrockNativeFloat3? PriorDelta,
    BedrockNativeFloat3? ExistingRequest,
    BedrockNativeGroundSpeedInput? SpeedInput,
    float? ResolvedEffectiveFriction,
    BedrockNativeRotationBasis? Basis);

/// <summary>Atomic arithmetic output; saved delta X/Z is the accelerated SV value before request addition.</summary>
internal readonly record struct BedrockNativeGroundRequestValue(
    float SelectedTravelSpeed,
    BedrockNativeFloat3 AcceleratedDelta,
    BedrockNativeFloat3 AccumulatedRequest,
    BedrockNativeFloat2 SavedDeltaXZ);
