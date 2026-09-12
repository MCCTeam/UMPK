using Umpk.Game.Items;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The per-era component wire-id table (a flat array indexed by component wire ID). It maps a component's numeric wire id to its identifier (for every component the era knows) and to a typed <see cref="ItemComponentCodec"/> (for the common gameplay set UMPK fully models). The id ordering is version data; the ordering differs between 770 and 776, so each era has its own table instance. Nested-stack codecs are bound to their owning table so bundle/container contents decode under the same era.</summary>
/// <remarks>A component id present in the era but outside the typed set has an identifier here and an <see cref="UnmodeledKeyByWireId"/> key, but no codec. How that is handled depends on whether the wire delimits the payload, and that decision lives in <c>ItemStackCodecs.ReadPatch</c>, not here: this table only reports what it has (<see cref="TryGetCodec"/>) and never decides a failure policy. An id OUTSIDE the era's range is a framing fault and still raises <see cref="ProtocolViolationException"/> from <see cref="KeyByWireId"/>.</remarks>
internal sealed partial class ItemComponentTable
{
    private readonly Identifier[] _idByWire;

    private readonly Dictionary<int, ItemComponentCodec> _codecByWire;

    private readonly Dictionary<Identifier, int> _wireById;

    private readonly Dictionary<DataComponentType, ItemComponentCodec> _codecByKey;

    // One key per wire id that the era knows but UMPK does not model, built eagerly with the table so the instances are stable for its lifetime (DataComponentMap compares keys by reference identity). Null at a slot means that id is typed and uses its codec's own key.
    private readonly DataComponentType?[] _unmodeledKeyByWire;

    private ItemComponentTable(
        Identifier[] idByWire,
        Dictionary<int, ItemComponentCodec> codecByWire,
        Dictionary<Identifier, int> wireById,
        Dictionary<DataComponentType, ItemComponentCodec> codecByKey,
        ItemComponentLayout layout)
    {
        _idByWire = idByWire;
        _codecByWire = codecByWire;
        _wireById = wireById;
        _codecByKey = codecByKey;
        ShapeToken = "components/" + WireShapeDigest.Of(DigestRows(idByWire, codecByWire, layout));

        _unmodeledKeyByWire = new DataComponentType?[idByWire.Length];
        for (int i = 0; i < idByWire.Length; i++)
            if (!codecByWire.ContainsKey(i))
                _unmodeledKeyByWire[i] = DataComponents.Unmodeled(idByWire[i]);

    }

    /// <summary>This era's contribution to the <see cref="WireShape"/> of every codec that closes over it: the digest of its <c>(wire id, identifier, component type)</c> rows and the layout axes that decide which payload codec each row gets. Renaming a codec class cannot move it, while rebinding a packet to a neighboring era's table necessarily does.</summary>
    internal string ShapeToken { get; }

    /// <summary>Looks up the typed codec for a component wire id. Reports absence; never decides policy.</summary>
    /// <param name="wireId">The component wire id.</param>
    /// <param name="codec">The codec, when this era types the component.</param>
    /// <returns>True when a typed codec exists.</returns>
    internal bool TryGetCodec(int wireId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ItemComponentCodec? codec) =>
        _codecByWire.TryGetValue(wireId, out codec);

    /// <summary>The codec for a component on a COMPACT component list, where payloads carry no length prefix. This is the single place in UMPK that raises <see cref="UnmodeledItemComponentException"/>, and therefore the single entry point to the connection's packet-scoped recovery. Callers on a length-delimited or payload-free list must NOT use it: they can recover losslessly and should go through <see cref="TryGetCodec"/> and <see cref="UnmodeledKeyByWireId"/> instead.</summary>
    /// <param name="wireId">The component wire id.</param>
    /// <returns>The typed codec.</returns>
    /// <exception cref="UnmodeledItemComponentException">The id is in range for this era but UMPK does not model it. Costs the packet, not the session.</exception>
    /// <exception cref="ProtocolViolationException">The id is out of range for this era. That is a framing fault, not a modeling gap, so it stays fatal: letting it into the recovery path would let a real desync masquerade as a missing codec.</exception>
    internal ItemComponentCodec CompactCodec(int wireId)
    {
        if (_codecByWire.TryGetValue(wireId, out ItemComponentCodec? codec))
            return codec;

        // KeyByWireId throws ProtocolViolationException for an out-of-range id, which is exactly the wanted split: only an id the era itself declares is eligible for recovery.
        throw new UnmodeledItemComponentException(wireId, KeyByWireId(wireId));
    }

    /// <summary>The stable key for a component this era knows but UMPK does not model. Used for a removal entry (which carries no payload) and for a length-delimited unmodeled payload, so both round-trip exactly instead of faulting.</summary>
    /// <param name="wireId">The component wire id.</param>
    /// <returns>The per-era key.</returns>
    /// <exception cref="ProtocolViolationException">The id is out of range for this era.</exception>
    internal DataComponentType UnmodeledKeyByWireId(int wireId)
    {
        if (wireId >= 0 && wireId < _unmodeledKeyByWire.Length && _unmodeledKeyByWire[wireId] is DataComponentType key)
            return key;

        if (wireId >= 0 && wireId < _idByWire.Length)
        {
            // Typed on this era: the codec owns the key.
            return _codecByWire[wireId].Type;
        }

        throw new ProtocolViolationException($"Item component wire id {wireId} is out of range for this era.");
    }

    /// <summary>The identifier for a component wire id (works for every component the era knows).</summary>
    /// <exception cref="ProtocolViolationException">The id is out of range for this era.</exception>
    public Identifier KeyByWireId(int wireId)
    {
        if (wireId >= 0 && wireId < _idByWire.Length)
            return _idByWire[wireId];

        throw new ProtocolViolationException($"Item component wire id {wireId} is out of range for this era.");
    }

    /// <summary>True when the component key is on this era's wire (excludes model-only keys like legacy_nbt).</summary>
    public bool Contains(DataComponentType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _codecByKey.ContainsKey(type) || _wireById.ContainsKey(type.Id);
    }

    /// <summary>The typed codec for a component key.</summary>
    internal ItemComponentCodec ByKey(DataComponentType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_codecByKey.TryGetValue(type, out ItemComponentCodec? codec))
            return codec;

        throw new ProtocolViolationException($"Component '{type.Id}' has no typed codec on this era.");
    }

    /// <summary>The wire id for a component key.</summary>
    public int WireId(DataComponentType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_wireById.TryGetValue(type.Id, out int id))
            return id;

        throw new ProtocolViolationException($"Component '{type.Id}' has no wire id on this era.");
    }

    /// <summary>The digest input: one row per wire id, naming the id, the identifier the era spells it with and the component type the bound codec carries (or a dash where the era leaves the id untyped), then the layout's own axes. The layout is there because two eras can share an ordering and an entirely typed set while disagreeing about a payload: 1.21.5 and 1.21.6 differ only in <c>attribute_modifiers</c>, and the rows alone cannot see that.</summary>
    private static IEnumerable<string> DigestRows(
        Identifier[] idByWire,
        Dictionary<int, ItemComponentCodec> codecByWire,
        ItemComponentLayout layout)
    {
        for (int i = 0; i < idByWire.Length; i++)
        {
            string type = codecByWire.TryGetValue(i, out ItemComponentCodec? codec) ? codec.Type.Id.ToString() : "-";
            yield return $"{i}:{idByWire[i]}:{type}";
        }

        yield return layout.ToString();
    }

    /// <summary>Builds the 1.21.5 (protocol 770) component id table.</summary>
    public static ItemComponentTable V1_21_5() => Build(ItemComponentLayout.V1_21_5);

    /// <summary>Builds the 1.21.6-1.21.8 (protocols 771/772) table. The component id ORDERING is byte-for-byte the 770 ordering, but <c>attribute_modifiers</c> is not the 770 payload: protocol 771 appended a display field after each entry's attribute, modifier, and slot, so 771 onward needs the display-carrying codec.</summary>
    /// <returns>The table.</returns>
    public static ItemComponentTable V1_21_6() => Build(ItemComponentLayout.V1_21_6);

    /// <summary>Builds the 1.21.9/1.21.10 (protocol 773) table: the 770 ordering, the 1.21.6 attribute payload, and the 1.21.9 forms of the three components that release re-shaped (see <c>ItemComponentLayout.TypedEntityData</c>).</summary>
    /// <returns>The table.</returns>
    public static ItemComponentTable V1_21_9() => Build(ItemComponentLayout.V1_21_9);

    /// <summary>Builds the 1.21.11 (protocol 774) table: its own ordering, the 1.21.9 payload family.</summary>
    /// <returns>The table.</returns>
    public static ItemComponentTable V1_21_11() => Build(ItemComponentLayout.V1_21_11);

    /// <summary>Builds the 26.1 (protocol 775) table: its own ordering (110 entries; 776 inserts <c>sulfur_cube_content</c> at wire id 78), the 26.x payload family, and the 26.1 nested-stack TEMPLATE form on <c>container</c> / <c>bundle_contents</c> / <c>charged_projectiles</c>.</summary>
    /// <returns>The table.</returns>
    public static ItemComponentTable V26_1() => Build(ItemComponentLayout.V26_1);

    /// <summary>Builds the 26.2 (protocol 776) component id table: its own 111-id ordering, the 26.x payload family, and the 26.1 nested-stack template form.</summary>
    /// <returns>The table.</returns>
    public static ItemComponentTable V26_2() => Build(ItemComponentLayout.V26_2);

    /// <summary>
    /// The three components whose payload shapes change at 1.21.9 and therefore require codecs from protocol 773 onward that differ from their 1.21.5 codecs.
    /// <list type="bullet">
    /// <item><c>minecraft:profile</c>: protocols through 772 carry optional name, optional UUID, and
    /// properties; protocol 773 adds a resolved/partial discriminator and trailing skin patch (<see cref="ItemComponentCodecs.ProfileV1_21_9"/>).</item>
    /// <item><c>minecraft:entity_data</c> and <c>minecraft:block_entity_data</c>: protocol 773 adds a
    /// registry type id before the compound tag (<see cref="ItemComponentCodecs.EntityDataV1_21_9"/> / <see cref="ItemComponentCodecs.BlockEntityDataV1_21_9"/>).</item>
    /// </list>
    /// <c>minecraft:bucket_entity_data</c> did not move with them and remains a bare compound tag.
    /// </summary>
    /// <param name="layout">The era's component layout.</param>
    /// <returns>The table.</returns>
    /// <remarks>The dialect decides which builder runs, and it is the only thing that does: 766-769 predate the 1.21.5 interaction rename and type a different, smaller set of components, so the two eras cannot share one typed array. Everything else an era decides rides in the layout.</remarks>
    private static ItemComponentTable Build(ItemComponentLayout layout) =>
        layout.Dialect == ComponentWireEra.Modern ? BuildModern(layout) : BuildLegacyDialect(layout);

    private static ItemComponentTable BuildModern(ItemComponentLayout layout)
    {
        string[] identifiers = layout.Ordering;
        var idByWire = new Identifier[identifiers.Length];
        var wireById = new Dictionary<Identifier, int>(identifiers.Length);
        for (int i = 0; i < identifiers.Length; i++)
        {
            var id = Identifier.Parse(identifiers[i]);
            idByWire[i] = id;
            wireById[id] = i;
        }

        // The typed component set. Nested-stack codecs are created fresh per table and bound below so their nested decoding uses this era's table.
        NestedStackComponentCodec container = ItemComponentCodecs.MakeContainer(layout.NestedStacks);
        NestedStackComponentCodec bundle = ItemComponentCodecs.MakeBundleContents(layout.NestedStacks);
        NestedStackComponentCodec charged = ItemComponentCodecs.MakeChargedProjectiles(layout.NestedStacks);

        // Both were listed-but-untyped, and both are a single bare nested stack. use_remainder
        // follows the era's stack form (count-first through 774, template on 775/776);
        // sulfur_cube_content only exists on 776 and is always a template, so the binding loop drops it on every other era by itself.
        NestedStackComponentCodec useRemainder = ItemComponentCodecs.MakeUseRemainder(layout.NestedStacks);
        NestedStackComponentCodec sulfurCube = ItemComponentCodecs.MakeSulfurCubeContent();

        ItemComponentCodec[] typed =
        [
            ItemComponentCodecs.CustomData,
            layout.TypedEntityData ? ItemComponentCodecs.BlockEntityDataV1_21_9 : ItemComponentCodecs.BlockEntityData,
            layout.TypedEntityData ? ItemComponentCodecs.EntityDataV1_21_9 : ItemComponentCodecs.EntityData,
            ItemComponentCodecs.BucketEntityData,
            ItemComponentCodecs.Damage,
            ItemComponentCodecs.MaxDamage,
            ItemComponentCodecs.MaxStackSize,
            ItemComponentCodecs.RepairCost,
            ItemComponentCodecs.Unbreakable,
            ItemComponentCodecs.CustomName,
            ItemComponentCodecs.ItemName,
            ItemComponentCodecs.Lore,
            ItemComponentCodecs.Rarity,
            ItemComponentCodecs.Enchantments,
            ItemComponentCodecs.StoredEnchantments,
            ItemComponentCodecs.DyedColor,
            ItemComponentCodecs.MapColor,
            ItemComponentCodecs.Food,
            ItemComponentCodecs.BlockState,
            container,
            bundle,
            charged,
            ItemComponentCodecs.PotDecorations,
            ItemComponentCodecs.PotionContents,
            layout.TypedEntityData ? ItemComponentCodecs.ProfileV1_21_9 : ItemComponentCodecs.Profile,
            ItemComponentCodecs.WritableBookContent,
            ItemComponentCodecs.WrittenBookContent,
            ItemComponentCodecs.Trim,
            ItemComponentCodecs.Tool,
            layout.AttributeDisplay ? ItemComponentCodecs.AttributeModifiersV26_2 : ItemComponentCodecs.AttributeModifiersV1_21_5,

            // Fixed-shape components shared by protocols 770 and 776.
            ItemComponentCodecs.MapId,
            ItemComponentCodecs.MapPostProcessing,
            ItemComponentCodecs.MapDecorations,
            ItemComponentCodecs.OminousBottleAmplifier,
            ItemComponentCodecs.EnchantmentGlintOverride,
            ItemComponentCodecs.PotionDurationScale,
            ItemComponentCodecs.ItemModel,
            ItemComponentCodecs.TooltipStyle,
            ItemComponentCodecs.NoteBlockSound,
            ItemComponentCodecs.BaseColor,
            ItemComponentCodecs.TooltipDisplay,
            ItemComponentCodecs.CustomModelData,
            ItemComponentCodecs.CreativeSlotLock,
            ItemComponentCodecs.Glider,
            ItemComponentCodecs.IntangibleProjectile,
            ItemComponentCodecs.DebugStickState,
            ItemComponentCodecs.Recipes,
            ItemComponentCodecs.Lock,
            ItemComponentCodecs.ContainerLoot,

            // The dye-color family, all one VarInt color id. minecraft:dye exists only on 776; the binding loop below drops any codec whose id this era does not carry, so listing it here is correct for both tables.
            ItemComponentCodecs.WolfCollar,
            ItemComponentCodecs.CatCollar,
            ItemComponentCodecs.SheepColor,
            ItemComponentCodecs.ShulkerColor,
            ItemComponentCodecs.TropicalFishBaseColor,
            ItemComponentCodecs.TropicalFishPatternColor,
            ItemComponentCodecs.Dye,
            ItemComponentCodecs.MinimumAttackCharge,
            ItemComponentCodecs.AdditionalTradeCost,
            useRemainder,
            sulfurCube,

            // The firework pair has the same wire layout on every component era, so both tables use the same codecs.
            ItemComponentCodecs.Fireworks,
            ItemComponentCodecs.FireworkExplosion,

            // These components use their era-specific codecs. minecraft:can_break and minecraft:can_place_on stay untyped here: from protocol 770 their predicate payload includes an open, registry-dispatched component-matcher branch. They are typed on the pre-770 table below.
            ItemComponentCodecs.BannerPatterns,
            layout.TypedEntityData ? ItemComponentCodecs.BeesV1_21_9 : ItemComponentCodecs.Bees,
            ItemComponentCodecs.SuspiciousStewEffects,
            ItemComponentCodecs.LodestoneTracker,
            ItemComponentCodecs.Consumable,
            ItemComponentCodecs.DeathProtection,
            ItemComponentCodecs.Enchantable,
            ItemComponentCodecs.Repairable,
            ItemComponentCodecs.UseCooldown,
            layout.UnwrappedHolders ? ItemComponentCodecs.DamageResistantV26_1 : ItemComponentCodecs.DamageResistant,
            layout.ShearableEquippable ? ItemComponentCodecs.EquippableV1_21_6 : ItemComponentCodecs.EquippableV1_21_5,
            layout.UnwrappedHolders ? ItemComponentCodecs.InstrumentV26_1 : ItemComponentCodecs.InstrumentV1_21_5,
            layout.UnwrappedHolders ? ItemComponentCodecs.JukeboxPlayableV26_1 : ItemComponentCodecs.JukeboxPlayableV1_21_5,
        ];

        var codecByWire = new Dictionary<int, ItemComponentCodec>(typed.Length);
        var codecByKey = new Dictionary<DataComponentType, ItemComponentCodec>(typed.Length);
        foreach (ItemComponentCodec codec in typed)
            if (wireById.TryGetValue(codec.Type.Id, out int wireId))
            {
                codecByWire[wireId] = codec;
                codecByKey[codec.Type] = codec;
            }

        var table = new ItemComponentTable(idByWire, codecByWire, wireById, codecByKey, layout);
        container.Bind(table);
        bundle.Bind(table);
        charged.Bind(table);
        useRemainder.Bind(table);
        sulfurCube.Bind(table);
        return table;
    }

    /// <summary>Builds the 1.20.5/1.20.6 (protocol 766) component id table.</summary>
    public static ItemComponentTable V1_20_5() => Build(ItemComponentLayout.V1_20_5);

    /// <summary>Builds the 1.21/1.21.1 (protocol 767) component id table.</summary>
    public static ItemComponentTable V1_21() => Build(ItemComponentLayout.V1_21);

    /// <summary>Builds the 1.21.2/1.21.3 (protocol 768) component id table. Its 67-entry ordering is its own (<see cref="ComponentIds.V1_21_2"/>); the PAYLOADS are still the pre-1.21.5 family, so this shares the 766/767 typed set: <c>unbreakable</c> is a <c>showInTooltip</c> bool, <c>custom_model_data</c> is one VarInt, and the interaction dialect on components is legacy.</summary>
    /// <remarks>Delta from the 767 table: <c>fire_resistant</c> is gone (replaced by the untyped <c>damage_resistant</c>) and drops out automatically because 768 has no wire id for it; <c>item_model</c>, <c>tooltip_style</c> and <c>glider</c> are new and use two identifier strings and a zero-byte unit; <c>food</c> became <c>VAR_INT nutrition + FLOAT saturation + BOOL canAlwaysEat</c> at 1.21.2, so it types here but not on 766/767. Only two payloads changed at all between 1.21.1 and 1.21.2 (<c>ominous_bottle_amplifier</c>, a one-field composite over the same VarInt, and <c>recipes</c>, which stays one network NBT tag).</remarks>
    /// <returns>The table.</returns>
    public static ItemComponentTable V1_21_2() => Build(ItemComponentLayout.V1_21_2);

    /// <summary>Builds the 1.21.4 (protocol 769) component id table. It keeps the same 67-entry ordering and payload family as 768, except <c>CustomModelData</c> changes from one VarInt to the four-list form, so 769 binds the list codec.</summary>
    /// <returns>The table.</returns>
    public static ItemComponentTable V1_21_4() => Build(ItemComponentLayout.V1_21_4);

    private static ItemComponentTable BuildLegacyDialect(ItemComponentLayout layout)
    {
        string[] identifiers = layout.Ordering;
        var idByWire = new Identifier[identifiers.Length];
        var wireById = new Dictionary<Identifier, int>(identifiers.Length);
        for (int i = 0; i < identifiers.Length; i++)
        {
            var id = Identifier.Parse(identifiers[i]);
            idByWire[i] = id;
            wireById[id] = i;
        }

        // Protocols 766-769 predate the 26.1 template-stack switch, so every nested stack here is the count-first form.
        NestedStackComponentCodec container = ItemComponentCodecs.MakeContainer(NestedStackForm.CountFirst);
        NestedStackComponentCodec bundle = ItemComponentCodecs.MakeBundleContents(NestedStackForm.CountFirst);
        NestedStackComponentCodec charged = ItemComponentCodecs.MakeChargedProjectiles(NestedStackForm.CountFirst);

        // use_remainder is a single bare count-first stack from protocol 768. It has no wire id on protocols 766-767, so the binding loop drops it there without needing a gate.
        NestedStackComponentCodec useRemainder = ItemComponentCodecs.MakeUseRemainder(NestedStackForm.CountFirst);

        // Wire-stable subset only. The pre-1.21.5 showInTooltip/UUID/food variants remain untyped; those ids throw with identity when they appear on the wire.
        ItemComponentCodec[] typed =
        [
            ItemComponentCodecs.CustomData,
            ItemComponentCodecs.BlockEntityData,
            ItemComponentCodecs.EntityData,
            ItemComponentCodecs.BucketEntityData,
            ItemComponentCodecs.Damage,
            ItemComponentCodecs.MaxDamage,
            ItemComponentCodecs.MaxStackSize,
            ItemComponentCodecs.RepairCost,

            // 766-769 sit BELOW the 1.21.5 interaction rename, so every component-valued payload here uses the legacy clickEvent/hoverEvent dialect. Protocol 770 renames the fields to "click_event"/"hover_event". Plain text is identical either way, which is why binding the modern dialect here was quiet until an item name carried an interaction.
            ItemComponentCodecs.CustomNameLegacy,
            ItemComponentCodecs.ItemNameLegacy,
            ItemComponentCodecs.LoreLegacy,
            ItemComponentCodecs.Rarity,
            ItemComponentCodecs.MapColor,
            ItemComponentCodecs.BlockState,
            container,
            bundle,
            charged,
            ItemComponentCodecs.Profile,
            ItemComponentCodecs.WritableBookContent,
            ItemComponentCodecs.WrittenBookContentLegacy,

            // Only components whose 1.20.6/1.21.1 payload matches the bound implementation are here;
            // era-specific variants get their own codec (Unbreakable is a bool here, zero bytes on 770+; custom_model_data is a single VarInt here, four lists on 770+).
            ItemComponentCodecs.MapId,
            ItemComponentCodecs.MapPostProcessing,
            ItemComponentCodecs.MapDecorations,
            ItemComponentCodecs.OminousBottleAmplifier,
            ItemComponentCodecs.EnchantmentGlintOverride,
            ItemComponentCodecs.NoteBlockSound,
            ItemComponentCodecs.BaseColor,
            ItemComponentCodecs.UnbreakableV1_20_5,
            layout.ListCustomModelData ? ItemComponentCodecs.CustomModelData : ItemComponentCodecs.CustomModelDataV1_20_5,
            ItemComponentCodecs.CreativeSlotLock,
            ItemComponentCodecs.HideTooltip,
            ItemComponentCodecs.HideAdditionalTooltip,
            ItemComponentCodecs.FireResistant,
            ItemComponentCodecs.IntangibleProjectile,
            ItemComponentCodecs.DebugStickState,
            ItemComponentCodecs.Recipes,
            ItemComponentCodecs.Lock,
            ItemComponentCodecs.ContainerLoot,

            // The 768/769 additions. item_model, tooltip_style and glider have no wire id on 766/767 at all, so the binding loop would drop them there anyway; food DOES exist on 766/767 with two other record shapes, so it is gated instead of listed unconditionally.
            .. layout.WideIdPayloads
                ? new[]
                {
                    ItemComponentCodecs.ItemModel,
                    ItemComponentCodecs.TooltipStyle,
                    ItemComponentCodecs.Glider,
                    ItemComponentCodecs.Food,
                }
                : [],
            useRemainder,

            // enchantments/stored_enchantments are the pre-1.21.5 shape: the same holder->level map plus the trailing showInTooltip BOOL that 1.21.5 deleted. The firework pair needs no era variant at all.
            ItemComponentCodecs.EnchantmentsV1_20_5,
            ItemComponentCodecs.StoredEnchantmentsV1_20_5,
            ItemComponentCodecs.Fireworks,
            ItemComponentCodecs.FireworkExplosion,

            // pot_decorations has the same VarInt-counted list of at most four item registry ids across every structured-component era.
            ItemComponentCodecs.PotDecorations,

            // These components are included on every era that declares them. Ids the era does not carry drop out of the binding loop by themselves, which is what keeps the 1.21.2 additions (consumable, use_cooldown, damage_resistant, enchantable, equippable, repairable, death_protection) and 1.21's jukebox_playable off 766/767 without a gate. Only the shape that genuinely MOVES inside this table is gated: instrument's direct holder changed at 1.21.2 (a VarInt tick count became a FLOAT second count and a description component was appended), so it follows WideIdPayloads.
            ItemComponentCodecs.BannerPatterns,
            ItemComponentCodecs.Bees,
            ItemComponentCodecs.CanBreakV1_20_5,
            ItemComponentCodecs.CanPlaceOnV1_20_5,
            ItemComponentCodecs.SuspiciousStewEffects,
            ItemComponentCodecs.LodestoneTracker,
            ItemComponentCodecs.JukeboxPlayableV1_21,
            ItemComponentCodecs.Consumable,
            ItemComponentCodecs.DamageResistant,
            ItemComponentCodecs.DeathProtection,
            ItemComponentCodecs.Enchantable,
            ItemComponentCodecs.EquippableV1_21_2,
            ItemComponentCodecs.Repairable,
            ItemComponentCodecs.UseCooldown,
            layout.WideIdPayloads ? ItemComponentCodecs.InstrumentV1_21_2 : ItemComponentCodecs.InstrumentV1_20_5,
        ];

        var codecByWire = new Dictionary<int, ItemComponentCodec>(typed.Length);
        var codecByKey = new Dictionary<DataComponentType, ItemComponentCodec>(typed.Length);
        foreach (ItemComponentCodec codec in typed)
            if (wireById.TryGetValue(codec.Type.Id, out int wireId))
            {
                codecByWire[wireId] = codec;
                codecByKey[codec.Type] = codec;
            }

        var table = new ItemComponentTable(idByWire, codecByWire, wireById, codecByKey, layout);
        container.Bind(table);
        bundle.Bind(table);
        charged.Bind(table);
        useRemainder.Bind(table);
        return table;
    }
}
