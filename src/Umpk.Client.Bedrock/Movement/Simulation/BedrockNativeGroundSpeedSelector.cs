namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Selects raw base-speed bits from caller-qualified same-entity row and membership inputs.</summary>
/// <remarks>This inert data-copy contract performs no arithmetic, ECS lookup, q-cube scaling, MobTravel write or runtime movement application.</remarks>
internal static class BedrockNativeGroundSpeedSelector
{
    internal static BedrockNativeArithmeticResult<uint> SelectBaseSpeedBits(BedrockNativeGroundSpeedInput input)
    {
        if (input.AttributesRowPresent is not true || input.SpeedRowPresent is not true)
        {
            return BedrockNativeArithmeticResult<uint>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
        }

        uint? selected = input.PlayerMembership switch
        {
            BedrockNativePlayerMembership.AbsentOrInvalidSameDomain => input.SpeedFirstFloatBits,
            BedrockNativePlayerMembership.PresentSameEntity when input.AttributesEngagement is { } engagement =>
                engagement == 0 ? 0x00000000U : input.AttributesFirstFloatBits,
            _ => null
        };
        return selected is { } bits
            ? BedrockNativeArithmeticResult<uint>.FromValue(bits)
            : BedrockNativeArithmeticResult<uint>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }
}

/// <summary>Caller-resolved optional Player membership in the same entity, generation and registry domain.</summary>
internal enum BedrockNativePlayerMembership
{
    Unknown,
    PresentSameEntity,
    AbsentOrInvalidSameDomain
}

/// <summary>Caller-supplied required row memberships and optional observed raw fields; null means unknown, never a default native value.</summary>
/// <remarks>Both row memberships are required by the enclosing Ground query; these inputs do not qualify a native registry or live actor provider.</remarks>
internal readonly record struct BedrockNativeGroundSpeedInput(
    bool? AttributesRowPresent,
    bool? SpeedRowPresent,
    BedrockNativePlayerMembership PlayerMembership,
    byte? AttributesEngagement,
    uint? AttributesFirstFloatBits,
    uint? SpeedFirstFloatBits);
