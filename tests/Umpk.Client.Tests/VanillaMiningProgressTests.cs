using Umpk.Client.Actions;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaMiningProgressTests
{
    [Theory]
    [InlineData(1.5F, true, 1.0F, false, 0.006666667F)]
    [InlineData(1.5F, true, 2.0F, true, 0.044444446F)]
    [InlineData(1.5F, true, 8.0F, true, 0.17777778F)]
    [InlineData(0.5F, false, 1.0F, false, 0.06666667F)]
    [InlineData(-1.0F, false, 8.0F, true, 0.0F)]
    public void ProgressUsesVanillasHardnessAndHarvestDivisor(
        float hardness, bool requiresTool, float speed, bool correctTool, float expected)
    {
        float progress = VanillaMiningProgress.Compute(
            new BlockMiningInfo(hardness, requiresTool), new ToolMiningInfo(speed, correctTool),
            MiningModifiers.Grounded, exponentialFatigue: false);

        Assert.Equal(expected, progress);
    }

    [Theory]
    [InlineData(1.0F, false, 151)]
    [InlineData(2.0F, true, 23)]
    [InlineData(8.0F, true, 6)]
    public void MiningTicksRetainSinglePrecisionAccumulation(float speed, bool correct, int expectedTicks)
    {
        float step = VanillaMiningProgress.Compute(
            new BlockMiningInfo(1.5F, true), new ToolMiningInfo(speed, correct),
            MiningModifiers.Grounded, exponentialFatigue: false);
        float accumulated = 0;
        int ticks = 0;
        while (accumulated < 1.0F)
        {
            accumulated += step;
            ticks++;
        }

        Assert.Equal(expectedTicks, ticks);
    }

    [Fact]
    public void ZeroHardnessUsesVanillasFloatingPointDivision()
    {
        var block = new BlockMiningInfo(0.0F, false);
        var tool = new ToolMiningInfo(1.0F, false);

        Assert.True(float.IsPositiveInfinity(VanillaMiningProgress.Compute(block, tool, MiningModifiers.Grounded, false)));
        Assert.True(float.IsNaN(VanillaMiningProgress.Compute(block, tool, MiningModifiers.Grounded with { BlockBreakSpeed = 0.0F }, false)));
    }

    [Fact]
    public void EfficiencyAddsOnlyWhenTheBaseToolSpeedExceedsOne()
    {
        var block = new BlockMiningInfo(1.5F, true);
        MiningModifiers modifiers = MiningModifiers.Grounded with { EfficiencyBonus = 26.0F };

        Assert.Equal(1.0F / 1.5F / 100.0F,
            VanillaMiningProgress.Compute(block, new ToolMiningInfo(1.0F, false), modifiers, false));
        Assert.Equal(34.0F / 1.5F / 30.0F,
            VanillaMiningProgress.Compute(block, new ToolMiningInfo(8.0F, true), modifiers, false));
    }

    [Fact]
    public void HasteAttributesWaterAndAirborneModifiersFollowVanillasOrder()
    {
        var modifiers = MiningModifiers.Grounded with
        {
            EfficiencyBonus = 5.0F,
            DigSpeedAmplifier = 1,
            BlockBreakSpeed = 2.0F,
            EyesUnderwater = true,
            SubmergedMiningSpeed = 0.2F,
            OnGround = false,
        };
        float speed = 8.0F + 5.0F;
        speed *= 1.0F + 2 * 0.2F;
        speed *= 2.0F;
        speed *= 0.2F;
        speed /= 5.0F;

        Assert.Equal(speed / 1.5F / 30.0F,
            VanillaMiningProgress.Compute(new BlockMiningInfo(1.5F, true), new ToolMiningInfo(8.0F, true), modifiers, false));
    }

    [Theory]
    [InlineData(0, 0.3F)]
    [InlineData(1, 0.09F)]
    [InlineData(2, 0.0027F)]
    [InlineData(3, 0.00081F)]
    [InlineData(4, 0.00081F)]
    public void OlderFatigueUsesTheVanillaPiecewiseScale(int amplifier, float scale)
    {
        var modifiers = MiningModifiers.Grounded with { MiningFatigueAmplifier = amplifier };

        Assert.Equal(8.0F * scale / 1.5F / 30.0F,
            VanillaMiningProgress.Compute(new BlockMiningInfo(1.5F, true), new ToolMiningInfo(8.0F, true), modifiers, false));
    }

    [Fact]
    public void On26_3FatigueUsesTheExponentialScale()
    {
        var modifiers = MiningModifiers.Grounded with { MiningFatigueAmplifier = 2 };

        Assert.Equal(8.0F * (float)Math.Pow(0.3, 3) / 1.5F / 30.0F,
            VanillaMiningProgress.Compute(new BlockMiningInfo(1.5F, true), new ToolMiningInfo(8.0F, true), modifiers, true));
    }
}
