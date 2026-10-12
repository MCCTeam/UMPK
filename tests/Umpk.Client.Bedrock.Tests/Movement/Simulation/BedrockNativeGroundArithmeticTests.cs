using Umpk.Client.Bedrock.Movement.Simulation;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Movement.Simulation;

public sealed class BedrockNativeGroundArithmeticTests
{
    // Literal artificial arithmetic fixtures from the independently qualified 22 + 7 yaw + 4 full-entry cohorts; no game, attribute-default or CRT parity claim.
    [Theory]
    [MemberData(nameof(IntentVectors))]
    public void IntentXYZMatchesIndependentBits(string id, uint[] input, uint[] expected)
    {
        var actual = BedrockNativeGroundArithmetic.ScaleIntentXYZ(Vector3(input));
        AssertComputed3(id, actual, expected);
    }

    [Theory]
    [MemberData(nameof(YawVectors))]
    public void RoundedYawMatchesIndependentBits(string id, uint input, uint expected)
    {
        var actual = BedrockNativeGroundArithmetic.PrepareYawRadians(F(input));
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, actual.Disposition);
        Assert.True(actual.Value.HasValue, id);
        Assert.Equal(expected, Bits(actual.Value.GetValueOrDefault()));
    }

    [Theory]
    [MemberData(nameof(GroundVectors))]
    public void FullEntryGroundDeltaMatchesIndependentBits(string id, uint[] intent, uint[] prior, uint speed, uint sine, uint cosine, uint[] expected)
    {
        var actual = BedrockNativeGroundArithmetic.AccumulateGroundDelta(Vector3(intent), Vector3(prior), F(speed), ResolvedBasis(sine, cosine));
        AssertComputed3(id, actual, expected);
    }

    [Theory]
    [MemberData(nameof(FrictionVectors))]
    public void OrdinaryFrictionXZMatchesIndependentBits(string id, uint[] prior, uint friction, bool engaged30, uint value30, bool engaged40, uint value40, uint[] expected)
    {
        var input = new BedrockNativeOrdinaryFrictionInput(F(friction), new(engaged30, F(value30)), new(engaged40, F(value40)), OrdinaryBranch());
        var actual = BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(Vector2(prior), input);
        AssertComputed2(id, actual, expected);
    }

    [Fact]
    public void DefaultResultAndBasisAreUnavailable()
    {
        AssertRefused(default(BedrockNativeArithmeticResult<BedrockNativeFloat3>), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        Assert.Equal(BedrockNativeRotationBasisAvailability.Unknown, default(BedrockNativeRotationBasis).Availability);
    }

    [Fact]
    public void ResultFactoriesSeparateValueFromRefusal()
    {
        var success = BedrockNativeArithmeticResult<float>.FromValue(F(0x80000000U));
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, success.Disposition);
        Assert.True(success.Value.HasValue);
        Assert.Equal(0x80000000U, Bits(success.Value.GetValueOrDefault()));
        AssertRefused(BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.MissingNativeInputs), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.UnsupportedBranch), BedrockNativeArithmeticDisposition.UnsupportedBranch);
        AssertRefused(BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        Assert.Throws<ArgumentOutOfRangeException>(() => BedrockNativeArithmeticResult<float>.Refused(BedrockNativeArithmeticDisposition.Computed));
        Assert.Throws<ArgumentOutOfRangeException>(() => BedrockNativeArithmeticResult<float>.Refused((BedrockNativeArithmeticDisposition)99));
    }

    [Fact]
    public void MissingRowsReturnNoFabricatedValue()
    {
        AssertRefused(BedrockNativeGroundArithmetic.ScaleIntentXYZ(null), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.PrepareYawRadians(null), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(null, new(0f, 0f, 0f), 1f, ZeroBasis()), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), null, 1f, ZeroBasis()), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, 0f, 0f), null, ZeroBasis()), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, 0f, 0f), 1f, null), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(null, FrictionInput()), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, -1f), null), BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Fact]
    public void UnknownBasisDoesNotBecomeZeroYaw()
    {
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, 0f, 0f), 1f, default(BedrockNativeRotationBasis)), BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Fact]
    public void UnsupportedBasisDoesNotChooseManagedTrig()
    {
        var basis = new BedrockNativeRotationBasis(0f, 1f, BedrockNativeRotationBasisAvailability.Unsupported);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, 0f, 0f), 1f, basis), BedrockNativeArithmeticDisposition.UnsupportedBranch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachUnknownOrdinaryPredicateRefuses(int index)
    {
        var branch = index switch
        {
            0 => OrdinaryBranch() with { VexPresent = null },
            1 => OrdinaryBranch() with { OverridePresent = null },
            2 => OrdinaryBranch() with { ActorFlags8NonNegative = null },
            _ => OrdinaryBranch() with { AppliesVertical = null }
        };
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, -1f), FrictionInput() with { Branch = branch }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachUnsupportedOrdinaryPredicateRefuses(int index)
    {
        var branch = index switch
        {
            0 => OrdinaryBranch() with { VexPresent = true },
            1 => OrdinaryBranch() with { OverridePresent = true },
            2 => OrdinaryBranch() with { ActorFlags8NonNegative = false },
            _ => OrdinaryBranch() with { AppliesVertical = true }
        };
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, -1f), FrictionInput() with { Branch = branch }), BedrockNativeArithmeticDisposition.UnsupportedBranch);
    }

    [Fact]
    public void MissingFrictionAndUnknownOrEngagedMissingModifiersRefuse()
    {
        var delta = new BedrockNativeFloat2(1f, -1f);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(delta, FrictionInput() with { AuxiliaryFriction = null }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(delta, FrictionInput() with { Modifier30 = new(null, 1f) }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(delta, FrictionInput() with { Modifier40 = new(null, 1f) }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(delta, FrictionInput() with { Modifier30 = new(true, null) }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(delta, FrictionInput() with { Modifier40 = new(true, null) }), BedrockNativeArithmeticDisposition.MissingNativeInputs);
    }

    [Fact]
    public void ProvenDisengagementIgnoresAbsentAndIrrelevantInvalidCoefficients()
    {
        var delta = new BedrockNativeFloat2(F(0x3e800000U), F(0xbf000000U));
        var input = FrictionInput() with { Modifier30 = new(false, null), Modifier40 = new(false, float.NaN) };
        AssertComputed2("qualified friction-disengaged", BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(delta, input), [0x3e0bc6a9U, 0xbe8bc6a9U]);
        input = input with { Modifier30 = new(false, float.PositiveInfinity), Modifier40 = new(false, null) };
        AssertComputed2("qualified friction-disengaged", BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(delta, input), [0x3e0bc6a9U, 0xbe8bc6a9U]);
    }

    [Theory]
    [InlineData(0x7fc00000U)]
    [InlineData(0x7f800000U)]
    [InlineData(0xff800000U)]
    public void RelevantNonfiniteArithmeticValuesThrow(uint bits)
    {
        float invalid = F(bits);
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.ScaleIntentXYZ(new(invalid, 0f, 0f)));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.PrepareYawRadians(invalid));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, invalid, 0f), new(0f, 0f, 0f), 1f, ZeroBasis()));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, invalid, 0f), 1f, ZeroBasis()));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, 0f, 0f), invalid, ZeroBasis()));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, 0f, 0f), 1f, new(invalid, 1f, BedrockNativeRotationBasisAvailability.ResolvedArithmeticBits)));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(invalid, 1f), FrictionInput()));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, 1f), FrictionInput() with { AuxiliaryFriction = invalid }));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, 1f), FrictionInput() with { Modifier30 = new(true, invalid) }));
        Assert.Throws<ArgumentException>(() => BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, 1f), FrictionInput() with { Modifier40 = new(true, invalid) }));
    }

    [Theory]
    [InlineData(0x00000001U)]
    [InlineData(0x007fffffU)]
    [InlineData(0x80000001U)]
    public void SubnormalInputsRefuseTheUnqualifiedFlushSubset(uint bits)
    {
        float subnormal = F(bits);
        AssertRefused(BedrockNativeGroundArithmetic.ScaleIntentXYZ(new(1f, subnormal, 0f)), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.PrepareYawRadians(subnormal), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, subnormal, 0f), new(0f, 0f, 0f), 1f, ZeroBasis()), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(subnormal, 1f), FrictionInput()), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
    }

    [Fact]
    public void NormalInputsThatEnterSubnormalOrUnderflowIntermediatesRefuse()
    {
        float minimumNormal = F(0x00800000U);
        AssertRefused(BedrockNativeGroundArithmetic.ScaleIntentXYZ(new(1f, minimumNormal, 0f)), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.PrepareYawRadians(minimumNormal), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(minimumNormal, 0f, 0f), new(0f, 0f, 0f), 1f, ZeroBasis()), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 1f), new(0f, 0f, 0f), minimumNormal, ZeroBasis()), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, -1f), FrictionInput() with { Modifier40 = new(true, minimumNormal) }), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
    }

    [Fact]
    public void FiniteNegativeSelectedScalarsDoNotBecomeUnsupportedProfiles()
    {
        var actual = BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(0f, 0f, 0f), -1f, ZeroBasis());
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, actual.Disposition);
        Assert.True(actual.Value.HasValue);
        var friction = BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(1f, -1f), FrictionInput() with { AuxiliaryFriction = -1f });
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, friction.Disposition);
        Assert.True(friction.Value.HasValue);
    }

    [Fact]
    public void LengthRotationAdditionAndFrictionOverflowRefuseAtomically()
    {
        float maximum = F(0x7f7fffffU);
        var prior = new BedrockNativeFloat3(F(0x80000000U), 0.25f, -0.5f);
        uint[] before = [Bits(prior.X), Bits(prior.Y), Bits(prior.Z)];
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(maximum, 0f, 0f), prior, 1f, ZeroBasis()), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), prior, maximum, ResolvedBasis(0U, 0x40000000U)), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        AssertRefused(BedrockNativeGroundArithmetic.AccumulateGroundDelta(new(1f, 0f, 0f), new(maximum, prior.Y, prior.Z), maximum, ZeroBasis()), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        var friction = FrictionInput() with { AuxiliaryFriction = -maximum, Modifier30 = new(true, maximum) };
        AssertRefused(BedrockNativeGroundArithmetic.ComputeOrdinaryGroundFrictionXZ(new(prior.X, prior.Z), friction), BedrockNativeArithmeticDisposition.UnrepresentableFiniteSubset);
        Assert.Equal(before, new[] { Bits(prior.X), Bits(prior.Y), Bits(prior.Z) });
    }

    public static IEnumerable<object[]> IntentVectors()
    {
        yield return ["intent-unit-forward", (uint[])[0x00000000U, 0x00000000U, 0x3f800000U], (uint[])[0x00000000U, 0x00000000U, 0x3f7ae148U]];
        yield return ["intent-retained-y", (uint[])[0x3f000000U, 0x3e800000U, 0xbf000000U], (uint[])[0x3efae148U, 0x3e7ae148U, 0xbefae148U]];
        yield return ["intent-signed-zero", (uint[])[0x80000000U, 0x00000000U, 0x80000000U], (uint[])[0x80000000U, 0x00000000U, 0x80000000U]];
        yield return ["intent-nonexact-products", (uint[])[0x3eaaaaabU, 0x3dcccccdU, 0xbe4ccccdU], (uint[])[0x3ea740dbU, 0x3dc8b43aU, 0xbe48b43aU]];
        yield return ["intent-large-finite", (uint[])[0x41000000U, 0xc0800000U, 0x40000000U], (uint[])[0x40fae148U, 0xc07ae148U, 0x3ffae148U]];
    }

    public static IEnumerable<object[]> YawVectors()
    {
        yield return ["yaw-positive-one", 0x3f800000U, 0x3c8efa35U];
        yield return ["yaw-negative-one", 0xbf800000U, 0xbc8efa35U];
        yield return ["yaw-positive-quarter-turn-scalar", 0x42b40000U, 0x3fc90fdbU];
        yield return ["yaw-negative-quarter-turn-scalar", 0xc2b40000U, 0xbfc90fdbU];
        yield return ["yaw-positive-half-turn-scalar", 0x43340000U, 0x40490fdbU];
        yield return ["yaw-next-above-one", 0x3f800001U, 0x3c8efa36U];
        yield return ["yaw-negative-fraction", 0xbeaaaaabU, 0xbbbea2f2U];
    }

    public static IEnumerable<object[]> GroundVectors()
    {
        yield return ["ground-zero-skip", (uint[])[0x00000000U, 0x00000000U, 0x00000000U], (uint[])[0x80000000U, 0x3e800000U, 0x3f000000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x80000000U, 0x3e800000U, 0x3f000000U]];
        yield return ["ground-below-threshold", (uint[])[0x3c23d709U, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x00000000U, 0x00000000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3e800000U, 0x00000000U, 0x00000000U]];
        yield return ["ground-equal-threshold", (uint[])[0x3c23d70aU, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x00000000U, 0x00000000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3e80a3d7U, 0x00000000U, 0x00000000U]];
        yield return ["ground-above-threshold", (uint[])[0x3c23d70bU, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x00000000U, 0x00000000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3e80a3d7U, 0x00000000U, 0x00000000U]];
        yield return ["ground-subunit-diagonal", (uint[])[0x3f000000U, 0x00000000U, 0x3f000000U], (uint[])[0x3e800000U, 0xbf000000U, 0x3f400000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3ea00000U, 0xbf000000U, 0x3f500000U]];
        yield return ["ground-overunit-345", (uint[])[0x40400000U, 0x40800000U, 0x00000000U], (uint[])[0x3e800000U, 0xbf000000U, 0x3f400000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3ea66666U, 0xbecccccdU, 0x3f400000U]];
        yield return ["ground-length-association", (uint[])[0x3f800000U, 0x39800000U, 0x39800000U], (uint[])[0x00000000U, 0x00000000U, 0x00000000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3e000000U, 0x38000000U, 0x38000000U]];
        yield return ["ground-zero-selected-speed", (uint[])[0x3f800000U, 0x00000000U, 0x00000000U], (uint[])[0x3e800000U, 0x80000000U, 0xbf000000U], 0x00000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3e800000U, 0x00000000U, 0xbf000000U]];
        yield return ["ground-overunit-sqrt2", (uint[])[0x3f800000U, 0x00000000U, 0x3f800000U], (uint[])[0x00000000U, 0x00000000U, 0x00000000U], 0x3e000000U, 0x00000000U, 0x3f800000U, (uint[])[0x3db504f3U, 0x00000000U, 0x3db504f3U]];
        yield return ["rotation-positive-sine-basis", (uint[])[0x3f000000U, 0x3e000000U, 0xbe800000U], (uint[])[0xbe000000U, 0x00000000U, 0x3f800000U], 0x3f800000U, 0x3f800000U, 0x00000000U, (uint[])[0x3e000000U, 0x3e000000U, 0x3fc00000U]];
        yield return ["rotation-negative-sine-basis", (uint[])[0x3f000000U, 0x3e000000U, 0xbe800000U], (uint[])[0xbe000000U, 0x00000000U, 0x3f800000U], 0x3f800000U, 0xbf800000U, 0x00000000U, (uint[])[0xbec00000U, 0x3e000000U, 0x3f000000U]];
        yield return ["rotation-nonunit-rounded-basis", (uint[])[0x3eaaaaabU, 0xbdcccccdU, 0x3e4ccccdU], (uint[])[0x3d000000U, 0xbe800000U, 0xbf000000U], 0x3f800000U, 0x3f19999aU, 0x3f4ccccdU, (uint[])[0x3e362fcaU, 0xbeb33333U, 0xbe0f5c28U]];
        yield return ["rotation-separate-product-rounding", (uint[])[0x3f800001U, 0xbe000000U, 0x3f800000U], (uint[])[0x80000000U, 0x00000000U, 0x00000000U], 0x3fb5b99fU, 0x3f800000U, 0x3f7fffffU, (uint[])[0x00000000U, 0xbe000000U, 0x40000000U]];
    }

    public static IEnumerable<object[]> FrictionVectors()
    {
        yield return ["friction-disengaged", (uint[])[0x3e800000U, 0xbf000000U], 0x3f19999aU, false, 0x3f800000U, false, 0x3f800000U, (uint[])[0x3e0bc6a9U, 0xbe8bc6a9U]];
        yield return ["friction-modifier-half", (uint[])[0x3e800000U, 0xbf000000U], 0x3f19999aU, true, 0x3f000000U, false, 0x3f800000U, (uint[])[0x3e3a5e36U, 0xbeba5e36U]];
        yield return ["friction-first-high-clamp", (uint[])[0x3e800000U, 0xbf000000U], 0x3f19999aU, true, 0x40800000U, false, 0x3f800000U, (uint[])[0x00000000U, 0x80000000U]];
        yield return ["friction-first-negative-clamp", (uint[])[0x3e800000U, 0xbf000000U], 0x3f19999aU, true, 0xbf800000U, false, 0x3f800000U, (uint[])[0x3e68f5c3U, 0xbee8f5c3U]];
        yield return ["friction-second-high-clamp", (uint[])[0x3e800000U, 0xbf000000U], 0x3f19999aU, false, 0x3f800000U, true, 0x41800000U, (uint[])[0x00000000U, 0x80000000U]];
        yield return ["friction-epsilon-equal-signed", (uint[])[0x34000000U, 0xb4000000U], 0x3f19999aU, false, 0x3f800000U, false, 0x3f800000U, (uint[])[0x00000000U, 0x80000000U]];
        yield return ["friction-epsilon-above", (uint[])[0x34000001U, 0xb4000001U], 0x3f19999aU, false, 0x3f800000U, false, 0x3f800000U, (uint[])[0x338bc6aaU, 0xb38bc6aaU]];
        yield return ["friction-output-below-epsilon-retained", (uint[])[0x34000001U, 0xb4000001U], 0x3c23d70aU, false, 0x3f800000U, false, 0x3f800000U, (uint[])[0x30951823U, 0xb0951823U]];
    }

    private static BedrockNativeRotationBasis ResolvedBasis(uint sine, uint cosine) => new(F(sine), F(cosine), BedrockNativeRotationBasisAvailability.ResolvedArithmeticBits);

    private static BedrockNativeRotationBasis ZeroBasis() => ResolvedBasis(0U, 0x3f800000U);

    private static BedrockNativeOrdinaryFrictionBranch OrdinaryBranch() => new(false, false, true, false);

    private static BedrockNativeOrdinaryFrictionInput FrictionInput() => new(F(0x3f19999aU), new(false, null), new(false, null), OrdinaryBranch());

    private static float F(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));

    private static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    private static BedrockNativeFloat3 Vector3(uint[] bits) => new(F(bits[0]), F(bits[1]), F(bits[2]));

    private static BedrockNativeFloat2 Vector2(uint[] bits) => new(F(bits[0]), F(bits[1]));

    private static void AssertComputed3(string id, BedrockNativeArithmeticResult<BedrockNativeFloat3> result, uint[] expected)
    {
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, result.Disposition);
        Assert.True(result.Value.HasValue, id);
        var value = result.Value.GetValueOrDefault();
        Assert.Equal(expected, new[] { Bits(value.X), Bits(value.Y), Bits(value.Z) });
    }

    private static void AssertComputed2(string id, BedrockNativeArithmeticResult<BedrockNativeFloat2> result, uint[] expected)
    {
        Assert.Equal(BedrockNativeArithmeticDisposition.Computed, result.Disposition);
        Assert.True(result.Value.HasValue, id);
        var value = result.Value.GetValueOrDefault();
        Assert.Equal(expected, new[] { Bits(value.X), Bits(value.Z) });
    }

    private static void AssertRefused<T>(BedrockNativeArithmeticResult<T> result, BedrockNativeArithmeticDisposition expected) where T : struct
    {
        Assert.Equal(expected, result.Disposition);
        Assert.False(result.Value.HasValue);
    }
}
