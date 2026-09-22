using Umpk.Game.Entities;

namespace Umpk.Pathfinding.Core;

/// <summary>A frozen-at-capture snapshot of everything about the PLAYER (as opposed to the world) that a plan may price: active status effects and carried items.</summary>
/// <remarks>
/// <para>The staleness contract is the same one <see cref="Umpk.Physics.PhysicsConditions"/> carries, and for the same reason: the A* search runs OFF the session loop (<c>Navigator.PlanOnLoopAsync</c> hands the capture to <c>Task.Run</c>), so reading live <c>SelfState</c> or <c>InventoryState</c> from inside a move calculation would be exactly the off-loop live read <c>PlanningWorldView</c>'s own class comment exists to condemn. This record describes the INSTANT the plan was captured; a replan is what refreshes it. A capability that changes mid-route (a potion that expires, an item that is consumed) is invisible to the running executor until something asks for a new plan.</para>
/// <para>All THREE "known" flags are <c>required</c> rather than defaulted, deliberately. A producer that cannot observe the player's effects must say so, because the dangerous polarity is a consumer reading an empty list as "the hazard is not cleared, but also not present". Empty plus <c>Known = false</c> means UNKNOWN; empty plus <c>Known = true</c> means NONE.</para>
/// <para>The same discipline runs one level DOWN, inside a capture that IS known. <see cref="SoulSpeedLevel"/> answers 0 for a bot with no boots and for a bot whose boots the session could not read, and those are different claims: <c>EnchantmentReadout.CanEnumerate</c> is what separates them, because a legacy-NBT stack can only be ASKED for a named enchantment and never enumerated. A consumer that treats "level 0" as "definitely not enchanted" is making the same mistake one layer lower.</para>
/// </remarks>
public sealed record PathfinderCapabilities
{
    /// <summary>The capability snapshot every context defaults to: nothing captured, every arm unknown. This is what a session-free planner run and every pre-existing construction site get, so threading the surface through the contexts changes no plan.</summary>
    public static PathfinderCapabilities None { get; } =
        new() { EffectsKnown = false, InventoryKnown = false, VitalsKnown = false };

    /// <summary>The player's active status effects at capture time. Empty when <see cref="EffectsKnown"/> is false, in which case empty means "not observable", not "none".</summary>
    public IReadOnlyList<CapabilityEffect> Effects { get; init; } = [];

    /// <summary>Whether <see cref="Effects"/> is an observation at all. False when the producer had no entity tracking to read (the effects of the local player are written by the entity applier alone).</summary>
    public required bool EffectsKnown { get; init; }

    /// <summary>Whether <see cref="Items"/> is an observation at all. False when the producer had no inventory tracking to read. <see cref="CountOf"/> answers 0 either way, so a caller must consult this first.</summary>
    public required bool InventoryKnown { get; init; }

    /// <summary>Whether <see cref="Health"/> and <see cref="Food"/> are observations at all. False on a session that never received a health update, in which case both carry their spawn defaults and a caller must not read them as measurements.</summary>
    /// <remarks>Required and separate for the same reason the other two flags are: <c>SelfState.Health</c> initialises to <c>20f</c> and <c>Food</c> to <c>20</c>, so an unobserved player is indistinguishable from a healthy one by value alone - and "assume full health" is precisely the direction that plans a fall the bot cannot survive.</remarks>
    public required bool VitalsKnown { get; init; }

    /// <summary>The player's health at capture time, 0 to 20. Meaningless unless <see cref="VitalsKnown"/>.</summary>
    public float Health { get; init; } = 20f;

    /// <summary>The player's food level at capture time, 0 to 20. Meaningless unless <see cref="VitalsKnown"/>.</summary>
    /// <remarks>Carried alongside <see cref="Health"/> because it is the OTHER thing a landing policy asks about: vanilla's natural regeneration needs food 18 or more, so a bot at full health and a starving one recover from the same two points of fall damage on completely different timescales. Nothing in this repository reads it yet; it is captured with health rather than after it so the two never disagree about which tick they describe.</remarks>
    public int Food { get; init; } = 20;

    /// <summary>The non-empty player-window slots at capture time, addressed in the 46-slot <c>InventoryMenu</c> index space (0 crafting result, 1-4 crafting grid, 5-8 armor head-first, 9-35 backpack, 36-44 hotbar, 45 offhand). Empty when <see cref="InventoryKnown"/> is false.</summary>
    public IReadOnlyList<CapabilityItem> Items { get; init; } = [];

    /// <summary>The selected hotbar index at capture time, which is what turns <see cref="EquipmentSlot.MainHand"/> into a menu slot. Zero when the inventory is unknown.</summary>
    public int HeldSlot { get; init; }

    /// <summary>The soul-speed enchantment level on the boots at capture time, or 0.</summary>
    /// <remarks>
    /// <para><b>Why this is the pricing input rather than an attribute.</b> On 1.21+ the floor bypass is the <c>minecraft:movement_efficiency</c> attribute, but that attribute is positional: with soul-speed-III boots it reads 0.0 while the bot stands on stone and 1.0 while it stands on soul sand. A plan is captured wherever the bot happens to be, which is almost never the lane it is about to price, so an attribute-driven gate would read 0 and price a soul-sand lane conservatively for a bot that is in fact booted. The BOOTS are the thing that is stable between inventory changes, and they are what both eras' bypass ultimately depends on.</para>
    /// <para><b>Zero has two meanings and the caller must know which.</b> It is 0 when the boots carry no soul speed, when the feet slot is empty, when <see cref="InventoryKnown"/> is false, and when the session could not name the stack's enchantments at all. Every one of those is the CONSERVATIVE answer for a cost model - it keeps the full slow-floor charge - which is why they are allowed to share a value here. A consumer that wanted the opposite polarity would have to consult <see cref="InventoryKnown"/> and <c>EnchantmentReadout.CanEnumerate</c> itself.</para>
    /// <para>Nothing captures a soul-speed level for the ENGINE through here; the engine gets its own read from <c>PhysicsEngineHolder</c> every capture. This exists for the planner alone.</para>
    /// </remarks>
    public int SoulSpeedLevel =>
        Equipment(EquipmentSlot.Feet) is { } boots && boots.Enchantments.TryGetLevel(SoulSpeedId, out int level)
            ? level
            : 0;

    /// <summary>The soul-speed enchantment's identifier.</summary>
    private static readonly Identifier SoulSpeedId = Identifier.Minecraft("soul_speed");

    /// <summary>The depth-strider enchantment level on the boots at capture time, or 0. Feeds <c>ActionCosts.WadeCurrentCostMultiplier</c>, which is how a booted body stops being charged a bare body's price for wading a current.</summary>
    /// <remarks>
    /// <para><b>Why the BOOTS here and the ATTRIBUTE in the engine, and why that asymmetry is deliberate.</b> <see cref="SoulSpeedLevel"/>'s doc argues the boots for <c>movement_efficiency</c> because that attribute is POSITIONAL - it reads 0.0 while the bot stands on stone. That argument does not apply here: <c>water_movement_efficiency</c> is a flat equipment modifier, so the attribute would in fact be honest. The boots are used anyway, for a different reason: <b>the enchantment read works on all 50 protocols and the attribute exists on 17 of them.</b> One code path, one behaviour, one set of tests, on every era the client supports.</para>
    /// <para>The ENGINE takes the opposite choice, and must: <c>PhysicsEngineHolder</c> reads the attribute on the attribute era because the engine's job is to reproduce vanilla exactly, and above 1.21 vanilla reads the attribute. The PLANNER's job is to produce the same number on every era. The two answers agree on a normal player wearing normal boots, which is the case that matters; where they could disagree - a server handing out the attribute without the enchantment - the planner is the conservative one, because it prices the water as if the boots were not there.</para>
    /// <para><b>Zero has the same several meanings it has for <see cref="SoulSpeedLevel"/></b> - no enchantment, no boots, <see cref="InventoryKnown"/> false, or a stack whose enchantments could not be named - and all of them are the conservative answer here too, because they keep the full current charge.</para>
    /// <para><b>Frozen at capture by contract.</b> A plan priced with depth strider III and then executed bare is charged 1.0870x for work that costs 3.7371x. There is deliberately no watchdog: depth-strider boots do not self-destroy the way soul-speed boots do - diamond and netherite boots take durability from damage, not from wading - so nothing about EXECUTING the plan destroys the capability that priced it. The remaining case is a human unequipping mid-route, which is the same class as consuming a placed block and is covered by the same contract. <c>SegmentBudgetPolicy.SubmergedWalkSlack</c>'s doc carries the proof that the budget absorbs it.</para>
    /// </remarks>
    public int DepthStriderLevel =>
        Equipment(EquipmentSlot.Feet) is { } boots && boots.Enchantments.TryGetLevel(DepthStriderId, out int level)
            ? level
            : 0;

    /// <summary>The depth-strider enchantment's identifier.</summary>
    private static readonly Identifier DepthStriderId = Identifier.Minecraft("depth_strider");

    /// <summary>Whether the player may walk on powder snow: leather boots in the feet slot, and nothing else.</summary>
    /// <remarks>
    /// <para>This is item identity, not a material class or an armour slot in general: the best boots in the game do nothing, and leather boots in the hand do nothing.</para>
    /// <para><b>Derived here rather than captured, like <see cref="SoulSpeedLevel"/>, and for the same reason.</b> The boots are the stable fact; the collision answer they produce depends on the cell the body is over, which a capture taken wherever the bot happens to stand cannot know.</para>
    /// <para><b>False has three meanings and all three are the same answer.</b> Nothing on the feet, the wrong boots, and <see cref="InventoryKnown"/> false all give false, and false keeps powder snow in the hazard set. That is the conservative direction and the only safe one: an unobserved inventory is indistinguishable by value from a barefoot player, and reading it as "booted" plans a walk across a freezing pit the body then falls into.</para>
    /// </remarks>
    public bool PowderSnowWalkable => Equipment(EquipmentSlot.Feet)?.ItemId == LeatherBootsId;

    /// <summary>The one item vanilla lets a player walk on powder snow in.</summary>
    private static readonly Identifier LeatherBootsId = Identifier.Minecraft("leather_boots");

    /// <summary>The effect with this identifier, when the player has it. False both for "the player does not have it" and for "the producer could not see effects at all"; the two are told apart by <see cref="EffectsKnown"/>.</summary>
    /// <param name="id">The effect identifier, e.g. <c>minecraft:fire_resistance</c>.</param>
    /// <param name="effect">The effect found, or the default.</param>
    /// <returns>True when the effect is present.</returns>
    public bool TryGetEffect(Identifier id, out CapabilityEffect effect)
    {
        // A player carries a handful of effects at most, so a scan beats a dictionary and allocates nothing; the capture is built once per plan and read a few times per search.
        for (int i = 0; i < Effects.Count; i++)
            if (Effects[i].Id == id)
            {
                effect = Effects[i];
                return true;
            }

        effect = default;
        return false;
    }

    /// <summary>How many of an item the player carries, summed over every player-window slot. Zero when the inventory is unknown, so consult <see cref="InventoryKnown"/> before reading a zero as "none".</summary>
    /// <param name="itemId">The item identifier, e.g. <c>minecraft:torch</c>.</param>
    /// <returns>The total count across all slots.</returns>
    public int CountOf(Identifier itemId)
    {
        int total = 0;
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].ItemId == itemId)
                total += Items[i].Count;

        return total;
    }

    /// <summary>What the player is wearing or holding in one equipment slot, or null when the slot is empty, the inventory is unknown, or the slot has no index in the player window at all.</summary>
    /// <remarks>The indices are <c>PlayerInventorySlotMap</c>'s, not a second index space: <see cref="EquipmentSlot.Head"/> 5, <see cref="EquipmentSlot.Chest"/> 6, <see cref="EquipmentSlot.Legs"/> 7, <see cref="EquipmentSlot.Feet"/> 8, <see cref="EquipmentSlot.MainHand"/> <c>36 + HeldSlot</c>, <see cref="EquipmentSlot.OffHand"/> 45. <see cref="EquipmentSlot.Body"/> and <see cref="EquipmentSlot.Saddle"/> are mount equipment with no player-window slot, so they answer null rather than a guessed index.</remarks>
    /// <param name="slot">The equipment slot to read.</param>
    /// <returns>The item in that slot, or null.</returns>
    public CapabilityItem? Equipment(EquipmentSlot slot)
    {
        if (!TryMenuSlot(slot, out int menuSlot))
            return null;

        for (int i = 0; i < Items.Count; i++)
            if (Items[i].MenuSlot == menuSlot)
                return Items[i];

        return null;
    }

    /// <summary>The menu index of the head armor slot (<c>InventoryMenu</c> adds armor head-first at 5).</summary>
    private const int HeadMenuSlot = 5;

    /// <summary>The first menu index of the hotbar.</summary>
    private const int HotbarMenuStart = 36;

    /// <summary>The number of hotbar slots.</summary>
    private const int HotbarSize = 9;

    /// <summary>The menu index of the offhand slot.</summary>
    private const int OffhandMenuSlot = 45;

    private bool TryMenuSlot(EquipmentSlot slot, out int menuSlot)
    {
        switch (slot)
        {
            // Armor runs HEAD -> FEET in the menu, while the enum runs FEET -> HEAD, so the block is reversed: menu = 5 + (Head - slot).
            case EquipmentSlot.Head:
            case EquipmentSlot.Chest:
            case EquipmentSlot.Legs:
            case EquipmentSlot.Feet:
                menuSlot = HeadMenuSlot + (EquipmentSlot.Head - slot);
                return true;
            case EquipmentSlot.MainHand:
                menuSlot = HotbarMenuStart + Math.Clamp(HeldSlot, 0, HotbarSize - 1);
                return true;
            case EquipmentSlot.OffHand:
                menuSlot = OffhandMenuSlot;
                return true;
            default:
                menuSlot = -1;
                return false;
        }
    }
}
