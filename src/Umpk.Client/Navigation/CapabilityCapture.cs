using Umpk.Client.State;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Pathfinding.Core;

namespace Umpk.Client.Navigation;

/// <summary>Builds the frozen <see cref="PathfinderCapabilities"/> snapshot that rides a plan, off the session state and on the session loop.</summary>
/// <remarks>
/// <para>This has to run ON the loop, for the same reason <c>PlanningWorldView</c> exists: the A* search runs on the thread pool (<c>Navigator.PlanOnLoopAsync</c> -&gt; <c>Task.Run</c>), and reading <c>SelfState</c> or <c>InventoryState</c> from there would be a live read of session state off the loop. So the reads happen once, here, at capture time, and the plan carries the answer.</para>
/// <para>The cost is bounded and small by construction: the effect table holds a handful of entries and the player window is 46 slots, so this is tens of microseconds once per plan and once per replan - three orders below the search. It must never become an O(volume) step; <c>CapturePlan</c>'s own remarks record what happened the last time a per-plan step walked the region.</para>
/// <para>Both "known" flags are answered STRUCTURALLY, from whether the state that would hold the data exists at all, not from a feature flag read separately. <c>ClientState</c> creates the entity store and the inventory only when their features are on, and the entity applier is the only writer of self effects (<c>ApplierCatalog.Build</c>), so a null store is exactly "this session can never observe an effect". Reporting that as an empty list would read as "no potions", which is the one direction that can get a bot killed.</para>
/// </remarks>
internal static class CapabilityCapture
{
    /// <summary>The identifier namespace for an effect this session's registry cannot name. Deliberately not <c>minecraft</c>: protocols 107-404 carry no <c>minecraft:mob_effect</c> table at all, and an unnameable effect is still PRESENT, so it is reported under an identifier that cannot collide with a vanilla one rather than dropped.</summary>
    private const string UnknownNamespace = "umpk";

    /// <summary>Builds the capability snapshot for the current session state.</summary>
    /// <param name="state">The session state, read on the loop.</param>
    /// <param name="legacyEra">The legacy-bridge era key for pre-1.13 numeric enchantment ids.</param>
    /// <param name="legacyIds">The per-era numeric enchantment-id table source.</param>
    public static PathfinderCapabilities From(
        ClientState state, string legacyEra, ILegacyItemBridgeSource legacyIds)
    {
        SelfState self = state.Self;
        RegistryAccess? registries = state.Registries;
        bool effectsKnown = state.EntitiesOrNull is not null;
        InventoryState? inventory = state.InventoryOrNull;

        return new PathfinderCapabilities
        {
            EffectsKnown = effectsKnown,
            InventoryKnown = inventory is not null,
            VitalsKnown = self.HealthObserved,
            Health = self.Health,
            Food = self.Food,
            Effects = effectsKnown ? ReadEffects(self, registries, state.SessionTick) : [],
            Items = inventory is null ? [] : ReadItems(inventory, legacyEra, legacyIds, registries),
            HeldSlot = inventory is null ? 0 : self.HeldSlot,
        };
    }

    private static IReadOnlyList<CapabilityEffect> ReadEffects(
        SelfState self, RegistryAccess? registries, long nowTick)
    {
        if (self.ActiveEffects.Count == 0)
            return [];

        var effects = new List<CapabilityEffect>(self.ActiveEffects.Count);
        foreach (ActiveEffect effect in self.ActiveEffects.Values)
        {
            // An effect stamped on THIS tick is the number the server just stated; anything older is derived arithmetic against the client's own tick clock, which is what "estimated" means. An infinite effect is never an estimate: it does not count down at all.
            bool estimated = !effect.IsInfinite && effect.AppliedAtTick != nowTick;
            effects.Add(new CapabilityEffect(
                NameOf(registries, effect.EffectId),
                effect.EffectId,
                effect.Amplifier,
                effect.RemainingTicksAt(nowTick),
                effect.IsInfinite,
                estimated));
        }

        return effects;
    }

    private static Identifier NameOf(RegistryAccess? registries, int networkId) =>
        registries is not null && registries.MobEffects.TryGetKey(networkId, out Identifier id)
            ? id
            : new Identifier(UnknownNamespace, $"unknown_effect_{networkId}");

    private static IReadOnlyList<CapabilityItem> ReadItems(
        InventoryState inventory,
        string legacyEra,
        ILegacyItemBridgeSource legacyIds,
        RegistryAccess? registries)
    {
        // Whether a component-era holder id can be turned into a name at all is a SESSION fact, resolved once here rather than per slot: it is the same registry for every stack, and it is empty on 767+ until the server's configuration-phase registry_data installs it.
        bool canName = registries is not null && registries.Enchantments.Count > 0;

        IReadOnlyList<ItemStack> slots = inventory.PlayerSlots;
        var items = new List<CapabilityItem>();
        for (int slot = 0; slot < slots.Count; slot++)
        {
            ItemStack stack = slots[slot];
            if (stack.IsEmpty)
                continue;

            items.Add(new CapabilityItem(
                stack.Item.Id,
                stack.Count,
                slot,
                new EnchantmentReadout(stack, legacyEra, legacyIds, canName)));
        }

        return items;
    }
}
