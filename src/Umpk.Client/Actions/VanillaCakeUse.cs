using System.Globalization;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

internal readonly record struct CakeConsumption(int State, int Food, float Saturation);
internal readonly record struct CakeAttachment(int State, ItemStack RemainingStack);

/// <summary>The registered cake's client result and local nutrition/block mutation.</summary>
internal readonly record struct VanillaCakeUse(bool HasRules, bool AlwaysHandles, bool EmptyHandConsumesWithoutSwing, bool InvulnerableCanEat, bool MainHandFallbackOnly, bool ClampNutritionLower, bool AttachesCandles, bool UsesInfiniteMaterials)
{
    // All75 retained client methods: cake always handles through1.10.2;1.11 adds eat-or-empty;1.15 introduces CONSUME and permits invulnerable eating. The main-hand fallback begins1.20.5;1.21 clamps nutrition at zero.
    internal static VanillaCakeUse ForProtocol(int protocol) => new(protocol is >= 47 and <= 777, protocol <= 210, protocol >= 573, protocol >= 573, protocol >= 766, protocol >= 767, protocol is >= 755 and <= 777, protocol >= 767);

    internal bool Supports(BlockState state) => HasRules && state.IsValid && state.Block.Id.IsMinecraft && state.Block.Id.Path == "cake";

    // Original755–777 attachment is a block activation in either hand, before ordinary item continuation. A synchronized tag cannot turn an unrelated item into a CandleBlock item.
    internal bool? CanAttachCandle(BlockState state, ItemStack item, bool? candleMember)
    {
        if (!AttachesCandles || item.IsEmpty || !IsCandleItem(item.Item.Id))
            return false;
        if (!TryBites(state, out int bites))
            return null;
        return bites == 0 ? candleMember : false;
    }

    internal bool TryCreateAttachment(BlockState state, ItemStack item, bool infiniteMaterials, out CakeAttachment attachment)
    {
        attachment = default;
        if (!AttachesCandles || !Supports(state) || !TryBites(state, out int bites) || bites != 0 || item.IsEmpty || !IsCandleItem(item.Item.Id)
            || !state.Source.Blocks.TryGet(Identifier.Minecraft(item.Item.Id.Path + "_cake"), out var candleCake))
            return false;
        var changed = new BlockState(state.Source, candleCake.Value.DefaultStateId);
        if (!changed.TryGetProperty("lit", out string lit) || lit != "false")
            return false;
        attachment = new(changed.StateId, infiniteMaterials ? item : item.Shrink(1));
        return true;
    }

    private static bool IsCandleItem(Identifier item)
        => item.IsMinecraft && item.Path is "candle" or "white_candle" or "orange_candle" or "magenta_candle" or "light_blue_candle" or "yellow_candle" or "lime_candle" or "pink_candle" or "gray_candle" or "light_gray_candle" or "cyan_candle" or "purple_candle" or "blue_candle" or "brown_candle" or "green_candle" or "red_candle" or "black_candle";

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
