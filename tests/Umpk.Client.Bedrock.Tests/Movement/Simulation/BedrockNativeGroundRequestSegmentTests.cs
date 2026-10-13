using Umpk.Client.Bedrock.Movement.Simulation;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Movement.Simulation;

public sealed class BedrockNativeGroundRequestSegmentTests
{
    // Artificial source-entry bits from the independent rational instruction oracle; no gameplay values, trig, clock or native execution.
    [Theory]
    [MemberData(nameof(InstructionGoldens))]
    public void WholeEntryMatchesIndependentInstructionGoldens(string id, uint[] intent, uint[] prior, uint[] request, uint baseSpeed, uint friction, uint sine, uint cosine, uint expectedSpeed, uint[] expectedDelta, uint[] expectedRequest, uint[] expectedSaved)
    {
        var input = new BedrockNativeGroundRequestInput(Vector(intent), Vector(prior), Vector(request), PlayerSpeed(baseSpeed), F(friction), Basis(sine, cosine));
        BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue> result = BedrockNativeGroundRequestSegment.Compute(input);
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, result.Disposition);
        Assert.True(result.Value.HasValue, id);
        var value = result.Value.GetValueOrDefault();
        Assert.Equal(expectedSpeed, Bits(value.SelectedTravelSpeed));
        Assert.Equal(expectedDelta, Bits(value.AcceleratedDelta));
        Assert.Equal(expectedRequest, Bits(value.AccumulatedRequest));
        Assert.Equal(expectedSaved, new[] { Bits(value.SavedDeltaXZ.X), Bits(value.SavedDeltaXZ.Z) });
    }

    [Fact]
    public void SavedDeltaIsAcceleratedBeforeRequestAdditionWithoutFrictionOrSecondsConversion()
    {
        var input = Input() with { PriorDelta = new(0.5f, 0.125f, -0.5f), ExistingRequest = new(0.25f, -0.125f, 0.75f) };
        var result = BedrockNativeGroundRequestSegment.Compute(input);
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, result.Disposition);
        Assert.True(result.Value.HasValue);
        var value = result.Value.GetValueOrDefault();
        Assert.Equal(new uint[] { 0x3f400000U, 0x3e000000U, 0xbf000000U }, Bits(value.AcceleratedDelta));
        Assert.Equal(new uint[] { 0x3f800000U, 0x00000000U, 0x3e800000U }, Bits(value.AccumulatedRequest));
        Assert.Equal(0x3f400000U, Bits(value.SavedDeltaXZ.X));
        Assert.Equal(0xbf000000U, Bits(value.SavedDeltaXZ.Z));
        Assert.Equal(new uint[] { 0x3f000000U, 0x3e000000U, 0xbf000000U }, Bits(input.PriorDelta.GetValueOrDefault()));
        Assert.Equal(new uint[] { 0x3e800000U, 0xbe000000U, 0x3f400000U }, Bits(input.ExistingRequest.GetValueOrDefault()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryRequiredUnknownInputRefusesWithoutPartialPublication(int field)
    {
        var input = field switch
        {
            0 => Input() with { PostIntent = null },
            1 => Input() with { PriorDelta = null },
            2 => Input() with { ExistingRequest = null },
            3 => Input() with { SpeedInput = null },
            4 => Input() with { ResolvedEffectiveFriction = null },
            _ => Input() with { Basis = null }
        };
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(input), BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Fact]
    public void DefaultInputAndUnknownBasisDoNotChooseNativeDefaults()
    {
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(default), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(Input() with { Basis = default(BedrockNativeRotationBasis) }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(default, BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Theory]
    [InlineData((int)BedrockNativeRotationBasisAvailability.Unsupported)]
    [InlineData(99)]
    public void UnsupportedBasisDoesNotSelectManagedTrig(int availability)
    {
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(Input() with { Basis = new(0f, 1f, (BedrockNativeRotationBasisAvailability)availability) }), BedrockNativeArithmeticDisposition.UnsupportedBranch);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, true)]
    [InlineData(true, null)]
    [InlineData(true, false)]
    public void RequiredRowsCannotBeSkippedEvenWhenTheirFloatIsUnread(bool? attributes, bool? speed)
    {
        var source = new BedrockNativeGroundSpeedInput(attributes, speed, BedrockNativePlayerMembership.PresentSameEntity, 0, null, null);
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(Input() with { SpeedInput = source }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UnknownSelectedFieldOrMembershipRefuses(int choice)
    {
        var source = choice switch
        {
            0 => PlayerSpeed(0x3e800000U) with { PlayerMembership = BedrockNativePlayerMembership.Unknown },
            1 => PlayerSpeed(0x3e800000U) with { PlayerMembership = (BedrockNativePlayerMembership)99 },
            2 => PlayerSpeed(0x3e800000U) with { AttributesEngagement = null },
            3 => PlayerSpeed(0x3e800000U) with { AttributesFirstFloatBits = null },
            _ => PlayerSpeed(0x3e800000U) with { PlayerMembership = BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, SpeedFirstFloatBits = null }
        };
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(Input() with { SpeedInput = source }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Theory]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)255, 0x3e800000U, 0x7fc12345U, 0x3e800000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)2, 0x3e800000U, 0x00000001U, 0x3e800000U)]
    [InlineData((int)BedrockNativePlayerMembership.PresentSameEntity, (byte)0, 0x7fc12345U, 0x7f800000U, 0x00000000U)]
    [InlineData((int)BedrockNativePlayerMembership.AbsentOrInvalidSameDomain, null, 0x7fc12345U, 0x3f000000U, 0x3f000000U)]
    public void WholeSegmentUsesQualifiedSpeedChoiceAndIgnoresUnreadRawPayloads(int membership, byte? engagement, uint attributes, uint fallback, uint expectedSpeed)
    {
        var source = new BedrockNativeGroundSpeedInput(true, true, (BedrockNativePlayerMembership)membership, engagement, attributes, fallback);
        var result = BedrockNativeGroundRequestSegment.Compute(Input() with { SpeedInput = source });
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, result.Disposition);
        Assert.True(result.Value.HasValue);
        Assert.Equal(expectedSpeed, Bits(result.Value.GetValueOrDefault().SelectedTravelSpeed));
    }

    [Theory]
    [MemberData(nameof(NonfiniteFields))]
    public void ReachedRelevantNonfiniteInputThrowsInsteadOfPublishing(uint bits, int field)
    {
        var input = WithField(Input(), F(bits), field);
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundRequestSegment.Compute(input));
    }

    [Theory]
    [InlineData(0x00000001U)]
    [InlineData(0x007fffffU)]
    [InlineData(0x80000001U)]
    public void RelevantSubnormalInputsRefuseUnqualifiedFlushBehavior(uint bits)
    {
        for (int field = 0; field < 7; field++)
        {
            AssertRefused(BedrockNativeGroundRequestSegment.Compute(WithField(Input(), F(bits), field)), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        }
    }

    [Theory]
    [InlineData(0x00000000U)]
    [InlineData(0x80000000U)]
    [InlineData(0x7f7fffffU)]
    [InlineData(0x55000000U)]
    [InlineData(0x5d800000U)]
    [InlineData(0x00800000U)]
    public void ResolvedFactorThatCannotProduceFiniteQualifiedCubeRefuses(uint bits)
    {
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(Input() with { ResolvedEffectiveFriction = F(bits) }), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
    }

    [Fact]
    public void BaseSpeedProductAndLaterRequestFailuresCannotLeakEarlierResults()
    {
        var input = Input() with { SpeedInput = PlayerSpeed(0x00800000U), ResolvedEffectiveFriction = 1f, PostIntent = new(0f, 0f, 0f) };
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(input), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        input = Input() with { SpeedInput = PlayerSpeed(0x7f7fffffU), ResolvedEffectiveFriction = F(0x3e8bc6a9U) };
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(input), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        input = Input() with { PostIntent = new(0f, 0f, 0f), PriorDelta = new(float.MaxValue, 0f, 0f), ExistingRequest = new(float.MaxValue, 0f, 0f) };
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(input), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        input = Input() with { PostIntent = new(0f, 0f, 0f), PriorDelta = new(F(0x00800000U), 0f, 0f), ExistingRequest = new(F(0x80800001U), 0f, 0f) };
        AssertRefused(BedrockNativeGroundRequestSegment.Compute(input), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
    }

    public static IEnumerable<object[]> InstructionGoldens()
    {
        yield return ["ratio-then-momentum-and-existing-request", (uint[])[0x00000000U, 0x00000000U, 0x3f800000U], (uint[])[0x00000000U, 0x00000000U, 0x3f000000U], (uint[])[0x3e800000U, 0x3e000000U, 0x3e800000U], 0x3e000000U, 0x3f800000U, 0x00000000U, 0x3f800000U, 0x3ca6ad8bU, (uint[])[0x00000000U, 0x00000000U, 0x3f05356cU], (uint[])[0x3e800000U, 0x3e000000U, 0x3f45356cU], (uint[])[0x00000000U, 0x3f05356cU]];
        yield return ["below-threshold-preserves-prior", (uint[])[0x3c23d709U, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x00000000U, 0x80000000U], (uint[])[0x3f000000U, 0x80000000U, 0x80000000U], 0x3e000000U, 0x3f0bc6a9U, 0x00000000U, 0x3f800000U, 0x3e000000U, (uint[])[0x3e800000U, 0x00000000U, 0x80000000U], (uint[])[0x3f400000U, 0x00000000U, 0x80000000U], (uint[])[0x3e800000U, 0x80000000U]];
        yield return ["equal-threshold-accelerates", (uint[])[0x3c23d70aU, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x00000000U, 0x80000000U], (uint[])[0x3f000000U, 0x80000000U, 0x80000000U], 0x3e000000U, 0x3f0bc6a9U, 0x00000000U, 0x3f800000U, 0x3e000000U, (uint[])[0x3e80a3d7U, 0x00000000U, 0x00000000U], (uint[])[0x3f4051ecU, 0x00000000U, 0x00000000U], (uint[])[0x3e80a3d7U, 0x00000000U]];
        yield return ["above-threshold-accelerates", (uint[])[0x3c23d70bU, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x00000000U, 0x80000000U], (uint[])[0x3f000000U, 0x80000000U, 0x80000000U], 0x3e000000U, 0x3f0bc6a9U, 0x00000000U, 0x3f800000U, 0x3e000000U, (uint[])[0x3e80a3d7U, 0x00000000U, 0x00000000U], (uint[])[0x3f4051ecU, 0x00000000U, 0x00000000U], (uint[])[0x3e80a3d7U, 0x00000000U]];
        yield return ["retained-y-controls-full-normalization", (uint[])[0x40400000U, 0x40800000U, 0x00000000U], (uint[])[0x3e800000U, 0xbf000000U, 0x3f400000U], (uint[])[0xbf000000U, 0x3e000000U, 0x3f800000U], 0x3e000000U, 0x3f0bc6a9U, 0x00000000U, 0x3f800000U, 0x3e000000U, (uint[])[0x3ea66666U, 0xbecccccdU, 0x3f400000U], (uint[])[0xbe333334U, 0xbe8ccccdU, 0x3fe00000U], (uint[])[0x3ea66666U, 0x3f400000U]];
        yield return ["signed-zero-skip-and-request-add", (uint[])[0x00000000U, 0x80000000U, 0x00000000U], (uint[])[0x80000000U, 0x80000000U, 0x80000000U], (uint[])[0x80000000U, 0x00000000U, 0x80000000U], 0x3e000000U, 0x3f800000U, 0x00000000U, 0x3f800000U, 0x3ca6ad8bU, (uint[])[0x80000000U, 0x80000000U, 0x80000000U], (uint[])[0x80000000U, 0x00000000U, 0x80000000U], (uint[])[0x80000000U, 0x80000000U]];
        yield return ["negative-resolved-friction-is-arithmetic", (uint[])[0x3f800000U, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x3e000000U, 0x3f800000U], (uint[])[0x00000000U, 0x00000000U, 0x00000000U], 0x3e000000U, 0xbf0bc6a9U, 0x00000000U, 0x3f800000U, 0xbe000000U, (uint[])[0x3e000000U, 0x3e000000U, 0x3f800000U], (uint[])[0x3e000000U, 0x3e000000U, 0x3f800000U], (uint[])[0x3e000000U, 0x3f800000U]];
        yield return ["negative-selected-base-is-arithmetic", (uint[])[0x3f800000U, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x3e000000U, 0x3f800000U], (uint[])[0x00000000U, 0x00000000U, 0x00000000U], 0xbe000000U, 0x3f0bc6a9U, 0x00000000U, 0x3f800000U, 0xbe000000U, (uint[])[0x3e000000U, 0x3e000000U, 0x3f800000U], (uint[])[0x3e000000U, 0x3e000000U, 0x3f800000U], (uint[])[0x3e000000U, 0x3f800000U]];
        yield return ["supplied-sine-preserves-prior-and-request", (uint[])[0x3f000000U, 0x3e000000U, 0xbe800000U], (uint[])[0xbe000000U, 0x00000000U, 0x3f800000U], (uint[])[0x3e800000U, 0xbf000000U, 0x3f000000U], 0x3f800000U, 0x3f0bc6a9U, 0x3f800000U, 0x00000000U, 0x3f800000U, (uint[])[0x3e000000U, 0x3e000000U, 0x3fc00000U], (uint[])[0x3ec00000U, 0xbec00000U, 0x40000000U], (uint[])[0x3e000000U, 0x3fc00000U]];
        yield return ["nonunit-supplied-basis-remains-explicit", (uint[])[0x3eaaaaabU, 0xbdcccccdU, 0x3e4ccccdU], (uint[])[0x3d000000U, 0xbe800000U, 0xbf000000U], (uint[])[0x3e800000U, 0x3e000000U, 0x3f000000U], 0x3f19999aU, 0x3f68f5c3U, 0x3f19999aU, 0x3f4ccccdU, 0x3e04b5deU, (uint[])[0x3d4ddb56U, 0xbe86a2b2U, 0xbee81cb4U], (uint[])[0x3e99bb6bU, 0xbe0d4564U, 0x3d3f1a60U], (uint[])[0x3d4ddb56U, 0xbee81cb4U]];
        yield return ["separate-products-not-fused", (uint[])[0x3f800001U, 0xbe000000U, 0x3f800000U], (uint[])[0x80000000U, 0x00000000U, 0x00000000U], (uint[])[0x80000000U, 0x00000000U, 0x00000000U], 0x3fb5b99fU, 0x3f0bc6a9U, 0x3f800000U, 0x3f7fffffU, 0x3fb5b99fU, (uint[])[0x00000000U, 0xbe000000U, 0x40000000U], (uint[])[0x00000000U, 0xbe000000U, 0x40000000U], (uint[])[0x00000000U, 0x40000000U]];
        yield return ["negative-zero-selected-speed", (uint[])[0x3f800000U, 0x00000000U, 0x00000000U], (uint[])[0x80000000U, 0x80000000U, 0x80000000U], (uint[])[0x80000000U, 0x80000000U, 0x80000000U], 0x80000000U, 0x3f0bc6a9U, 0x00000000U, 0x3f800000U, 0x80000000U, (uint[])[0x00000000U, 0x80000000U, 0x80000000U], (uint[])[0x00000000U, 0x80000000U, 0x80000000U], (uint[])[0x00000000U, 0x80000000U]];
        yield return ["base-multiply-after-cube", (uint[])[0x3f800000U, 0x00000000U, 0x00000000U], (uint[])[0x00000000U, 0x00000000U, 0x00000000U], (uint[])[0x00000000U, 0x00000000U, 0x00000000U], 0x3eaaaaabU, 0x3f800000U, 0x00000000U, 0x3f800000U, 0x3d5e3cbaU, (uint[])[0x3d5e3cbaU, 0x00000000U, 0x00000000U], (uint[])[0x3d5e3cbaU, 0x00000000U, 0x00000000U], (uint[])[0x3d5e3cbaU, 0x00000000U]];
    }

    public static IEnumerable<object[]> NonfiniteFields()
    {
        foreach (uint bits in new uint[] { 0x7fc00000U, 0x7f800000U, 0xff800000U })
        {
            for (int field = 0; field < 7; field++)
            {
                yield return [bits, field];
            }
        }
    }

    private static BedrockNativeGroundRequestInput Input() => new(new(1f, 0f, 0f), new(0f, 0f, 0f), new(0f, 0f, 0f), PlayerSpeed(0x3e800000U), F(0x3f0bc6a9U), Basis(0U, 0x3f800000U));

    private static BedrockNativeGroundSpeedInput PlayerSpeed(uint bits) => new(true, true, BedrockNativePlayerMembership.PresentSameEntity, 1, bits, 0x3f000000U);

    private static BedrockNativeRotationBasis Basis(uint sine, uint cosine) => new(F(sine), F(cosine), BedrockNativeRotationBasisAvailability.ResolvedArithmeticBits);

    private static BedrockNativeGroundRequestInput WithField(BedrockNativeGroundRequestInput input, float value, int field) => field switch
    {
        0 => input with { ResolvedEffectiveFriction = value },
        1 => input with { SpeedInput = PlayerSpeed(Bits(value)) },
        2 => input with { PostIntent = new(1f, value, 0f) },
        3 => input with { PriorDelta = new(0f, 0f, value) },
        4 => input with { ExistingRequest = new(value, 0f, 0f) },
        5 => input with { Basis = new(value, 1f, BedrockNativeRotationBasisAvailability.ResolvedArithmeticBits) },
        _ => input with { Basis = new(0f, value, BedrockNativeRotationBasisAvailability.ResolvedArithmeticBits) }
    };

    private static BedrockNativeFloat3 Vector(uint[] bits) => new(F(bits[0]), F(bits[1]), F(bits[2]));

    private static float F(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));

    private static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    private static uint[] Bits(BedrockNativeFloat3 value) => [Bits(value.X), Bits(value.Y), Bits(value.Z)];

    private static void AssertRefused(BedrockNativeArithmeticResult<BedrockNativeGroundRequestValue> result, BedrockNativeArithmeticDisposition expected)
    {
        Assert.Equal(expected, result.Disposition);
        Assert.False(result.Value.HasValue);
    }
}
