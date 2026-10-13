namespace Umpk.Client.Bedrock.Movement.Simulation;

// Inert finite arithmetic leaves; callers supply native row and branch authority. This does not apply movement or supply a trig backend.
internal static class BedrockNativeGroundArithmetic
{
    private static readonly float s_intentScale = BitConverter.Int32BitsToSingle(0x3f7ae148);
    private static readonly float s_yawScale = BitConverter.Int32BitsToSingle(0x3c8efa35);
    private static readonly float s_groundThreshold = BitConverter.Int32BitsToSingle(0x38d1b717);
    private static readonly float s_secondFrictionCoefficient = BitConverter.Int32BitsToSingle(0x3db851e8);
    private const uint FrictionEpsilonBits = 0x34000000;

    internal static BedrockNativeArithmeticResult<BedrockNativeFloat3> ScaleIntentXYZ(BedrockNativeFloat3? retainedLocalVelocity)
    {
        if (retainedLocalVelocity is not { } input)
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        ValidateFinite(input, nameof(retainedLocalVelocity));
        if (!IsRepresentable(input)
            || !TryMultiply(input.Z, s_intentScale, out float z)
            || !TryMultiply(input.Y, s_intentScale, out float y)
            || !TryMultiply(input.X, s_intentScale, out float x))
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        return BedrockNativeArithmeticResult<BedrockNativeFloat3>.FromValue(new(x, y, z));
    }

    internal static BedrockNativeArithmeticResult<float> PrepareYawRadians(float? actorYaw)
    {
        if (actorYaw is not { } input)
        {
            return BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        ValidateFinite(input, nameof(actorYaw));
        if (!IsRepresentable(input) || !TryMultiply(input, s_yawScale, out float radians))
        {
            return BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        return BedrockNativeArithmeticResult<float>.FromValue(radians);
    }

    internal static BedrockNativeArithmeticResult<BedrockNativeFloat3> AccumulateGroundDelta(BedrockNativeFloat3? intent, BedrockNativeFloat3? priorDelta, float? selectedSpeed, BedrockNativeRotationBasis? basis)
    {
        if (intent is not { } input || priorDelta is not { } prior || selectedSpeed is not { } speed || basis is not { } rotation || rotation.Availability == BedrockNativeRotationBasisAvailability.Unknown)
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        if (rotation.Availability != BedrockNativeRotationBasisAvailability.ResolvedArithmeticBits)
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.UnsupportedBranch);
        }

        ValidateFinite(input, nameof(intent));
        ValidateFinite(prior, nameof(priorDelta));
        ValidateFinite(speed, nameof(selectedSpeed));
        ValidateFinite(rotation.Sine, nameof(basis));
        ValidateFinite(rotation.Cosine, nameof(basis));
        if (!IsRepresentable(input) || !IsRepresentable(prior) || !IsRepresentable(speed) || !IsRepresentable(rotation.Sine) || !IsRepresentable(rotation.Cosine)
            || !TryMultiply(input.X, input.X, out float xx)
            || !TryMultiply(input.Y, input.Y, out float yy)
            || !TryAdd(yy, xx, out float xy)
            || !TryMultiply(input.Z, input.Z, out float zz)
            || !TryAdd(zz, xy, out float lengthSquared))
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        if (lengthSquared < s_groundThreshold)
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.FromValue(prior);
        }

        float root = MathF.Sqrt(lengthSquared);
        float denominator = root > 1f ? root : 1f;
        if (!IsRepresentable(root)
            || !TryDivide(speed, denominator, out float quotient)
            || !TryMultiply(input.X, quotient, out float scaledX)
            || !TryMultiply(input.Y, quotient, out float scaledY)
            || !TryMultiply(input.Z, quotient, out float scaledZ)
            || !TryMultiply(scaledX, rotation.Cosine, out float xCosine)
            || !TryMultiply(rotation.Sine, scaledZ, out float sineZ)
            || !TrySubtract(xCosine, sineZ, out float accelerationX)
            || !TryAdd(accelerationX, prior.X, out float x)
            || !TryAdd(scaledY, prior.Y, out float y)
            || !TryMultiply(scaledZ, rotation.Cosine, out float zCosine)
            || !TryMultiply(scaledX, rotation.Sine, out float xSine)
            || !TryAdd(xSine, zCosine, out float accelerationZ)
            || !TryAdd(accelerationZ, prior.Z, out float z))
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        return BedrockNativeArithmeticResult<BedrockNativeFloat3>.FromValue(new(x, y, z));
    }

    internal static BedrockNativeArithmeticResult<BedrockNativeFloat2> ComputeOrdinaryGroundFrictionXZ(BedrockNativeFloat2? priorDelta, BedrockNativeOrdinaryFrictionInput? input)
    {
        if (priorDelta is not { } prior || input is not { } friction || friction.AuxiliaryFriction is not { } auxiliary
            || friction.Branch.VexPresent is not { } vex || friction.Branch.OverridePresent is not { } overridden
            || friction.Branch.ActorFlags8NonNegative is not { } nonNegativeFlags || friction.Branch.AppliesVertical is not { } vertical)
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat2>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        if (vex || overridden || !nonNegativeFlags || vertical)
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat2>.Refused(BedrockNativeArithmeticDisposition.UnsupportedBranch);
        }

        if (friction.Modifier30.IsEngaged is not { } engaged30 || friction.Modifier40.IsEngaged is not { } engaged40
            || (engaged30 && !friction.Modifier30.Value.HasValue) || (engaged40 && !friction.Modifier40.Value.HasValue))
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat2>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        float modifier30 = engaged30 ? friction.Modifier30.Value.GetValueOrDefault() : 1f;
        float modifier40 = engaged40 ? friction.Modifier40.Value.GetValueOrDefault() : 1f;
        ValidateFinite(prior.X, nameof(priorDelta));
        ValidateFinite(prior.Z, nameof(priorDelta));
        ValidateFinite(auxiliary, nameof(input));
        ValidateFinite(modifier30, nameof(input));
        ValidateFinite(modifier40, nameof(input));
        if (!IsRepresentable(prior.X) || !IsRepresentable(prior.Z) || !IsRepresentable(auxiliary) || !IsRepresentable(modifier30) || !IsRepresentable(modifier40)
            || !TrySubtract(1f, auxiliary, out float firstDifference)
            || !TryMultiply(firstDifference, modifier30, out float firstProduct)
            || !TryComplementClamp(firstProduct, out float firstFactor)
            || !TryMultiply(s_secondFrictionCoefficient, modifier40, out float secondProduct)
            || !TryComplementClamp(secondProduct, out float secondFactor)
            || !TryMultiply(firstFactor, secondFactor, out float factor))
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat2>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        float xFactor = AbsoluteBits(prior.X) <= FrictionEpsilonBits ? 0f : factor;
        float zFactor = AbsoluteBits(prior.Z) <= FrictionEpsilonBits ? 0f : factor;
        if (!TryMultiply(xFactor, prior.X, out float x) || !TryMultiply(zFactor, prior.Z, out float z))
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat2>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        return BedrockNativeArithmeticResult<BedrockNativeFloat2>.FromValue(new(x, z));
    }

    /// <summary>Computes selected native travel-speed bits from explicit resolved finite base speed and effective Ground factor.</summary>
    /// <remarks>The caller resolves the actual terrain/variant/SoulSpeed/global/zero-substitution branch first. Each operation rounds separately; this performs no terrain lookup, row publication, clock conversion or profile activation.</remarks>
    internal static BedrockNativeArithmeticResult<float> ComputeGroundSelectedSpeed(float? baseSpeed, float? resolvedEffectiveFriction)
    {
        if (baseSpeed is not { } speed || resolvedEffectiveFriction is not { } friction)
        {
            return BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        ValidateFinite(speed, nameof(baseSpeed));
        ValidateFinite(friction, nameof(resolvedEffectiveFriction));
        if (!IsRepresentable(speed) || !IsRepresentable(friction)
            || !TryDivide(BitConverter.Int32BitsToSingle(0x3f0bc6a9), friction, out float quotient)
            || !TryMultiply(quotient, quotient, out float squared)
            || !TryMultiply(squared, quotient, out float cubed)
            || !TryMultiply(speed, cubed, out float selected))
        {
            return BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        return BedrockNativeArithmeticResult<float>.FromValue(selected);
    }

    /// <summary>Adds current native SV delta to existing MoveRequest XYZ in source X, Y, Z order.</summary>
    /// <remarks>Delta is an increment per admitted native dispatch, not blocks per second. Existing contributions remain; the caller owns row association, original-delta retention, saved-position and other request side effects.</remarks>
    internal static BedrockNativeArithmeticResult<BedrockNativeFloat3> AccumulateMoveRequestXYZ(BedrockNativeFloat3? currentDelta, BedrockNativeFloat3? existingRequest)
    {
        if (currentDelta is not { } delta || existingRequest is not { } request)
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        ValidateFinite(delta, nameof(currentDelta));
        ValidateFinite(request, nameof(existingRequest));
        if (!IsRepresentable(delta) || !IsRepresentable(request)
            || !TryAdd(delta.X, request.X, out float x)
            || !TryAdd(delta.Y, request.Y, out float y)
            || !TryAdd(delta.Z, request.Z, out float z))
        {
            return BedrockNativeArithmeticResult<BedrockNativeFloat3>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }

        return BedrockNativeArithmeticResult<BedrockNativeFloat3>.FromValue(new(x, y, z));
    }

    private static void ValidateFinite(BedrockNativeFloat3 value, string parameter)
    {
        ValidateFinite(value.X, parameter);
        ValidateFinite(value.Y, parameter);
        ValidateFinite(value.Z, parameter);
    }

    private static void ValidateFinite(float value, string parameter)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentException("Native arithmetic inputs must be finite.", parameter);
        }
    }

    private static bool IsRepresentable(BedrockNativeFloat3 value) => IsRepresentable(value.X) && IsRepresentable(value.Y) && IsRepresentable(value.Z);

    private static bool IsRepresentable(float value)
    {
        uint bits = AbsoluteBits(value);
        return float.IsFinite(value) && (bits == 0 || bits >= 0x00800000);
    }

    private static uint AbsoluteBits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value)) & 0x7fffffffU;

    // Each primitive materializes one binary32 result before the next operation; underflow/overflow refuses the whole returned value.
    private static bool TryMultiply(float left, float right, out float result)
    {
        result = left * right;
        return IsRepresentable(result) && (result != 0f || left == 0f || right == 0f);
    }

    private static bool TryDivide(float left, float right, out float result)
    {
        result = left / right;
        return IsRepresentable(result) && (result != 0f || left == 0f);
    }

    private static bool TryAdd(float left, float right, out float result)
    {
        result = left + right;
        return IsRepresentable(result);
    }

    private static bool TrySubtract(float left, float right, out float result)
    {
        result = left - right;
        return IsRepresentable(result);
    }

    private static bool TryComplementClamp(float value, out float result)
    {
        float clamped = value < 0f ? 0f : value > 1f ? 1f : value;
        return TrySubtract(1f, clamped, out result);
    }
}

internal readonly record struct BedrockNativeFloat3(float X, float Y, float Z);

internal readonly record struct BedrockNativeFloat2(float X, float Z);

internal enum BedrockNativeArithmeticDisposition
{
    MissingNativeInputs,
    Computed,
    UnsupportedBranch,
    UnrepresentableFiniteSubset
}

internal readonly record struct BedrockNativeArithmeticResult<T> where T : struct
{
    private BedrockNativeArithmeticResult(BedrockNativeArithmeticDisposition disposition, T? value)
    {
        Disposition = disposition;
        Value = value;
    }

    internal BedrockNativeArithmeticDisposition Disposition { get; }

    internal T? Value { get; }

    internal static BedrockNativeArithmeticResult<T> FromValue(T value) => new(BedrockNativeArithmeticDisposition.Computed, value);

    internal static BedrockNativeArithmeticResult<T> Refused(BedrockNativeArithmeticDisposition disposition)
    {
        if (disposition is not (BedrockNativeArithmeticDisposition.MissingNativeInputs or BedrockNativeArithmeticDisposition.UnsupportedBranch or BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        return new(disposition, null);
    }
}

internal enum BedrockNativeRotationBasisAvailability
{
    Unknown,
    ResolvedArithmeticBits,
    Unsupported
}

internal readonly record struct BedrockNativeRotationBasis(float Sine, float Cosine, BedrockNativeRotationBasisAvailability Availability);

internal readonly record struct BedrockNativeFrictionModifier(bool? IsEngaged, float? Value);

internal readonly record struct BedrockNativeOrdinaryFrictionBranch(bool? VexPresent, bool? OverridePresent, bool? ActorFlags8NonNegative, bool? AppliesVertical);

internal readonly record struct BedrockNativeOrdinaryFrictionInput(float? AuxiliaryFriction, BedrockNativeFrictionModifier Modifier30, BedrockNativeFrictionModifier Modifier40, BedrockNativeOrdinaryFrictionBranch Branch);
