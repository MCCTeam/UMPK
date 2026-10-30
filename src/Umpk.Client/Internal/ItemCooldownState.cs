using Umpk.Game.Items;

namespace Umpk.Client.Internal;

/// <summary>Owns native player-tick cooldown intervals and exact modern group identities.</summary>
internal sealed class ItemCooldownState
{
    private readonly record struct Cooldown(int Start, int End, int? ItemProjection);

    private readonly Dictionary<int, Cooldown> _items = [];
    private readonly Dictionary<Identifier, Cooldown> _groups = [];
    private readonly Dictionary<int, int> _itemProjection = [];
    private int _tickCount;

    internal IReadOnlyDictionary<int, int> ItemProjection => _itemProjection;

    internal void SetItem(int itemId, int ticks)
    {
        if (ticks == 0)
            _items.Remove(itemId);
        else
            _items[itemId] = new Cooldown(_tickCount, unchecked(_tickCount + ticks), itemId);
        RefreshProjection(itemId);
    }

    internal void SetGroup(Identifier group, int ticks, int? itemProjection)
    {
        int? previousProjection = _groups.TryGetValue(group, out Cooldown previous) ? previous.ItemProjection : null;
        if (ticks == 0)
            _groups.Remove(group);
        else
            _groups[group] = new Cooldown(_tickCount, unchecked(_tickCount + ticks), itemProjection);
        if (previousProjection is { } previousItem)
            RefreshProjection(previousItem);
        if (itemProjection is { } item)
            RefreshProjection(item);
    }

    internal bool IsOnCooldown(ItemStack stack, int protocol)
    {
        if (stack.IsEmpty)
            return false;
        if (_items.TryGetValue(stack.Item.NetworkId, out Cooldown item) && IsActive(item))
            return true;
        Identifier groupId = stack.Components.Get(DataComponents.UseCooldown)?.CooldownGroup ?? stack.Item.Id;
        return protocol >= 768 && _groups.TryGetValue(groupId, out Cooldown group) && IsActive(group);
    }

    private bool IsActive(Cooldown cooldown)
    {
        // ItemCooldowns uses int interval arithmetic, then float division at partial tick zero. This also preserves negative durations and Java's counter-wrap behavior.
        float duration = unchecked(cooldown.End - cooldown.Start);
        float remaining = cooldown.End - (float)_tickCount;
        return Math.Clamp(remaining / duration, 0f, 1f) > 0f;
    }

    internal void Tick()
    {
        _tickCount = unchecked(_tickCount + 1);
        foreach ((int key, Cooldown cooldown) in _items)
            if (cooldown.End <= _tickCount)
            {
                _items.Remove(key);
                RefreshProjection(key);
            }
        foreach ((Identifier key, Cooldown cooldown) in _groups)
            if (cooldown.End <= _tickCount)
            {
                _groups.Remove(key);
                if (cooldown.ItemProjection is { } item)
                    RefreshProjection(item);
            }
    }

    private void RefreshProjection(int itemId)
    {
        if (_items.TryGetValue(itemId, out Cooldown item))
        {
            _itemProjection[itemId] = unchecked(item.End - item.Start);
            return;
        }
        foreach (Cooldown group in _groups.Values)
            if (group.ItemProjection == itemId)
            {
                _itemProjection[itemId] = unchecked(group.End - group.Start);
                return;
            }
        _itemProjection.Remove(itemId);
    }

    internal void Clear()
    {
        _items.Clear();
        _groups.Clear();
        _itemProjection.Clear();
        _tickCount = 0;
    }
}
