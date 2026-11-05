using Umpk.Game.Blocks;
using Umpk.Game.Items;

namespace Umpk.Client.Actions;

/// <summary>The native positive ignition result after block activation, cooldown and permission handling.</summary>
internal readonly record struct VanillaIgnitionUse(bool HasRules, bool LightsCandles, bool UsesComponents, bool LocalSwing)
{
    // Original 1.16–26.3 clients: both item methods accept canLight on synchronized campfire tags; 1.17 adds candles. SUCCESS changes from shouldSwing to CLIENT in1.21.2 and PREDICTED in26.3, where the inherited swing sends no serverbound animation.
    public static VanillaIgnitionUse ForProtocol(int protocol) => new(protocol is >= 735 and <= 777, protocol >= 755, protocol >= 766, protocol == 777);

    public bool Supports(Identifier item) => HasRules && item.IsMinecraft && item.Path is "flint_and_steel" or "fire_charge";

    public bool SupportsStack(ItemStack stack)
    {
        if (stack.Components.HasNoPatch)
            return true;
        // Damage and legacy NBT do not change this Survival useOn result. Effective modern patches can introduce other item behavior and remain unclassified.
        foreach (DataComponentEntry entry in stack.Components.Patch)
            if (!entry.Type.Equals(DataComponents.Damage) && (UsesComponents || !entry.Type.Equals(DataComponents.LegacyNbt)))
                return false;
        return true;
    }

    public BlockUsePrediction Predict(BlockState clicked, bool? campfireMember, bool? candleMember)
    {
        if (HasRules && (campfireMember is true || LightsCandles && candleMember is true)
            && clicked.TryGetProperty("lit", out string lit) && lit == "false"
            && clicked.TryGetProperty("waterlogged", out string waterlogged) && waterlogged == "false")
            return BlockUsePrediction.Success(LocalSwing);
        // Failure to light this state continues into native fire-survival and portal checks. It does not establish FAIL or PASS.
        return BlockUsePrediction.Unknown;
    }
}
