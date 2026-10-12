using Umpk.Client.Bedrock.Movement.Simulation;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Movement.Simulation;

public sealed class BedrockNativeGroundSpeedSelectorTests
{
    // Opposing artificial raw bits prove the qualified branch/load contract; no runtime/native row values or arithmetic parity are claimed.
    [Theory]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)1, 0x3e800000U, 0x3f000000U, 0x3e800000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)2, 0x3e800000U, 0x3f000000U, 0x3e800000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)255, 0x3e800000U, 0x3f000000U, 0x3e800000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)0, 0x80000000U, 0x3f000000U, 0x00000000U)]
    [InlineData((int)BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, (byte)255, 0x3e800000U, 0x3f000000U, 0x3f000000U)]
    [InlineData((int)BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, (byte)0, 0x3e800000U, 0x3f000000U, 0x3f000000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)0, null, null, 0x00000000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)2, 0x3e800000U, null, 0x3e800000U)]
    [InlineData((int)BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, null, null, 0x3f000000U, 0x3f000000U)]
    public void BranchSelectsLiteralOpposingBitsAndIgnoresUnreadFields(int membership, byte? engagement, uint? attributes, uint? speed, uint expected)
    {
        var input = new BedrockNativeGroundSpeedInput(true, true, (BedrockNativePlayerMembership)membership, engagement, attributes, speed);
        AssertSelected(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(input), expected);
    }

    [Theory]
    [InlineData(0x00000000U)]
    [InlineData(0x80000000U)]
    [InlineData(0x00000001U)]
    [InlineData(0x7f800001U)]
    [InlineData(0x7fc12345U)]
    [InlineData(0x7f800000U)]
    [InlineData(0xff800000U)]
    [InlineData(0xffffffffU)]
    public void AttributesSelectionCopiesEverySuppliedPayloadBit(uint bits)
    {
        var input = new BedrockNativeGroundSpeedInput(true, true, BedrockNativePlayerMembership.PresentSameEntity, 255, bits, 0x3f000000U);
        AssertSelected(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(input), bits);
    }

    [Theory]
    [InlineData(0x00000000U)]
    [InlineData(0x80000000U)]
    [InlineData(0x00000001U)]
    [InlineData(0x7f800001U)]
    [InlineData(0x7fc12345U)]
    [InlineData(0x7f800000U)]
    [InlineData(0xff800000U)]
    [InlineData(0xffffffffU)]
    public void SpeedFallbackCopiesEverySuppliedPayloadBit(uint bits)
    {
        var input = new BedrockNativeGroundSpeedInput(true, true, BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, null, null, bits);
        AssertSelected(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(input), bits);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, true)]
    [InlineData(true, null)]
    [InlineData(true, false)]
    [InlineData(null, null)]
    [InlineData(false, false)]
    public void BothRequiredRowsMustBeKnownPresentEvenForUnreadFields(bool? attributesPresent, bool? speedPresent)
    {
        var input = new BedrockNativeGroundSpeedInput(attributesPresent, speedPresent, BedrockNativePlayerMembership.PresentSameEntity, 0, null, null);
        AssertUnavailable(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(input));
        input = input with { PlayerMembership = BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, SpeedFirstFloatBits = 0x3f000000U };
        AssertUnavailable(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(input));
    }

    [Theory]
    [InlineData((int)BedrockNativePlayerMembership.Unknown)]
    [InlineData(99)]
    public void UnresolvedMembershipDoesNotBecomeConfirmedAbsence(int membership)
    {
        var input = new BedrockNativeGroundSpeedInput(true, true, (BedrockNativePlayerMembership)membership, 1, 0x3e800000U, 0x3f000000U);
        AssertUnavailable(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(input));
    }

    [Theory]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)1, null, 0x3f000000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, null, 0x3e800000U, 0x3f000000U)]
    [InlineData((int)BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, (byte)0, 0x3e800000U, null)]
    public void UnknownSelectedValueOrPlayerEngagementRefuses(int membership, byte? engagement, uint? attributes, uint? speed)
    {
        var input = new BedrockNativeGroundSpeedInput(true, true, (BedrockNativePlayerMembership)membership, engagement, attributes, speed);
        AssertUnavailable(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(input));
    }

    [Fact]
    public void DefaultInputAndResultRemainUnavailable()
    {
        AssertUnavailable(BedrockNativeGroundSpeedSelector.SelectBaseSpeedBits(default));
        AssertUnavailable(default);
    }

    private static void AssertSelected(BedrockNativeArithmeticResult<uint> result, uint expected)
    {
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, result.Disposition);
        Assert.True(result.Value.HasValue);
        Assert.Equal(expected, result.Value.GetValueOrDefault());
    }

    private static void AssertUnavailable(BedrockNativeArithmeticResult<uint> result)
    {
        Assert.Equal(BedrockNativeArithmeticDisposition.MissingNativeInputs, result.Disposition);
        Assert.False(result.Value.HasValue);
    }
}
