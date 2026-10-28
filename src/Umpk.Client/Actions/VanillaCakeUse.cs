using System.Globalization;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

internal readonly record struct CakeConsumption(int State, int Food, float Saturation);

/// <summary>The registered cake's client result and local nutrition/block mutation.</summary>
internal readonly record struct VanillaCakeUse(bool HasRules, bool AlwaysHandles, bool EmptyHandConsumesWithoutSwing, bool InvulnerableCanEat, bool MainHandFallbackOnly, bool ClampNutritionLower)
{
    // All75 retained client methods: cake always handles through1.10.2;1.11 adds eat-or-empty;1.15 introduces CONSUME and permits invulnerable eating. The main-hand fallback begins1.20.5;1.21 clamps nutrition at zero.
    internal static VanillaCakeUse ForProtocol(int protocol) => new(protocol is >= 47 and <= 777, protocol <= 210, protocol >= 573, protocol >= 573, protocol >= 766, protocol >= 767);

    internal bool Supports(BlockState state) => HasRules && state.IsValid && state.Block.Id.IsMinecraft && state.Block.Id.Path == "cake";

    internal BlockUsePrediction Predict(BlockState state, ItemStack item, Hand hand, SelfState self, IBlockInteractionSource data, bool localSwing, bool currentPlayerObserved, out CakeConsumption? consumption)
    {
        consumption = null;
        // A measured base Item useOn cannot be a CandleBlockItem, even if a server changes candle tags. Specialized/default-block items and effective component patches retain their separate context.
        if (!item.IsEmpty && (!item.Components.HasNoPatch || !data.TryGetItem(item.Item.Id, out ItemUseOnInfo use) || use.Kind != ItemUseOnKind.Pass))
            return BlockUsePrediction.Unknown;
        if (MainHandFallbackOnly && hand == Hand.Off)
            return BlockUsePrediction.Pass;
        if (!currentPlayerObserved || !self.HealthObserved || !TryBites(state, out int bites))
            return BlockUsePrediction.Unknown;
        bool canEat = InvulnerableCanEat ? self.Invulnerable || self.Food < 20 : !self.Invulnerable && self.Food < 20;
        if (!canEat)
            return AlwaysHandles || item.IsEmpty && !EmptyHandConsumesWithoutSwing ? BlockUsePrediction.Success(localSwing)
                : item.IsEmpty ? BlockUsePrediction.Consume : BlockUsePrediction.Pass;
        if (!TryNextState(state, bites, out int next))
            return BlockUsePrediction.Unknown;
        int food = ClampNutritionLower ? Math.Clamp(unchecked(self.Food + 2), 0, 20) : Math.Min(unchecked(self.Food + 2), 20);
        float saturation = self.Saturation + 2 * .1F * 2.0F;
        saturation = ClampNutritionLower ? Math.Clamp(saturation, 0.0F, food) : Math.Min(saturation, food);
        consumption = new(next, food, saturation);
        return BlockUsePrediction.Success(localSwing);
    }

    private static bool TryBites(BlockState state, out int bites)
    {
        bites = state.LegacyMeta;
        return state.LegacyId >= 0 ? bites is >= 0 and <= 6
            : state.TryGetProperty("bites", out string value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out bites) && bites is >= 0 and <= 6;
    }

    private static bool TryNextState(BlockState state, int bites, out int next)
    {
        next = 0;
        if (bites == 6)
        {
            if (!state.Source.Blocks.TryGet(Identifier.Minecraft("air"), out var air))
                return false;
            next = air.Value.DefaultStateId;
            return true;
        }
        if (state.LegacyId < 0)
            return JukeboxPredictionState.TryPropertyState(state, "bites", (bites + 1).ToString(CultureInfo.InvariantCulture), out next);
        next = (state.StateId & ~15) | (bites + 1);
        return state.Source.IsValidState(next);
    }
}
