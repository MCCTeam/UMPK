using Umpk.Game.Items;
using Umpk.Game.Items.Components;

namespace Umpk.Pathfinding.Core;

/// <summary>One active status effect on the player, with its remaining duration RESOLVED against the instant the plan was captured.</summary>
/// <remarks>The wire never carries a remaining duration: vanilla's server sends <c>update_mob_effect</c> on add and on refresh and <c>remove_mob_effect</c> on expiry, and nothing in between, so the number in <c>SelfState.ActiveEffect.Duration</c> is the duration AS APPLIED and stays that value until the next packet. <see cref="RemainingTicks"/> is that number minus the ticks elapsed since the apply, which is why <see cref="DurationIsEstimated"/> exists: presence is reliable, the number is derived.</remarks>
/// <param name="Id">The effect identifier. <c>umpk:unknown_effect_&lt;n&gt;</c> when the session's <c>minecraft:mob_effect</c> registry cannot name the network id (protocols 107-404 carry no such table at all).</param>
/// <param name="NetworkId">The effect's network (registry) id on this session.</param>
/// <param name="Amplifier">The amplifier; 0 is level I.</param>
/// <param name="RemainingTicks">Ticks left at capture time. <see cref="InfiniteRemaining"/> (-1) when the effect is infinite, and <see cref="UnknownRemaining"/> (<see cref="int.MinValue"/>) when the effect is known to be present but its remaining duration cannot be derived.</param>
/// <param name="IsInfinite">Whether the effect never expires on its own (a -1 duration on the wire).</param>
/// <param name="DurationIsEstimated">True when <see cref="RemainingTicks"/> was derived from an apply-tick stamp rather than read straight off a packet that arrived this tick.</param>
public readonly record struct CapabilityEffect(
    Identifier Id,
    int NetworkId,
    int Amplifier,
    int RemainingTicks,
    bool IsInfinite,
    bool DurationIsEstimated)
{
    /// <summary>The <see cref="RemainingTicks"/> value that means "never expires".</summary>
    public const int InfiniteRemaining = -1;

    /// <summary>The <see cref="RemainingTicks"/> value that means "present, duration not derivable". A consumer that gates on duration must refuse on this, not treat it as a large number.</summary>
    public const int UnknownRemaining = int.MinValue;
}

/// <summary>One non-empty player-window slot at capture time.</summary>
/// <param name="ItemId">The item identifier.</param>
/// <param name="Count">The stack count.</param>
/// <param name="MenuSlot">The 46-slot <c>InventoryMenu</c> index this stack sits in.</param>
/// <param name="Enchantments">The era-neutral enchantment readout for this stack.</param>
public readonly record struct CapabilityItem(
    Identifier ItemId,
    int Count,
    int MenuSlot,
    EnchantmentReadout Enchantments);

/// <summary>The enchantments on one captured stack, as a QUERY rather than a list, because enumeration is impossible on part of the supported range and a list would be a lie there.</summary>
/// <remarks>
/// <para>The era coverage is inverted from the obvious expectation:</para>
/// <list type="bullet">
/// <item>
/// Protocols 47-765 put the stack's NBT verbatim into <c>DataComponents.LegacyNbt</c>, and <see cref="ItemStack.TryGetEnchantmentLevel"/> interprets it on demand with no registry lookup at all - so <see cref="TryGetLevel"/> works there, while <c>ItemStack.Enchantments</c> (components only) is always empty, so <see cref="All"/> cannot.
/// </item>
/// <item>
/// Protocols 766+ carry a structured <c>minecraft:enchantments</c> component whose entries name their enchantment ONLY by numeric holder id, resolved against the session's <c>minecraft:enchantment</c> registry. Enumeration is possible exactly when that registry is populated and every holder in the stack resolved through it.
/// </item>
/// </list>
/// <para>So <see cref="CanEnumerate"/> is not a protocol test: it is "this readout can produce the COMPLETE, NAMED list for this stack", and it is false for every legacy-format stack and for any component-era stack whose session could not name its holders. When it is false, <see cref="All"/> is empty and empty does NOT mean "no enchantments"; ask <see cref="TryGetLevel"/> for the one you care about.</para>
/// <para>The readout holds the captured <see cref="ItemStack"/> and the legacy-id bridge rather than a pre-resolved list, because the bridge's concrete source lives in <c>Umpk.Data.Java</c>, which <c>Umpk.Pathfinding</c> must not reference; the producer in <c>Umpk.Client</c> supplies it. Holding the stack keeps its component map alive for the life of the plan, which is bounded by the 46 slots of the player window.</para>
/// </remarks>
public readonly record struct EnchantmentReadout
{
    private readonly ItemStack? _stack;
    private readonly string? _legacyEra;
    private readonly ILegacyItemBridgeSource? _legacyIds;

    /// <summary>Creates a readout over one captured stack.</summary>
    /// <param name="stack">The captured stack.</param>
    /// <param name="legacyEra">The legacy-bridge era key selecting the pre-1.13 numeric enchantment-id table.</param>
    /// <param name="legacyIds">The per-era numeric enchantment-id table source.</param>
    /// <param name="sessionCanNameEnchantments">Whether the session's <c>minecraft:enchantment</c> registry is populated, i.e. whether a component-era holder id can be turned into an identifier at all.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public EnchantmentReadout(
        ItemStack stack, string legacyEra, ILegacyItemBridgeSource legacyIds, bool sessionCanNameEnchantments)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(legacyEra);
        ArgumentNullException.ThrowIfNull(legacyIds);
        _stack = stack;
        _legacyEra = legacyEra;
        _legacyIds = legacyIds;
        CanEnumerate = sessionCanNameEnchantments && CarriesANamedEnchantmentList(stack);
    }

    /// <summary>The readout for a stack nobody captured: it can neither enumerate nor answer a level. This is what an unknown inventory and an empty slot both produce.</summary>
    public static EnchantmentReadout None => default;

    /// <summary>Whether <see cref="All"/> is the complete, named enchantment list for this stack. False for a legacy-NBT stack (whose enchantments are readable only through <see cref="TryGetLevel"/>) and for a component-era stack the session could not name.</summary>
    public bool CanEnumerate { get; }

    /// <summary>Every enchantment on the stack, when <see cref="CanEnumerate"/> says the list is complete. Empty otherwise, and empty does not mean "none".</summary>
    public IReadOnlyList<EnchantmentInstance> All => CanEnumerate && _stack is not null ? _stack.Enchantments : [];

    /// <summary>The level of one KNOWN enchantment on this stack, era-neutrally. Works on every protocol whose stack format this readout was built over, including the legacy-NBT band that <see cref="CanEnumerate"/> refuses.</summary>
    /// <param name="enchantment">The enchantment to look for, e.g. <c>minecraft:depth_strider</c>.</param>
    /// <param name="level">The level found, or 0.</param>
    /// <returns>True when the enchantment is present.</returns>
    public bool TryGetLevel(Identifier enchantment, out int level)
    {
        if (_stack is null || _legacyEra is null || _legacyIds is null)
        {
            level = 0;
            return false;
        }

        return _stack.TryGetEnchantmentLevel(enchantment, _legacyEra, _legacyIds, out level);
    }

    /// <summary>Whether the stack carries a STRUCTURED enchantment list every entry of which resolved to a named registry entry.</summary>
    /// <remarks>Both halves matter. Without the component check, a legacy-NBT stack (which never has one) would pass vacuously and report an empty <see cref="All"/> as an authoritative "no enchantments" while its <c>ench</c> list sat unread. Without the holder check, a component-era stack whose session could not name a holder would list a default handle that cannot report even its numeric id. The cost is that an unenchanted component-era stack, whose default component the server does not send, also reports false: conservative in the safe direction, since a consumer that wants one enchantment asks <see cref="TryGetLevel"/> and gets a true answer either way.</remarks>
    private static bool CarriesANamedEnchantmentList(ItemStack stack)
    {
        if (!stack.Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? component)
            || component is null)
            return false;

        IReadOnlyList<EnchantmentInstance> instances = component.Enchantments;
        for (int i = 0; i < instances.Count; i++)
            if (instances[i].Enchantment.IsDefault)
                return false;

        return true;
    }
}
