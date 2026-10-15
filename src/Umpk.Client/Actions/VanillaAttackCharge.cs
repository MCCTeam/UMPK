using Umpk.Game.Items;

namespace Umpk.Client.Actions;

/// <summary>The local player's attack counter, advanced by the session's player-tick phase after input.</summary>
internal sealed class VanillaAttackCharge
{
    private long? _playerLifetime;
    private ItemStack _lastItem = ItemStack.Empty;

    public int StrengthTicks { get; private set; }

    public void Synchronize(long playerLifetime)
    {
        if (_playerLifetime == playerLifetime)
            return;
        _playerLifetime = playerLifetime;
        StrengthTicks = 0;
        _lastItem = ItemStack.Empty;
    }

    public void Advance(long playerLifetime, ItemStack heldItem)
    {
        Synchronize(playerLifetime);
        StrengthTicks = unchecked(StrengthTicks + 1);
        if (!_lastItem.IsSameItem(heldItem))
            Reset();
        _lastItem = heldItem;
    }

    public void Reset() => StrengthTicks = 0;

    public static bool IsBlocked(int strengthTicks, double attackSpeed, float requiredStrength, int tolerance = 0)
    {
        float delay = (float)(1.0 / attackSpeed * 20.0);
        float optimisticStrength = unchecked(strengthTicks + tolerance) / delay;
        return requiredStrength > 0.0F && optimisticStrength < requiredStrength;
    }
}
