using System.Runtime.CompilerServices;
using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

/// <summary>The typed 1.20.5–1.21.4 can_break predicates and their native component-owned last-state cache. Null retains missing tag, property-order or full block-entity context.</summary>
internal sealed class VanillaComponentMiningPermission
{
    private const int ItemComponentsProtocol = 766;
    private const int ExtendedPredicateProtocol = 770;
    private readonly ConditionalWeakTable<AdventureModePredicateComponent, CachedPermission> _cache = new();

    private sealed record CachedPermission(BlockState State, bool Allowed);

    public static bool Supports(int protocol) => protocol is >= ItemComponentsProtocol and < ExtendedPredicateProtocol;

    public bool? Allows(ItemStack item, BlockState target, ClientRegistryTags tags, int protocol)
    {
        if (!Supports(protocol) || !target.IsValid)
            return null;
        if (item.IsEmpty || item.Components.Get(DataComponents.CanBreak) is not { } component)
            return false;
        if (_cache.TryGetValue(component, out CachedPermission? cached) && cached.State == target)
            return cached.Allowed;
        _cache.Remove(component);
        bool unknown = false;
        foreach (BlockPredicateEntry predicate in component.Predicates)
        {
            bool? matches = predicate.Blocks is not { } blocks ? true
                : blocks.Tag is { } tag ? tags.Contains(RegistryIds.Block, tag, target.Block.NetworkId)
                : blocks.Ids.Contains(target.Block.NetworkId);
            if (matches is false)
                continue;
            // BlockPredicate tests its state before block-entity NBT. A possible NBT match also leaves native cache invalidation unowned, even if a later predicate could grant this call.
            if (predicate.Nbt is not null)
                return null;
            if (matches is null || predicate.Properties is { Count: > 0 })
            {
                unknown = true;
                continue;
            }
            _cache.Add(component, new(target, true));
            return true;
        }
        if (unknown)
            return null;
        _cache.Add(component, new(target, false));
        return false;
    }
}
