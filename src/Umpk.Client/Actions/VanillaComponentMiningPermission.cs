using System.Runtime.CompilerServices;
using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

/// <summary>The typed 1.20.5–1.21.5 can_break predicates and their native component-owned last-state cache. Null retains missing tag, native property-kind/range or full block-entity context.</summary>
internal sealed class VanillaComponentMiningPermission
{
    private const int ItemComponentsProtocol = 766;
    private const int LastMeasuredPredicateProtocol = 770;
    private readonly ConditionalWeakTable<AdventureModePredicateComponent, CachedPermission> _cache = new();

    private sealed record CachedPermission(BlockState State, bool Allowed);

    public static bool Supports(int protocol) => protocol is >= ItemComponentsProtocol and <= LastMeasuredPredicateProtocol;

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
            // The native client BlockInWorld overload deliberately ignores modern component matchers.
            bool? matches = predicate.Blocks is not { } blocks ? true
                : blocks.Tag is { } tag ? tags.Contains(RegistryIds.Block, tag, target.Block.NetworkId)
                : blocks.Ids.Contains(target.Block.NetworkId);
            bool? properties = MatchesProperties(predicate.Properties, target);
            if (matches is false || properties is false)
                continue;
            // BlockPredicate tests its state before block-entity NBT. A possible NBT match also leaves native cache invalidation unowned, even if a later predicate could grant this call.
            if (predicate.Nbt is not null)
                return null;
            if (matches is null || properties is null)
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

    private static bool? MatchesProperties(IReadOnlyList<StatePropertyMatcher>? properties, BlockState target)
    {
        bool unknown = false;
        if (properties is null)
            return true;
        foreach (StatePropertyMatcher property in properties)
        {
            if (!target.TryGetProperty(property.Name, out string actual))
            {
                if (!target.PropertyNames.Contains(property.Name))
                    return false;
                unknown = true;
                continue;
            }
            if (!property.Exact)
            {
                // Native ranges include equality. Canonical current-value endpoints need no property-kind or ordering assumption.
                if ((property.MinValue is null || property.MinValue == actual)
                    && (property.MaxValue is null || property.MaxValue == actual))
                    continue;
                unknown = true;
                continue;
            }
            if (actual == property.Value)
                continue;
            // IntegerProperty accepts alternate Java decimal spellings. The canonical value domain does not identify the native property class or its range order.
            if (property.Value is { } value && VanillaLegacyMiningPermission.TryReadJavaInteger(value, out int requested)
                && VanillaLegacyMiningPermission.TryReadJavaInteger(actual, out int present) && requested == present)
                unknown = true;
            else
                return false;
        }
        return unknown ? null : true;
    }

}
