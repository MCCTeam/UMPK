using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

internal readonly record struct MiningModifiers(
    float EfficiencyBonus,
    int DigSpeedAmplifier,
    int MiningFatigueAmplifier,
    float BlockBreakSpeed,
    float SubmergedMiningSpeed,
    bool EyesUnderwater,
    bool OnGround)
{
    public static MiningModifiers Grounded => new(0.0F, -1, -1, 1.0F, 0.2F, false, true);

    public bool LegacyUnderwaterPenalty { get; init; }
}

/// <summary>Vanilla Player.getDestroySpeed followed by BlockBehaviour.getDestroyProgress. Keep every intermediate value single precision and retain the era's fatigue formula.</summary>
internal static class VanillaMiningProgress
{
    public static float Compute(BlockMiningInfo block, ToolMiningInfo tool, MiningModifiers modifiers, bool exponentialFatigue)
    {
        if (block.Hardness < 0.0F)
            return 0.0F;

        float speed = tool.Speed;
        if (speed > 1.0F)
            speed += modifiers.EfficiencyBonus;
        if (modifiers.DigSpeedAmplifier >= 0)
            speed *= 1.0F + (modifiers.DigSpeedAmplifier + 1) * 0.2F;
        if (modifiers.MiningFatigueAmplifier >= 0)
        {
            float scale = exponentialFatigue
                ? (float)Math.Pow(0.3, modifiers.MiningFatigueAmplifier + 1)
                : modifiers.MiningFatigueAmplifier switch
                {
                    0 => 0.3F,
                    1 => 0.09F,
                    2 => 0.0027F,
                    _ => 8.1E-4F,
                };
            speed *= scale;
        }
        speed *= modifiers.BlockBreakSpeed;
        if (modifiers.EyesUnderwater)
            // Before the submerged-mining-speed attribute, native divides by five. Multiplication by 0.2F can cross the completion threshold a tick earlier.
            speed = modifiers.LegacyUnderwaterPenalty ? speed / 5.0F : speed * modifiers.SubmergedMiningSpeed;
        if (!modifiers.OnGround)
            speed /= 5.0F;

        int divisor = !block.RequiresCorrectTool || tool.CorrectForDrops ? 30 : 100;
        return speed / block.Hardness / divisor;
    }
}
