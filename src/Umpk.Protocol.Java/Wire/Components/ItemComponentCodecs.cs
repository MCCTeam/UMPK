using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;

// The model value type and the codec property below share the name "FireworkExplosion"; the alias keeps the type reachable from inside ItemComponentCodecs, where the property would otherwise shadow it.
using FireworkExplosionValue = Umpk.Game.Items.Components.FireworkExplosion;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The typed vanilla item data-component payload codecs for the modern component wire (protocols 770/776). Each codec reads/writes one component and hashes its value through <see cref="HashOps"/> for the 1.21.5+ hashed-slot model. The common gameplay set is fully typed; components outside it have no codec on the era table and raise a <see cref="ProtocolViolationException"/> with identity (the compact wire is not skippable).</summary>
/// <remarks>Protocol 770 flattened <c>enchantments</c>, <c>unbreakable</c>, <c>dyed_color</c>, <c>trim</c>, and <c>attribute_modifiers</c> dropped their per-component <c>show_in_tooltip</c> flag (it moved to the separate <c>tooltip_display</c> component). Those flags live on the model records with a default of <see langword="true"/> and are not read/written on the implemented eras; round-trip preserves the default. The 26.2 <c>attribute_modifiers</c> adds a trailing display field, encoded via the era flag.</remarks>
internal static partial class ItemComponentCodecs
{
    /// <summary>Builds a codec for <c>minecraft:custom_data</c>: a single network-NBT compound.</summary>
    public static ItemComponentCodec CustomData { get; } = new NbtCarrierCodec(
        DataComponents.CustomData,
        static nbt => new CustomDataComponent(AsCompound(nbt)),
        static v => ((CustomDataComponent)v).Data);

    /// <summary><c>minecraft:block_entity_data</c> on 766-772: a bare network-NBT compound.</summary>
    public static ItemComponentCodec BlockEntityData { get; } = new NbtCarrierCodec(
        DataComponents.BlockEntityData,
        static nbt => new BlockEntityDataComponent(AsCompound(nbt)),
        static v => ((BlockEntityDataComponent)v).Data);

    /// <summary><c>minecraft:entity_data</c> on 766-772: a bare network-NBT compound.</summary>
    public static ItemComponentCodec EntityData { get; } = new NbtCarrierCodec(
        DataComponents.EntityData,
        static nbt => new EntityDataComponent(AsCompound(nbt)),
        static v => ((EntityDataComponent)v).Data);

    /// <summary><c>minecraft:block_entity_data</c> from 773 (1.21.9): a VarInt block-entity type registry id before the compound. The registry id is a plain VarInt on protocols 773-776.</summary>
    public static ItemComponentCodec BlockEntityDataV1_21_9 { get; } = new TypedNbtCarrierCodec(
        DataComponents.BlockEntityData,
        static (typeId, nbt) => new BlockEntityDataComponent(nbt, typeId),
        static v => (((BlockEntityDataComponent)v).TypeId, ((BlockEntityDataComponent)v).Data));

    /// <summary><c>minecraft:entity_data</c> from 773 (1.21.9): a VarInt entity type registry id BEFORE the compound. <c>bucket_entity_data</c> did not move and keeps the bare-compound form through 776.</summary>
    public static ItemComponentCodec EntityDataV1_21_9 { get; } = new TypedNbtCarrierCodec(
        DataComponents.EntityData,
        static (typeId, nbt) => new EntityDataComponent(nbt, typeId),
        static v => (((EntityDataComponent)v).TypeId, ((EntityDataComponent)v).Data));

    /// <summary><c>minecraft:bucket_entity_data</c>.</summary>
    public static ItemComponentCodec BucketEntityData { get; } = new NbtCarrierCodec(
        DataComponents.BucketEntityData,
        static nbt => new BucketEntityDataComponent(AsCompound(nbt)),
        static v => ((BucketEntityDataComponent)v).Data);

    /// <summary><c>minecraft:damage</c> (VarInt).</summary>
    public static ItemComponentCodec Damage { get; } = new VarIntCodec(
        DataComponents.Damage, static i => new DamageComponent(i), static v => ((DamageComponent)v).Value);

    /// <summary><c>minecraft:max_damage</c> (VarInt).</summary>
    public static ItemComponentCodec MaxDamage { get; } = new VarIntCodec(
        DataComponents.MaxDamage, static i => new MaxDamageComponent(i), static v => ((MaxDamageComponent)v).Value);

    /// <summary><c>minecraft:max_stack_size</c> (VarInt).</summary>
    public static ItemComponentCodec MaxStackSize { get; } = new VarIntCodec(
        DataComponents.MaxStackSize, static i => new MaxStackSizeComponent(i), static v => ((MaxStackSizeComponent)v).Value);

    /// <summary><c>minecraft:repair_cost</c> (VarInt).</summary>
    public static ItemComponentCodec RepairCost { get; } = new VarIntCodec(
        DataComponents.RepairCost, static i => new RepairCostComponent(i), static v => ((RepairCostComponent)v).Value);

    /// <summary><c>minecraft:unbreakable</c> (zero payload bytes on 1.21.5+).</summary>
    public static ItemComponentCodec Unbreakable { get; } = new UnitCodec(
        DataComponents.Unbreakable, static () => new UnbreakableComponent());

    /// <summary><c>minecraft:custom_name</c> (network component, modern interaction dialect: 770+).</summary>
    public static ItemComponentCodec CustomName { get; } = MakeCustomName(ComponentWireEra.Modern);

    /// <summary><c>minecraft:custom_name</c> on 766-769 (legacy <c>clickEvent</c>/<c>hoverEvent</c> dialect).</summary>
    public static ItemComponentCodec CustomNameLegacy { get; } = MakeCustomName(ComponentWireEra.Legacy);

    /// <summary><c>minecraft:item_name</c> (network component, modern interaction dialect: 770+).</summary>
    public static ItemComponentCodec ItemName { get; } = MakeItemName(ComponentWireEra.Modern);

    /// <summary><c>minecraft:item_name</c> on 766-769 (legacy interaction dialect).</summary>
    public static ItemComponentCodec ItemNameLegacy { get; } = MakeItemName(ComponentWireEra.Legacy);

    /// <summary><c>minecraft:lore</c> (list of network components, modern interaction dialect: 770+).</summary>
    public static ItemComponentCodec Lore { get; } = new LoreCodecImpl(ComponentWireEra.Modern);

    /// <summary><c>minecraft:lore</c> on 766-769 (legacy interaction dialect).</summary>
    public static ItemComponentCodec LoreLegacy { get; } = new LoreCodecImpl(ComponentWireEra.Legacy);

    private static ItemComponentCodec MakeCustomName(ComponentWireEra era) => new ComponentCodec(
        DataComponents.CustomName, era, static c => new CustomNameComponent(c), static v => ((CustomNameComponent)v).Name);

    private static ItemComponentCodec MakeItemName(ComponentWireEra era) => new ComponentCodec(
        DataComponents.ItemName, era, static c => new ItemNameComponent(c), static v => ((ItemNameComponent)v).Name);

    /// <summary><c>minecraft:rarity</c> (VarInt id-mapper: common/uncommon/rare/epic).</summary>
    public static ItemComponentCodec Rarity { get; } = new RarityCodecImpl();

    /// <summary><c>minecraft:enchantments</c> (map of enchantment holder -> level).</summary>
    public static ItemComponentCodec Enchantments { get; } = new EnchantmentsCodecImpl(
        DataComponents.Enchantments, stored: false);

    /// <summary><c>minecraft:stored_enchantments</c>.</summary>
    public static ItemComponentCodec StoredEnchantments { get; } = new EnchantmentsCodecImpl(
        DataComponents.StoredEnchantments, stored: true);

    /// <summary><c>minecraft:enchantments</c> on 766-769: the same map followed by the pre-1.21.5 <c>showInTooltip</c> boolean.</summary>
    public static ItemComponentCodec EnchantmentsV1_20_5 { get; } = new EnchantmentsCodecImpl(
        DataComponents.Enchantments, stored: false, hasShowInTooltip: true);

    /// <summary><c>minecraft:stored_enchantments</c> on 766-769 (trailing <c>showInTooltip</c> BOOL).</summary>
    public static ItemComponentCodec StoredEnchantmentsV1_20_5 { get; } = new EnchantmentsCodecImpl(
        DataComponents.StoredEnchantments, stored: true, hasShowInTooltip: true);

    /// <summary><c>minecraft:fireworks</c>. One codec for every component era: the payload is byte-identical from 1.20.5 to 26.2 (see <see cref="FireworksCodecImpl"/>).</summary>
    public static ItemComponentCodec Fireworks { get; } = new FireworksCodecImpl();

    /// <summary><c>minecraft:firework_explosion</c> (a firework STAR). One codec for every component era; the payload is byte-identical from 1.20.5 to 26.2 (see <see cref="FireworkExplosionCodecImpl"/>).</summary>
    public static ItemComponentCodec FireworkExplosion { get; } = new FireworkExplosionCodecImpl();

    /// <summary><c>minecraft:dyed_color</c> (Int rgb on 1.21.5+).</summary>
    public static ItemComponentCodec DyedColor { get; } = new IntColorCodec(
        DataComponents.DyedColor, static rgb => new DyedColorComponent(rgb), static v => ((DyedColorComponent)v).Rgb);

    /// <summary><c>minecraft:map_color</c> (Int rgb).</summary>
    public static ItemComponentCodec MapColor { get; } = new IntColorCodec(
        DataComponents.MapColor, static rgb => new MapColorComponent(rgb), static v => ((MapColorComponent)v).Rgb);

    /// <summary><c>minecraft:food</c>.</summary>
    public static ItemComponentCodec Food { get; } = new FoodCodecImpl();

    /// <summary><c>minecraft:block_state</c> (string map).</summary>
    public static ItemComponentCodec BlockState { get; } = new BlockStateCodecImpl();

    /// <summary>Builds a <c>minecraft:container</c> codec bound to a nested-stack era table.</summary>
    /// <param name="form">The era's nested item-stack wire form.</param>
    /// <returns>The codec.</returns>
    public static NestedStackComponentCodec MakeContainer(NestedStackForm form) => new ContainerCodecImpl(form);

    /// <summary>Builds a <c>minecraft:bundle_contents</c> codec bound to a nested-stack era table.</summary>
    /// <param name="form">The era's nested item-stack wire form.</param>
    /// <returns>The codec.</returns>
    public static NestedStackComponentCodec MakeBundleContents(NestedStackForm form) => new StackListCodec(
        DataComponents.BundleContents,
        static items => new BundleContentsComponent(items),
        static v => ((BundleContentsComponent)v).Items,
        form);

    /// <summary><c>minecraft:pot_decorations</c>: a VarInt-counted list of at most four item registry ids. The raw ids are retained on decode, so an id outside the session item registry still round-trips (this component reaches clients through advancement display icons, which must never fault the session).</summary>
    public static ItemComponentCodec PotDecorations { get; } = new PotDecorationsCodecImpl();

    /// <summary>Builds a 26.3+ <c>minecraft:pot_decorations</c> codec: exactly four optional item-stack templates (back, left, right, front), bound to a nested-stack era table.</summary>
    /// <returns>The codec.</returns>
    public static NestedStackComponentCodec MakePotDecorationsV26_3() => new PotDecorationsStacksCodecImpl();

    /// <summary>Builds a <c>minecraft:charged_projectiles</c> codec bound to a nested-stack era table.</summary>
    /// <param name="form">The era's nested item-stack wire form.</param>
    /// <returns>The codec.</returns>
    public static NestedStackComponentCodec MakeChargedProjectiles(NestedStackForm form) => new StackListCodec(
        DataComponents.ChargedProjectiles,
        static items => new ChargedProjectilesComponent(items),
        static v => ((ChargedProjectilesComponent)v).Projectiles,
        form);

    /// <summary>Builds a <c>minecraft:use_remainder</c> codec bound to a nested-stack era table.</summary>
    /// <remarks>One bare stack, whose form follows the era: count-first on 768-774 and template form on 775-776. The component does not exist below 768.</remarks>
    /// <param name="form">The era's nested item-stack wire form.</param>
    /// <returns>The codec.</returns>
    public static NestedStackComponentCodec MakeUseRemainder(NestedStackForm form) => new SingleStackCodec(
        DataComponents.UseRemainder,
        static stack => new UseRemainderComponent(stack),
        static v => ((UseRemainderComponent)v).ConvertInto,
        form);

    /// <summary>Builds a <c>minecraft:sulfur_cube_content</c> codec (26.2+): one bare item-stack TEMPLATE.</summary>
    /// <remarks>Protocol 776 is the only era with the component, at wire id 78.</remarks>
    /// <returns>The codec.</returns>
    public static NestedStackComponentCodec MakeSulfurCubeContent() => new SingleStackCodec(
        DataComponents.SulfurCubeContent,
        static stack => new SulfurCubeContentComponent(stack),
        static v => ((SulfurCubeContentComponent)v).AbsorbedBlockItemStack,
        NestedStackForm.Template);

    /// <summary><c>minecraft:potion_contents</c>.</summary>
    public static ItemComponentCodec PotionContents { get; } = new PotionContentsCodecImpl();

    /// <summary><c>minecraft:profile</c> on 766-772 (optional name, optional uuid, properties).</summary>
    public static ItemComponentCodec Profile { get; } = new ProfileCodecImpl();

    /// <summary><c>minecraft:profile</c> from 773 (either(GameProfile, Partial) plus the skin patch).</summary>
    public static ItemComponentCodec ProfileV1_21_9 { get; } = new ProfileV1_21_9CodecImpl();

    /// <summary><c>minecraft:writable_book_content</c>.</summary>
    public static ItemComponentCodec WritableBookContent { get; } = new WritableBookCodecImpl();

    /// <summary><c>minecraft:written_book_content</c> (pages are components, modern dialect: 770+).</summary>
    public static ItemComponentCodec WrittenBookContent { get; } = new WrittenBookCodecImpl(ComponentWireEra.Modern);

    /// <summary><c>minecraft:written_book_content</c> on 766-769 (legacy interaction dialect).</summary>
    public static ItemComponentCodec WrittenBookContentLegacy { get; } = new WrittenBookCodecImpl(ComponentWireEra.Legacy);

    /// <summary><c>minecraft:trim</c> (material holder, pattern holder on 1.21.5+).</summary>
    public static ItemComponentCodec Trim { get; } = new TrimCodecImpl();

    /// <summary><c>minecraft:tool</c> (rules opaque, mining speed, damage-per-block, creative flag).</summary>
    public static ItemComponentCodec Tool { get; } = new ToolCodecImpl();

    /// <summary><c>minecraft:attribute_modifiers</c> for the 1.21.5 shape (no display field).</summary>
    public static ItemComponentCodec AttributeModifiersV1_21_5 { get; } = new AttributeModifiersCodecImpl(hasDisplay: false);

    /// <summary><c>minecraft:attribute_modifiers</c> for the 26.2 shape (trailing display field).</summary>
    public static ItemComponentCodec AttributeModifiersV26_2 { get; } = new AttributeModifiersCodecImpl(hasDisplay: true);

    // Components the era listed but UMPK did not model

    /// <summary><c>minecraft:map_id</c>: a VarInt saved-map id, identical on 766/767/770/776. A filled map in inventory carries it, which is why an unmodeled map_id disconnected every 1.20.5+ client. The hashed form is the plain integer.</summary>
    public static ItemComponentCodec MapId { get; } = new VarIntCodec(
        DataComponents.MapId, static i => new MapIdComponent(i), static v => ((MapIdComponent)v).Id);

    /// <summary><c>minecraft:ominous_bottle_amplifier</c>: one VarInt amplifier on all supported eras.</summary>
    public static ItemComponentCodec OminousBottleAmplifier { get; } = new VarIntCodec(
        DataComponents.OminousBottleAmplifier,
        static i => new OminousBottleAmplifierComponent(i),
        static v => ((OminousBottleAmplifierComponent)v).Amplifier);

    /// <summary><c>minecraft:map_post_processing</c>: a VarInt enum id (0 = LOCK, 1 = SCALE), identical on all four eras. It has no persistent form, so it exists purely in transit while a crafted map is materialized.</summary>
    public static ItemComponentCodec MapPostProcessing { get; } = new MapPostProcessingCodecImpl();

    /// <summary><c>minecraft:enchantment_glint_override</c>: a single boolean on all four eras.</summary>
    public static ItemComponentCodec EnchantmentGlintOverride { get; } = new BoolCodec(
        DataComponents.EnchantmentGlintOverride,
        static b => new EnchantmentGlintOverrideComponent(b),
        static v => ((EnchantmentGlintOverrideComponent)v).ShowGlint);

    /// <summary><c>minecraft:unbreakable</c> on 766/767 only: one <c>showInTooltip</c> boolean. On 770/776 the component became a zero-byte payload, which is the existing <see cref="Unbreakable"/> codec; that difference is exactly why the pre-1.21.5 tables left it untyped.</summary>
    public static ItemComponentCodec UnbreakableV1_20_5 { get; } = new BoolCodec(
        DataComponents.Unbreakable,
        static b => new UnbreakableComponent(b),
        static v => ((UnbreakableComponent)v).ShowInTooltip);

    /// <summary><c>minecraft:potion_duration_scale</c> (770/776): one float.</summary>
    public static ItemComponentCodec PotionDurationScale { get; } = new FloatCodec(
        DataComponents.PotionDurationScale,
        static f => new PotionDurationScaleComponent(f),
        static v => ((PotionDurationScaleComponent)v).Scale);

    /// <summary><c>minecraft:item_model</c> (770/776): one namespaced identifier string.</summary>
    public static ItemComponentCodec ItemModel { get; } = new IdentifierCodec(
        DataComponents.ItemModel,
        static id => new ItemModelComponent(id),
        static v => ((ItemModelComponent)v).Model);

    /// <summary><c>minecraft:tooltip_style</c> (770/776): one identifier string, as item_model.</summary>
    public static ItemComponentCodec TooltipStyle { get; } = new IdentifierCodec(
        DataComponents.TooltipStyle,
        static id => new TooltipStyleComponent(id),
        static v => ((TooltipStyleComponent)v).Style);

    /// <summary><c>minecraft:note_block_sound</c>: one UTF-8 identifier string on all four eras.</summary>
    public static ItemComponentCodec NoteBlockSound { get; } = new IdentifierCodec(
        DataComponents.NoteBlockSound,
        static id => new NoteBlockSoundComponent(id),
        static v => ((NoteBlockSoundComponent)v).Sound);

    /// <summary><c>minecraft:base_color</c>: a VarInt dye-color id, identical on all four eras. The model carries the canonical name, and the data codec is the name string, so the hashed form is the string.</summary>
    public static ItemComponentCodec BaseColor { get; } = new DyeColorCodec(
        DataComponents.BaseColor,
        static name => new BaseColorComponent(name),
        static v => ((BaseColorComponent)v).Color);

    /// <summary><c>minecraft:tooltip_display</c> (770/776): a boolean followed by a VarInt-counted list of component wire ids.</summary>
    public static ItemComponentCodec TooltipDisplay { get; } = new TooltipDisplayCodecImpl();

    /// <summary><c>minecraft:custom_model_data</c> on 770/776: four lists (float, bool, string, int). The colors list uses fixed four-byte integers rather than VarInts.</summary>
    public static ItemComponentCodec CustomModelData { get; } = new CustomModelDataCodecImpl();

    /// <summary><c>minecraft:custom_model_data</c> on 766/767: a single VarInt. It lands in the model's float list as one entry, which is what the shared record documents, and re-encodes to the same VarInt.</summary>
    public static ItemComponentCodec CustomModelDataV1_20_5 { get; } = new LegacyCustomModelDataCodecImpl();

    /// <summary><c>minecraft:creative_slot_lock</c>: zero payload bytes on all four eras.</summary>
    public static ItemComponentCodec CreativeSlotLock { get; } = new UnitMarkerCodec(DataComponents.CreativeSlotLock);

    /// <summary><c>minecraft:glider</c> (770/776): zero payload bytes.</summary>
    public static ItemComponentCodec Glider { get; } = new UnitMarkerCodec(DataComponents.Glider);

    /// <summary><c>minecraft:hide_tooltip</c> (766/767): zero payload bytes.</summary>
    public static ItemComponentCodec HideTooltip { get; } = new UnitMarkerCodec(DataComponents.HideTooltip);

    /// <summary><c>minecraft:hide_additional_tooltip</c> (766/767): zero payload bytes.</summary>
    public static ItemComponentCodec HideAdditionalTooltip { get; } = new UnitMarkerCodec(DataComponents.HideAdditionalTooltip);

    /// <summary><c>minecraft:fire_resistant</c> (766/767): zero payload bytes.</summary>
    public static ItemComponentCodec FireResistant { get; } = new UnitMarkerCodec(DataComponents.FireResistant);

    // These six components carry exactly one unnamed-root network NBT tag. Keeping the tag verbatim is byte-exact in both directions on all four eras.

    /// <summary><c>minecraft:map_decorations</c>: one network NBT tag.</summary>
    public static ItemComponentCodec MapDecorations { get; } = new NbtPayloadCodec(DataComponents.MapDecorations);

    /// <summary><c>minecraft:intangible_projectile</c>: one network NBT tag (an empty compound).</summary>
    public static ItemComponentCodec IntangibleProjectile { get; } = new NbtPayloadCodec(DataComponents.IntangibleProjectile);

    /// <summary><c>minecraft:debug_stick_state</c>: one network NBT tag.</summary>
    public static ItemComponentCodec DebugStickState { get; } = new NbtPayloadCodec(DataComponents.DebugStickState);

    /// <summary><c>minecraft:recipes</c>: one network NBT tag.</summary>
    public static ItemComponentCodec Recipes { get; } = new NbtPayloadCodec(DataComponents.Recipes);

    /// <summary><c>minecraft:lock</c>: one network NBT tag.</summary>
    public static ItemComponentCodec Lock { get; } = new NbtPayloadCodec(DataComponents.Lock);

    /// <summary><c>minecraft:container_loot</c>: one network NBT tag.</summary>
    public static ItemComponentCodec ContainerLoot { get; } = new NbtPayloadCodec(DataComponents.ContainerLoot);

    /// <summary>The dye-color valued components (770/776, plus <c>minecraft:dye</c> on 776 only) carry one VarInt color id. They ride spawn eggs, bucketed mobs, tamed-pet items and shulker boxes.</summary>
    public static ItemComponentCodec WolfCollar { get; } = MakeDyeColor(DataComponents.WolfCollar);

    /// <summary><c>minecraft:cat/collar</c>: a VarInt dye-color id.</summary>
    public static ItemComponentCodec CatCollar { get; } = MakeDyeColor(DataComponents.CatCollar);

    /// <summary><c>minecraft:sheep/color</c>: a VarInt dye-color id.</summary>
    public static ItemComponentCodec SheepColor { get; } = MakeDyeColor(DataComponents.SheepColor);

    /// <summary><c>minecraft:shulker/color</c>: a VarInt dye-color id.</summary>
    public static ItemComponentCodec ShulkerColor { get; } = MakeDyeColor(DataComponents.ShulkerColor);

    /// <summary><c>minecraft:tropical_fish/base_color</c>: a VarInt dye-color id.</summary>
    public static ItemComponentCodec TropicalFishBaseColor { get; } = MakeDyeColor(DataComponents.TropicalFishBaseColor);

    /// <summary><c>minecraft:tropical_fish/pattern_color</c>: a VarInt dye-color id.</summary>
    public static ItemComponentCodec TropicalFishPatternColor { get; } = MakeDyeColor(DataComponents.TropicalFishPatternColor);

    /// <summary><c>minecraft:dye</c> (776 only): a VarInt dye-color id.</summary>
    public static ItemComponentCodec Dye { get; } = MakeDyeColor(DataComponents.Dye);

    /// <summary><c>minecraft:minimum_attack_charge</c> (776): one float.</summary>
    public static ItemComponentCodec MinimumAttackCharge { get; } = new FloatCodec(
        DataComponents.MinimumAttackCharge,
        static f => new MinimumAttackChargeComponent(f),
        static v => ((MinimumAttackChargeComponent)v).Value);

    /// <summary><c>minecraft:additional_trade_cost</c> (776): one VarInt.</summary>
    public static ItemComponentCodec AdditionalTradeCost { get; } = new VarIntCodec(
        DataComponents.AdditionalTradeCost,
        static i => new AdditionalTradeCostComponent(i),
        static v => ((AdditionalTradeCostComponent)v).Value);

    private static ItemComponentCodec MakeDyeColor(DataComponentType type) => new DyeColorCodec(
        type,
        static name => new DyeColorValueComponent(name),
        static v => ((DyeColorValueComponent)v).Color);

    internal static NbtCompound AsCompound(NbtTag tag) => tag as NbtCompound ?? new NbtCompound();

    // Concrete codec kinds

    private sealed class VarIntCodec(DataComponentType type, Func<int, object> wrap, Func<object, int> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) => wrap(reader.ReadVarInt());

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteVarInt(unwrap(value));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => ops.Int(unwrap(value));
    }

    private sealed class PotDecorationsCodecImpl() : ItemComponentCodec(DataComponents.PotDecorations)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int count = reader.ReadVarInt();
            if (count is < 0 or > 4)
                throw new ProtocolViolationException(
                    $"pot_decorations carries {count} entries; the wire caps the list at 4.");

            var ids = new int[count];
            for (int i = 0; i < count; i++)
                ids[i] = reader.ReadVarInt();

            return new PotDecorationsComponent(ids);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            IReadOnlyList<int> ids = ((PotDecorationsComponent)value).SherdItemIds;
            writer.WriteVarInt(ids.Count);
            foreach (int id in ids)
                writer.WriteVarInt(id);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            // The hashed data form is a list of item identifier strings, so the ids resolve through the session registry here. Hashing only runs on the 1.21.5+ click send path, whose stacks were decoded under the same registry, so resolution succeeding is the normal case; an unresolvable id is a wire/registry mismatch worth surfacing.
            IReadOnlyList<int> ids = ((PotDecorationsComponent)value).SherdItemIds;
            var hashes = new int[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                if (!context.Registries.Items.TryGetKey(ids[i], out Identifier itemId))
                    throw new ProtocolViolationException(
                        $"pot_decorations item id {ids[i]} is not in the item registry, so its hashed-stack form cannot be built.");

                hashes[i] = ops.String(itemId.ToString());
            }

            return ops.List(hashes);
        }
    }

    /// <summary><c>minecraft:pot_decorations</c> from 26.3: exactly four optional item-stack templates in face order (back, left, right, front).</summary>
    private sealed class PotDecorationsStacksCodecImpl() : NestedStackComponentCodec(DataComponents.PotDecorations)
    {
        private static readonly string[] FaceNames = ["back", "left", "right", "front"];

        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            var faces = new ItemStack[FaceNames.Length];
            var ids = new List<int>(FaceNames.Length);
            for (int i = 0; i < faces.Length; i++)
            {
                faces[i] = ItemStackCodecs.ReadOptionalTemplateStack(ref reader, context, Table);
                if (!faces[i].IsEmpty)
                    ids.Add(faces[i].Item.NetworkId);
            }

            return new PotDecorationsComponent(ids) { FaceStacks = faces };
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            if (value is not PotDecorationsComponent decorations
                || decorations.FaceStacks is not { Count: 4 } faces)
                throw new ProtocolViolationException(
                    "26.3 pot_decorations requires four face stacks (back, left, right, front); a bare sherd-id list has no wire form on this era.");

            foreach (ItemStack stack in faces)
                ItemStackCodecs.WriteOptionalTemplateStack(ref writer, stack, context, Table);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            if (value is not PotDecorationsComponent decorations
                || decorations.FaceStacks is not { Count: 4 } faces)
                throw new ProtocolViolationException(
                    "26.3 pot_decorations requires four face stacks (back, left, right, front) to hash.");

            var entries = new List<(int, int)>(faces.Count);
            for (int i = 0; i < faces.Count; i++)
            {
                ItemStack stack = faces[i];
                if (!stack.IsEmpty)
                    entries.Add((ops.String(FaceNames[i]), ItemStackCodecs.HashTemplateStack(ops, stack, Table, context)));
            }

            return ops.Map(entries);
        }
    }

    private sealed class IntColorCodec(DataComponentType type, Func<int, object> wrap, Func<object, int> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) => wrap(reader.ReadInt());

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteInt(unwrap(value));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => ops.Int(unwrap(value));
    }

    private sealed class UnitCodec(DataComponentType type, Func<object> make) : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) => make();

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
        }

        // A unit value hashes as the empty map.
        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => ops.EmptyMap;
    }

    private sealed class ComponentCodec(DataComponentType type, ComponentWireEra era, Func<Component, object> wrap, Func<object, Component> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            wrap(ItemCodecPrimitives.ReadNetworkComponent(ref reader, era));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            ItemCodecPrimitives.WriteNetworkComponent(ref writer, unwrap(value), era);

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            ItemCodecPrimitives.HashNbt(ops, ComponentNbt.To(unwrap(value), era));
    }

    private sealed class LoreCodecImpl(ComponentWireEra era) : ItemComponentCodec(DataComponents.Lore)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            ComponentWireEra e = era;
            Component[] lines = reader.ReadList((ref PacketReader r) => ItemCodecPrimitives.ReadNetworkComponent(ref r, e));
            return new LoreComponent(lines);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var lore = (LoreComponent)value;
            ComponentWireEra e = era;
            writer.WriteList(lore.Lines, (ref PacketWriter w, Component c) => ItemCodecPrimitives.WriteNetworkComponent(ref w, c, e));
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var lore = (LoreComponent)value;
            var hashes = new int[lore.Lines.Count];
            for (int i = 0; i < hashes.Length; i++)
                hashes[i] = ItemCodecPrimitives.HashNbt(ops, ComponentNbt.To(lore.Lines[i], era));

            return ops.List(hashes);
        }
    }

    private sealed class RarityCodecImpl() : ItemComponentCodec(DataComponents.Rarity)
    {
        private static readonly string[] Names = ["common", "uncommon", "rare", "epic"];

        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int id = reader.ReadVarInt();
            return new RarityComponent(id >= 0 && id < Names.Length ? Names[id] : "common");
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteVarInt(IndexOf(((RarityComponent)value).Rarity));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            ops.String(((RarityComponent)value).Rarity);

        private static int IndexOf(string rarity)
        {
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i], rarity, StringComparison.Ordinal))
                    return i;

            return 0;
        }
    }

    /// <summary>
    /// <c>minecraft:enchantments</c> / <c>minecraft:stored_enchantments</c>. Two era shapes, and the boundary is exactly 770.
    /// <list type="bullet">
    /// <item>766-769 (<paramref name="hasShowInTooltip"/> true): a VarInt-counted map of enchantment
    /// holder ids to VarInt levels, followed by a tooltip boolean.</item>
    /// <item>770+: the map alone; <c>show_in_tooltip</c> moved to the separate
    /// <c>tooltip_display</c> component.</item>
    /// </list>
    /// The trailing bool is a pure LENGTH difference on an id that does not move (9 on 766/767, 10 on 768/769), so only a frame-length or cross-era assertion can separate the two codecs; a round trip through either one agrees with itself.
    /// </summary>
    /// <remarks>The data form used for hashed slots moved with the wire form. Before protocol 770 it is <c>{ levels: map, show_in_tooltip: bool }</c>; from 770 it is the bare map. Since hashed stacks exist only from 770, a live hash must never use the wrapper shape.</remarks>
    private sealed class EnchantmentsCodecImpl(DataComponentType type, bool stored, bool hasShowInTooltip = false)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int count = reader.ReadVarInt();
            if (count < 0 || count > reader.Remaining + 1)
                throw new ProtocolViolationException(
                    $"Enchantment map length {count} is implausible for {reader.Remaining} remaining bytes.");

            var list = new List<EnchantmentInstance>(count);
            for (int i = 0; i < count; i++)
            {
                int enchId = ItemCodecPrimitives.ReadHolderId(ref reader);
                int level = reader.ReadVarInt();
                list.Add(new EnchantmentInstance(ItemCodecPrimitives.ResolveEnchantment(context, enchId), level));
            }

            bool showInTooltip = !hasShowInTooltip || reader.ReadBool();
            return stored
                ? new StoredEnchantmentsComponent(list, showInTooltip)
                : new EnchantmentsComponent(list, showInTooltip);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            IReadOnlyList<EnchantmentInstance> enchantments = Enchantments(value);
            writer.WriteVarInt(enchantments.Count);
            foreach (EnchantmentInstance instance in enchantments)
            {
                ItemCodecPrimitives.WriteHolderId(ref writer, instance.Enchantment.NetworkId);
                writer.WriteVarInt(instance.Level);
            }

            if (hasShowInTooltip)
                writer.WriteBool(ShowInTooltip(value));

        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<EnchantmentInstance> enchantments = Enchantments(value);
            var entries = new List<(int, int)>(enchantments.Count);
            foreach (EnchantmentInstance instance in enchantments)
                entries.Add((ops.String(instance.Enchantment.Id.ToString()), ops.Int(instance.Level)));

            int levels = ops.Map(entries);
            if (!hasShowInTooltip)
            {
                // From protocol 770, the data form is the bare unbounded map; this is the only era a hashed stack exists on.
                return levels;
            }

            // Before protocol 770, the data form is { levels: .., show_in_tooltip: .. }, with the flag omitted at its default of true. It is unreachable on the wire because hashed stacks begin at protocol 770, but is modeled for completeness.
            var wrapped = new List<(int, int)> { (ops.String("levels"), levels) };
            if (!ShowInTooltip(value))
                wrapped.Add((ops.String("show_in_tooltip"), ops.Boolean(false)));

            return ops.Map(wrapped);
        }

        private IReadOnlyList<EnchantmentInstance> Enchantments(object value) => stored
            ? ((StoredEnchantmentsComponent)value).Enchantments
            : ((EnchantmentsComponent)value).Enchantments;

        private bool ShowInTooltip(object value) => stored
            ? ((StoredEnchantmentsComponent)value).ShowInTooltip
            : ((EnchantmentsComponent)value).ShowInTooltip;
    }

    private sealed class FoodCodecImpl() : ItemComponentCodec(DataComponents.Food)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int nutrition = reader.ReadVarInt();
            float saturation = reader.ReadFloat();
            bool canAlwaysEat = reader.ReadBool();
            return new FoodComponent(nutrition, saturation, canAlwaysEat);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var food = (FoodComponent)value;
            writer.WriteVarInt(food.Nutrition);
            writer.WriteFloat(food.Saturation);
            writer.WriteBool(food.CanAlwaysEat);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var food = (FoodComponent)value;
            var entries = new List<(int, int)>
            {
                (ops.String("nutrition"), ops.Int(food.Nutrition)),
                (ops.String("saturation"), ops.Float(food.Saturation)),
            };
            if (food.CanAlwaysEat)
                entries.Add((ops.String("can_always_eat"), ops.Boolean(true)));

            return ops.Map(entries);
        }
    }

    private sealed class BlockStateCodecImpl() : ItemComponentCodec(DataComponents.BlockState)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int count = reader.ReadVarInt();
            var map = new Dictionary<string, string>(count);
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                map[key] = reader.ReadString();
            }

            return new BlockStateComponent(map);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var props = ((BlockStateComponent)value).Properties;
            writer.WriteVarInt(props.Count);
            foreach (KeyValuePair<string, string> pair in props)
            {
                writer.WriteString(pair.Key);
                writer.WriteString(pair.Value);
            }
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var props = ((BlockStateComponent)value).Properties;
            var entries = new List<(int, int)>(props.Count);
            foreach (KeyValuePair<string, string> pair in props)
                entries.Add((ops.String(pair.Key), ops.String(pair.Value)));

            return ops.Map(entries);
        }
    }

    /// <summary><c>minecraft:bundle_contents</c> and <c>minecraft:charged_projectiles</c>: a VarInt-counted list of NON-optional nested stacks. The list framing never moved; the element did, at 26.1.</summary>
    /// <remarks>Protocols through 774 use count-first stack elements; 775-776 use template stack elements. Protocol 776 caps the projectile list at 1024 without changing its framing. Empty elements are invalid in both forms, so the template form needs no empty sentinel here.</remarks>
    /// <summary>A component whose whole payload is ONE bare nested stack, with no count prefix and no optional wrapper (<c>use_remainder</c>, <c>sulfur_cube_content</c>). The era's stack form is the only axis.</summary>
    /// <remarks>Both element forms reject an empty stack. <see cref="ItemStackCodecs.WriteTemplateStack"/> already faults on empty, so nothing here has to invent a sentinel.</remarks>
    private sealed class SingleStackCodec(
        DataComponentType type,
        Func<ItemStack, object> wrap,
        Func<object, ItemStack> unwrap,
        NestedStackForm form)
        : NestedStackComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            wrap(form == NestedStackForm.Template
                ? ItemStackCodecs.ReadTemplateStack(ref reader, context, Table)
                : ItemStackCodecs.ReadModernStack(ref reader, context, Table));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            ItemStack stack = unwrap(value);
            if (form == NestedStackForm.Template)
                ItemStackCodecs.WriteTemplateStack(ref writer, stack, context, Table);

            else
                ItemStackCodecs.WriteModernStack(ref writer, stack, context, Table);

        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            ItemStack stack = unwrap(value);
            return form == NestedStackForm.Template
                ? ItemStackCodecs.HashTemplateStack(ops, stack, Table, context)
                : ItemStackCodecs.HashStack(ops, stack, Table, context);
        }
    }

    private sealed class StackListCodec(
        DataComponentType type,
        Func<IReadOnlyList<ItemStack>, object> wrap,
        Func<object, IReadOnlyList<ItemStack>> unwrap,
        NestedStackForm form)
        : NestedStackComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            var stacks = new List<ItemStack>();
            int count = reader.ReadVarInt();
            for (int i = 0; i < count; i++)
                stacks.Add(form == NestedStackForm.Template
                    ? ItemStackCodecs.ReadTemplateStack(ref reader, context, Table)
                    : ItemStackCodecs.ReadModernStack(ref reader, context, Table));

            return wrap(stacks);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            IReadOnlyList<ItemStack> stacks = unwrap(value);
            writer.WriteVarInt(stacks.Count);
            foreach (ItemStack stack in stacks)
                if (form == NestedStackForm.Template)
                    ItemStackCodecs.WriteTemplateStack(ref writer, stack, context, Table);

                else
                    ItemStackCodecs.WriteModernStack(ref writer, stack, context, Table);

        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<ItemStack> stacks = unwrap(value);
            var hashes = new int[stacks.Count];
            for (int i = 0; i < hashes.Length; i++)
                hashes[i] = form == NestedStackForm.Template
                    ? ItemStackCodecs.HashTemplateStack(ops, stacks[i], Table, context)
                    : ItemStackCodecs.HashStack(ops, stacks[i], Table, context);

            return ops.List(hashes);
        }
    }

    /// <summary><c>minecraft:container</c>: a VarInt-counted, slot-indexed list where an empty slot is on the wire. The list framing never moved; how an element spells "absent" did, at 26.1.</summary>
    /// <remarks>Through protocol 774 an empty slot is the single VarInt 0 of the count-first form. On 775-776 the template has no sentinel, so an empty slot is a false presence boolean instead. Both are one byte for an empty slot (0x00), which is precisely why a container of empty slots cannot separate the two eras and only a slot with a real item can.</remarks>
    private sealed class ContainerCodecImpl(NestedStackForm form) : NestedStackComponentCodec(DataComponents.Container)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int count = reader.ReadVarInt();
            var entries = new List<ContainerSlotEntry>(count);
            for (int slot = 0; slot < count; slot++)
            {
                ItemStack stack = form == NestedStackForm.Template
                    ? ItemStackCodecs.ReadOptionalTemplateStack(ref reader, context, Table)
                    : ItemStackCodecs.ReadModernStack(ref reader, context, Table);
                entries.Add(new ContainerSlotEntry(slot, stack));
            }

            return new ContainerComponent(entries);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            IReadOnlyList<ContainerSlotEntry> entries = ((ContainerComponent)value).Items;
            int length = 0;
            foreach (ContainerSlotEntry entry in entries)
                length = Math.Max(length, entry.Slot + 1);

            var byIndex = new ItemStack[length];
            for (int i = 0; i < length; i++)
                byIndex[i] = ItemStack.Empty;

            foreach (ContainerSlotEntry entry in entries)
                byIndex[entry.Slot] = entry.Item;

            writer.WriteVarInt(length);
            foreach (ItemStack stack in byIndex)
                if (form == NestedStackForm.Template)
                    ItemStackCodecs.WriteOptionalTemplateStack(ref writer, stack, context, Table);

                else
                    ItemStackCodecs.WriteModernStack(ref writer, stack, context, Table);

        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<ContainerSlotEntry> entries = ((ContainerComponent)value).Items;
            var hashes = new List<int>(entries.Count);
            foreach (ContainerSlotEntry entry in entries)
                if (!entry.Item.IsEmpty)
                    hashes.Add(form == NestedStackForm.Template
                        ? ItemStackCodecs.HashTemplateStack(ops, entry.Item, Table, context)
                        : ItemStackCodecs.HashStack(ops, entry.Item, Table, context));

            return ops.List(hashes);
        }
    }

    private sealed class PotionContentsCodecImpl() : ItemComponentCodec(DataComponents.PotionContents)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            // The potion holder id resolves against a registry that is not surfaced here, so the raw id round-trips through a synthetic umpk:potion_{id} identifier.
            Identifier? potionId = reader.ReadBool()
                ? new Identifier("umpk", $"potion_{ItemCodecPrimitives.ReadHolderId(ref reader)}")
                : null;
            int? customColor = reader.ReadBool() ? reader.ReadInt() : null;
            MobEffectDetail[] effects = reader.ReadList(static (ref PacketReader r) => ReadEffect(ref r));
            string? customName = reader.ReadBool() ? reader.ReadString() : null;
            return new PotionContentsComponent(potionId, customColor, effects, customName);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var potion = (PotionContentsComponent)value;
            if (potion.PotionId is { } id)
            {
                writer.WriteBool(true);
                ItemCodecPrimitives.WriteHolderId(ref writer, ParsePotionId(id));
            }
            else
                writer.WriteBool(false);

            writer.WriteOptionalStruct(potion.CustomColor, static (ref PacketWriter w, int c) => w.WriteInt(c));
            writer.WriteList(potion.CustomEffects, static (ref PacketWriter w, MobEffectDetail e) => WriteEffect(ref w, e));
            writer.WriteOptional(potion.CustomName, static (ref PacketWriter w, string s) => w.WriteString(s));
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var potion = (PotionContentsComponent)value;
            var entries = new List<(int, int)>();
            if (potion.PotionId is { } id)
                entries.Add((ops.String("potion"), ops.String(id.ToString())));

            if (potion.CustomColor is { } color)
                entries.Add((ops.String("custom_color"), ops.Int(color)));

            if (potion.CustomEffects.Count > 0)
            {
                var effectHashes = new int[potion.CustomEffects.Count];
                for (int i = 0; i < effectHashes.Length; i++)
                    effectHashes[i] = HashEffect(ops, potion.CustomEffects[i]);

                entries.Add((ops.String("custom_effects"), ops.List(effectHashes)));
            }

            if (potion.CustomName is { } name)
                entries.Add((ops.String("custom_name"), ops.String(name)));

            return ops.Map(entries);
        }

        internal static MobEffectDetail ReadEffect(ref PacketReader reader)
        {
            int effectId = ItemCodecPrimitives.ReadHolderId(ref reader);
            int amplifier = reader.ReadVarInt();
            int duration = reader.ReadVarInt();
            bool ambient = reader.ReadBool();
            bool showParticles = reader.ReadBool();
            bool showIcon = reader.ReadBool();
            if (reader.ReadBool())
            {
                // Hidden nested effect: consume it to stay frame-exact (not modeled separately).
                _ = ReadEffectDetailsOnly(ref reader);
            }

            return new MobEffectDetail(new Identifier("umpk", $"effect_{effectId}"), amplifier, duration, ambient, showParticles, showIcon);
        }

        private static bool ReadEffectDetailsOnly(ref PacketReader reader)
        {
            _ = reader.ReadVarInt();
            _ = reader.ReadVarInt();
            _ = reader.ReadBool();
            _ = reader.ReadBool();
            _ = reader.ReadBool();
            if (reader.ReadBool())
                _ = ReadEffectDetailsOnly(ref reader);

            return true;
        }

        internal static void WriteEffect(ref PacketWriter writer, MobEffectDetail effect)
        {
            // The effect id here is the model's identifier; if it is a synthetic umpk id we cannot recover a holder, so we write 0. Fully typed effect ids round-trip through the mob-effect registry at the call site; potion contents retain the raw id.
            int id = TryParseSynthetic(effect.Effect, out int raw) ? raw : 0;
            ItemCodecPrimitives.WriteHolderId(ref writer, id);
            writer.WriteVarInt(effect.Amplifier);
            writer.WriteVarInt(effect.Duration);
            writer.WriteBool(effect.Ambient);
            writer.WriteBool(effect.ShowParticles);
            writer.WriteBool(effect.ShowIcon);
            writer.WriteBool(false);
        }

        private static int HashEffect(in HashOps ops, MobEffectDetail effect)
        {
            var entries = new List<(int, int)>
            {
                (ops.String("id"), ops.String(effect.Effect.ToString())),
            };
            if (effect.Amplifier != 0)
                entries.Add((ops.String("amplifier"), ops.Int(effect.Amplifier)));

            if (effect.Duration != 0)
                entries.Add((ops.String("duration"), ops.Int(effect.Duration)));

            return ops.Map(entries);
        }

        private static bool TryParseSynthetic(Identifier id, out int raw)
        {
            raw = 0;
            const string prefix = "effect_";
            if (string.Equals(id.Namespace, "umpk", StringComparison.Ordinal) && id.Path.StartsWith(prefix, StringComparison.Ordinal))
                return int.TryParse(id.Path.AsSpan(prefix.Length), out raw);

            return false;
        }

        private static int ParsePotionId(Identifier id)
        {
            const string prefix = "potion_";
            return string.Equals(id.Namespace, "umpk", StringComparison.Ordinal) && id.Path.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(id.Path.AsSpan(prefix.Length), out int raw)
                ? raw
                : 0;
        }
    }

    private sealed class ProfileCodecImpl() : ItemComponentCodec(DataComponents.Profile)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            string? name = reader.ReadBool() ? reader.ReadString(16) : null;
            Guid? id = reader.ReadBool() ? reader.ReadUuid() : null;
            int propertyCount = reader.ReadVarInt();
            var properties = new List<Umpk.Game.Items.Components.ProfileProperty>(propertyCount);
            for (int i = 0; i < propertyCount; i++)
            {
                string propName = reader.ReadString();
                string propValue = reader.ReadString();
                string? signature = reader.ReadBool() ? reader.ReadString() : null;
                properties.Add(new Umpk.Game.Items.Components.ProfileProperty(propName, propValue, signature));
            }

            return new ProfileComponent(new ResolvableProfile(name, id, properties));
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            ResolvableProfile profile = ((ProfileComponent)value).Profile;
            writer.WriteOptional(profile.Name, static (ref PacketWriter w, string s) => w.WriteString(s, 16));
            writer.WriteOptionalStruct(profile.Id, static (ref PacketWriter w, Guid g) => w.WriteUuid(g));
            writer.WriteVarInt(profile.Properties.Count);
            foreach (Umpk.Game.Items.Components.ProfileProperty property in profile.Properties)
            {
                writer.WriteString(property.Name);
                writer.WriteString(property.Value);
                writer.WriteOptional(property.Signature, static (ref PacketWriter w, string s) => w.WriteString(s));
            }
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            ResolvableProfile profile = ((ProfileComponent)value).Profile;
            var entries = new List<(int, int)>();
            if (profile.Name is { } name)
                entries.Add((ops.String("name"), ops.String(name)));

            if (profile.Id is { } id)
                entries.Add((ops.String("id"), HashUuid(ops, id)));

            return ops.Map(entries);
        }

        private static int HashUuid(in HashOps ops, Guid id)
        {
            (long most, long least) = UuidCodec.ToMostLeast(id);
            return ops.IntList([(int)(most >> 32), (int)most, (int)(least >> 32), (int)least]);
        }
    }

    /// <summary><c>minecraft:profile</c> on protocols 773-776.</summary>
    /// <remarks>
    /// Three things separate this from the 1.20.5-1.21.8 codec, and all three are on the wire:
    /// <list type="bullet">
    /// <item>a leading discriminator boolean, with true selecting the resolved branch;</item>
    /// <item>the resolved branch is UUID, player name, then profile properties; the UUID comes first
    /// and both fields are mandatory, the reverse order and optionality of the partial branch UMPK already modeled;</item>
    /// <item>a trailing skin patch: three optional asset identifiers plus an optional model boolean.</item>
    /// </list>
    /// Binding the 1.21.5 codec here read the discriminator bool as the partial branch's name-present flag, so every player head on 773+ desynchronized the rest of the component list.
    /// </remarks>
    private sealed class ProfileV1_21_9CodecImpl() : ItemComponentCodec(DataComponents.Profile)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            bool resolved = reader.ReadBool();
            string? name;
            Guid? id;
            if (resolved)
            {
                id = reader.ReadUuid();
                name = reader.ReadString(16);
            }
            else
            {
                name = reader.ReadBool() ? reader.ReadString(16) : null;
                id = reader.ReadBool() ? reader.ReadUuid() : null;
            }

            IReadOnlyList<Umpk.Game.Items.Components.ProfileProperty> properties = ReadProperties(ref reader);
            PlayerSkinPatch patch = ReadSkinPatch(ref reader);
            return new ProfileComponent(new ResolvableProfile(name, id, properties, resolved, patch));
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            ResolvableProfile profile = ((ProfileComponent)value).Profile;

            // The resolved (left) branch has no optionality: both fields are mandatory there, so a profile that claims to be resolved without them cannot be spelled and faults rather than silently degrading to the partial branch, which the server would read as a different value.
            if (profile.Resolved)
            {
                if (profile.Id is not { } resolvedId || profile.Name is not { } resolvedName)
                    throw new ProtocolViolationException(
                        "A resolved minecraft:profile needs both an id and a name on this protocol.");

                writer.WriteBool(true);
                writer.WriteUuid(resolvedId);
                writer.WriteString(resolvedName, 16);
            }
            else
            {
                writer.WriteBool(false);
                writer.WriteOptional(profile.Name, static (ref PacketWriter w, string s) => w.WriteString(s, 16));
                writer.WriteOptionalStruct(profile.Id, static (ref PacketWriter w, Guid g) => w.WriteUuid(g));
            }

            WriteProperties(ref writer, profile.Properties);
            WriteSkinPatch(ref writer, profile.SkinPatch ?? PlayerSkinPatch.Empty);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            // The resolved and partial profile forms merge into one map, so name and id use the same keys either way. Skin patch fields use their map names. Properties are omitted because a disagreement costs a server-driven resync rather than the session.
            ResolvableProfile profile = ((ProfileComponent)value).Profile;
            var entries = new List<(int, int)>();
            if (profile.Name is { } name)
                entries.Add((ops.String("name"), ops.String(name)));

            if (profile.Id is { } id)
            {
                (long most, long least) = UuidCodec.ToMostLeast(id);
                entries.Add((ops.String("id"), ops.IntList([(int)(most >> 32), (int)most, (int)(least >> 32), (int)least])));
            }

            if (profile.SkinPatch is { } patch)
            {
                AddAsset(ops, entries, "texture", patch.Body);
                AddAsset(ops, entries, "cape", patch.Cape);
                AddAsset(ops, entries, "elytra", patch.Elytra);
                if (patch.SlimModel is { } slim)
                    entries.Add((ops.String("model"), ops.String(slim ? "slim" : "wide")));

            }

            return ops.Map(entries);
        }

        private static void AddAsset(in HashOps ops, List<(int, int)> entries, string key, Identifier? asset)
        {
            if (asset is { } id)
                entries.Add((ops.String(key), ops.String(id.ToString())));

        }

        private static IReadOnlyList<Umpk.Game.Items.Components.ProfileProperty> ReadProperties(ref PacketReader reader)
        {
            int propertyCount = reader.ReadVarInt();
            var properties = new List<Umpk.Game.Items.Components.ProfileProperty>(propertyCount);
            for (int i = 0; i < propertyCount; i++)
            {
                string propName = reader.ReadString();
                string propValue = reader.ReadString();
                string? signature = reader.ReadBool() ? reader.ReadString() : null;
                properties.Add(new Umpk.Game.Items.Components.ProfileProperty(propName, propValue, signature));
            }

            return properties;
        }

        private static void WriteProperties(
            ref PacketWriter writer, IReadOnlyList<Umpk.Game.Items.Components.ProfileProperty> properties)
        {
            writer.WriteVarInt(properties.Count);
            foreach (Umpk.Game.Items.Components.ProfileProperty property in properties)
            {
                writer.WriteString(property.Name);
                writer.WriteString(property.Value);
                writer.WriteOptional(property.Signature, static (ref PacketWriter w, string s) => w.WriteString(s));
            }
        }

        private static PlayerSkinPatch ReadSkinPatch(ref PacketReader reader)
        {
            Identifier? body = ReadOptionalAsset(ref reader);
            Identifier? cape = ReadOptionalAsset(ref reader);
            Identifier? elytra = ReadOptionalAsset(ref reader);
            bool? model = reader.ReadBool() ? reader.ReadBool() : null;
            return new PlayerSkinPatch(body, cape, elytra, model);
        }

        private static void WriteSkinPatch(ref PacketWriter writer, PlayerSkinPatch patch)
        {
            WriteOptionalAsset(ref writer, patch.Body);
            WriteOptionalAsset(ref writer, patch.Cape);
            WriteOptionalAsset(ref writer, patch.Elytra);
            writer.WriteOptionalStruct(patch.SlimModel, static (ref PacketWriter w, bool slim) => w.WriteBool(slim));
        }

        private static Identifier? ReadOptionalAsset(ref PacketReader reader) =>
            reader.ReadBool() ? Identifier.Parse(reader.ReadString()) : null;

        private static void WriteOptionalAsset(ref PacketWriter writer, Identifier? asset)
        {
            if (asset is { } id)
            {
                writer.WriteBool(true);
                writer.WriteString(id.ToString());
                return;
            }

            writer.WriteBool(false);
        }
    }

    private sealed class WritableBookCodecImpl() : ItemComponentCodec(DataComponents.WritableBookContent)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            BookPage[] pages = reader.ReadList(static (ref PacketReader r) =>
            {
                string raw = r.ReadString(1024);
                string? filtered = r.ReadBool() ? r.ReadString(1024) : null;
                return new BookPage(raw, filtered);
            });
            return new WritableBookContentComponent(pages);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var content = (WritableBookContentComponent)value;
            writer.WriteList(content.Pages, static (ref PacketWriter w, BookPage p) =>
            {
                w.WriteString(p.Raw, 1024);
                w.WriteOptional(p.Filtered, static (ref PacketWriter fw, string s) => fw.WriteString(s, 1024));
            });
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var content = (WritableBookContentComponent)value;
            var hashes = new int[content.Pages.Count];
            for (int i = 0; i < hashes.Length; i++)
                hashes[i] = HashFilterable(ops, content.Pages[i]);

            return ops.Map([(ops.String("pages"), ops.List(hashes))]);
        }

        private static int HashFilterable(in HashOps ops, BookPage page) =>
            page.Filtered is null ? ops.String(page.Raw) : ops.Map([(ops.String("raw"), ops.String(page.Raw)), (ops.String("filtered"), ops.String(page.Filtered))]);
    }

    private sealed class WrittenBookCodecImpl(ComponentWireEra era) : ItemComponentCodec(DataComponents.WrittenBookContent)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            string title = reader.ReadString(32);
            string? filteredTitle = reader.ReadBool() ? reader.ReadString(32) : null;
            string author = reader.ReadString();
            int generation = reader.ReadVarInt();
            ComponentWireEra e = era;
            WrittenBookPage[] pages = reader.ReadList((ref PacketReader r) =>
            {
                Component raw = ItemCodecPrimitives.ReadNetworkComponent(ref r, e);
                Component? filtered = r.ReadBool() ? ItemCodecPrimitives.ReadNetworkComponent(ref r, e) : null;
                return new WrittenBookPage(raw, filtered);
            });
            bool resolved = reader.ReadBool();
            return new WrittenBookContentComponent(title, author, generation, pages, resolved, filteredTitle);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var content = (WrittenBookContentComponent)value;
            writer.WriteString(content.Title, 32);
            writer.WriteOptional(content.FilteredTitle, static (ref PacketWriter fw, string s) => fw.WriteString(s, 32));
            writer.WriteString(content.Author);
            writer.WriteVarInt(content.Generation);
            ComponentWireEra e = era;
            writer.WriteList(content.Pages, (ref PacketWriter w, WrittenBookPage p) =>
            {
                ItemCodecPrimitives.WriteNetworkComponent(ref w, p.Raw, e);
                if (p.Filtered is { } f)
                {
                    w.WriteBool(true);
                    ItemCodecPrimitives.WriteNetworkComponent(ref w, f, e);
                }
                else
                    w.WriteBool(false);

            });
            writer.WriteBool(content.Resolved);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var content = (WrittenBookContentComponent)value;
            var entries = new List<(int, int)>
            {
                (ops.String("title"), ops.String(content.Title)),
                (ops.String("author"), ops.String(content.Author)),
            };
            if (content.Generation != 0)
                entries.Add((ops.String("generation"), ops.Int(content.Generation)));

            return ops.Map(entries);
        }
    }

    /// <summary><c>minecraft:fireworks</c>: a VarInt flight duration then a list of explosion layers.</summary>
    /// <remarks>
    /// The layout is unchanged on protocols 766-776, so one codec serves the entire band.
    /// <para>In the hashed data form, <c>flight_duration</c> is a byte rather than an integer and is omitted when zero. <c>explosions</c> is likewise omitted when empty.</para>
    /// <para>The explosion list has a 256-element limit in both directions; see <see cref="MaxExplosions"/> for why decoding and encoding both need it.</para>
    /// </remarks>
    private sealed class FireworksCodecImpl() : ItemComponentCodec(DataComponents.Fireworks)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int flightDuration = reader.ReadVarInt();
            int count = ReadCount(ref reader, MaxExplosions, "minecraft:fireworks explosions");
            var explosions = new List<FireworkExplosionValue>(Math.Min(count, MaxInitialCollectionSize));
            for (int i = 0; i < count; i++)
                explosions.Add(ReadExplosion(ref reader));

            return new FireworksComponent(flightDuration, explosions);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var fireworks = (FireworksComponent)value;
            writer.WriteVarInt(fireworks.FlightDuration);
            WriteCount(ref writer, fireworks.Explosions.Count, MaxExplosions, "minecraft:fireworks explosions");
            foreach (FireworkExplosionValue explosion in fireworks.Explosions)
                WriteExplosion(ref writer, explosion);

        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var fireworks = (FireworksComponent)value;
            var entries = new List<(int, int)>(2);
            if (fireworks.FlightDuration != 0)
                entries.Add((ops.String("flight_duration"), ops.Byte(unchecked((sbyte)fireworks.FlightDuration))));

            if (fireworks.Explosions.Count > 0)
            {
                var layers = new int[fireworks.Explosions.Count];
                for (int i = 0; i < layers.Length; i++)
                    layers[i] = HashExplosion(ops, fireworks.Explosions[i]);

                entries.Add((ops.String("explosions"), ops.List(layers)));
            }

            return ops.Map(entries);
        }
    }

    /// <summary><c>minecraft:firework_explosion</c> (the firework STAR component): one bare explosion layer.</summary>
    /// <remarks>The payload is a shape VarInt, two color lists, then trail and twinkle booleans. This layout is unchanged on protocols 766-776.</remarks>
    private sealed class FireworkExplosionCodecImpl() : ItemComponentCodec(DataComponents.FireworkExplosion)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            new FireworkExplosionComponent(ReadExplosion(ref reader));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            WriteExplosion(ref writer, ((FireworkExplosionComponent)value).Explosion);

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            HashExplosion(ops, ((FireworkExplosionComponent)value).Explosion);
    }

    /// <summary>The explosion shape names in VarInt wire-id order. An out-of-range id maps to <c>small_ball</c>; this default is why an out-of-range id decodes to <c>small_ball</c> here too, so re-encoding it emits id 0 rather than reproducing the invalid original id.</summary>
    private static readonly string[] FireworkShapes = ["small_ball", "large_ball", "star", "creeper", "burst"];

    /// <summary>The 256-element explosion-list limit applies in both directions.</summary>
    /// <remarks>Both directions matter. On DECODE the generic reader's plausibility guard only bounds a claimed count by the bytes remaining, and an explosion is five wire bytes at its smallest while a decoded element is a reference, so a hostile count inside a large frame allocates several times the frame before a single element is read. On encode, peers reject more than 256 elements and close the connection. Refusing before writing a byte keeps that failure local.</remarks>
    private const int MaxExplosions = 256;

    /// <summary>Unbounded wire lists cap their initial allocation at 65536 so a claimed count cannot force a larger allocation before elements are read. Color lists have no element-count cap, so this allocation bound is the only limit to mirror; capping their count would reject otherwise valid frames.</summary>
    private const int MaxInitialCollectionSize = 65536;

    /// <summary>Reads a VarInt count and rejects it above the cap before anything is allocated, then the same plausibility bound the shared list reader applies. The cap is checked first on purpose, so an oversized count is reported as the cap breach it is rather than as a generic implausible length, and so no allocation decision is ever taken from a count above the protocol cap.</summary>
    private static int ReadCount(ref PacketReader reader, int max, string what)
    {
        int count = reader.ReadVarInt();
        if (count > max)
            throw new ProtocolViolationException($"{count} elements exceeded max size of: {max} ({what}).");

        if (count < 0 || count > reader.Remaining + 1)
            throw new ProtocolViolationException(
                $"{what} length {count} is implausible for {reader.Remaining} remaining bytes.");

        return count;
    }

    /// <summary>Refuses an oversized list before emitting anything.</summary>
    private static void WriteCount(ref PacketWriter writer, int count, int max, string what)
    {
        if (count > max)
            throw new ProtocolViolationException($"{count} elements exceeded max size of: {max} ({what}).");

        writer.WriteVarInt(count);
    }

    private static FireworkExplosionValue ReadExplosion(ref PacketReader reader)
    {
        int shapeId = reader.ReadVarInt();
        int[] colors = ReadColorList(ref reader);
        int[] fadeColors = ReadColorList(ref reader);
        bool hasTrail = reader.ReadBool();
        bool hasTwinkle = reader.ReadBool();
        string shape = shapeId >= 0 && shapeId < FireworkShapes.Length ? FireworkShapes[shapeId] : FireworkShapes[0];
        return new FireworkExplosionValue(shape, colors, fadeColors, hasTrail, hasTwinkle);
    }

    /// <summary>One color list: a VarInt count followed by that many four-byte big-endian integers. There is no element cap, only the 65536 initial allocation bound, so this grows rather than trusting the claimed count.</summary>
    private static int[] ReadColorList(ref PacketReader reader)
    {
        int count = ReadCount(ref reader, int.MaxValue, "minecraft:firework_explosion colors");
        var colors = new List<int>(Math.Min(count, MaxInitialCollectionSize));
        for (int i = 0; i < count; i++)
            colors.Add(reader.ReadInt());

        return [.. colors];
    }

    private static void WriteExplosion(ref PacketWriter writer, FireworkExplosionValue explosion)
    {
        writer.WriteVarInt(FireworkShapeId(explosion.Shape));
        writer.WriteList(explosion.Colors, static (ref PacketWriter w, int c) => w.WriteInt(c));
        writer.WriteList(explosion.FadeColors, static (ref PacketWriter w, int c) => w.WriteInt(c));
        writer.WriteBool(explosion.HasTrail);
        writer.WriteBool(explosion.HasTwinkle);
    }

    /// <summary>In the data form of one explosion layer, <c>shape</c> is a required string, while <c>colors</c> and <c>fade_colors</c> are lists of ints, not an int ARRAY) omitted when empty, and <c>has_trail</c>/<c>has_twinkle</c> are bools omitted when false.</summary>
    private static int HashExplosion(in HashOps ops, FireworkExplosionValue explosion)
    {
        var entries = new List<(int, int)>(5)
        {
            (ops.String("shape"), ops.String(explosion.Shape)),
        };

        if (explosion.Colors.Count > 0)
            entries.Add((ops.String("colors"), HashIntList(ops, explosion.Colors)));

        if (explosion.FadeColors.Count > 0)
            entries.Add((ops.String("fade_colors"), HashIntList(ops, explosion.FadeColors)));

        if (explosion.HasTrail)
            entries.Add((ops.String("has_trail"), ops.Boolean(true)));

        if (explosion.HasTwinkle)
            entries.Add((ops.String("has_twinkle"), ops.Boolean(true)));

        return ops.Map(entries);
    }

    private static int HashIntList(in HashOps ops, IReadOnlyList<int> values)
    {
        var hashes = new int[values.Count];
        for (int i = 0; i < hashes.Length; i++)
            hashes[i] = ops.Int(values[i]);

        return ops.List(hashes);
    }

    private static int FireworkShapeId(string shape)
    {
        for (int i = 0; i < FireworkShapes.Length; i++)
            if (string.Equals(FireworkShapes[i], shape, StringComparison.Ordinal))
                return i;

        return 0;
    }

    private sealed class TrimCodecImpl() : ItemComponentCodec(DataComponents.Trim)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int material = ItemCodecPrimitives.ReadHolderId(ref reader);
            int pattern = ItemCodecPrimitives.ReadHolderId(ref reader);
            return new TrimComponent(new Identifier("umpk", $"trim_material_{material}"), new Identifier("umpk", $"trim_pattern_{pattern}"));
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var trim = (TrimComponent)value;
            ItemCodecPrimitives.WriteHolderId(ref writer, ParseSynthetic(trim.Material, "trim_material_"));
            ItemCodecPrimitives.WriteHolderId(ref writer, ParseSynthetic(trim.Pattern, "trim_pattern_"));
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var trim = (TrimComponent)value;
            return ops.Map([(ops.String("material"), ops.String(trim.Material.ToString())), (ops.String("pattern"), ops.String(trim.Pattern.ToString()))]);
        }

        private static int ParseSynthetic(Identifier id, string prefix) =>
            string.Equals(id.Namespace, "umpk", StringComparison.Ordinal) && id.Path.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(id.Path.AsSpan(prefix.Length), out int raw)
                ? raw
                : 0;
    }

    private sealed class ToolCodecImpl() : ItemComponentCodec(DataComponents.Tool)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int ruleCount = reader.ReadVarInt();
            var rules = new List<NbtCompound>(ruleCount);
            for (int i = 0; i < ruleCount; i++)
                rules.Add(ReadRule(ref reader));

            float defaultMiningSpeed = reader.ReadFloat();
            int damagePerBlock = reader.ReadVarInt();
            _ = reader.ReadBool(); // canDestroyBlocksInCreative; not modeled, consumed to stay frame-exact.
            return new ToolComponent(defaultMiningSpeed, damagePerBlock, rules);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var tool = (ToolComponent)value;
            writer.WriteVarInt(tool.Rules.Count);
            foreach (NbtCompound rule in tool.Rules)
                WriteRule(ref writer, rule);

            writer.WriteFloat(tool.DefaultMiningSpeed);
            writer.WriteVarInt(tool.DamagePerBlock);
            writer.WriteBool(true); // canDestroyBlocksInCreative default (vanilla true); round-trips a fixed value.
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var tool = (ToolComponent)value;
            var entries = new List<(int, int)>();
            if (tool.DefaultMiningSpeed != 1.0f)
                entries.Add((ops.String("default_mining_speed"), ops.Float(tool.DefaultMiningSpeed)));

            if (tool.DamagePerBlock != 1)
                entries.Add((ops.String("damage_per_block"), ops.Int(tool.DamagePerBlock)));

            return ops.Map(entries);
        }

        // A rule is a holder-set of blocks (VarInt tag-or-count form), an optional float speed, and an optional bool correctForDrops. It is retained opaquely as an NBT compound with those members so the wire round-trips without a block registry dependency.
        private static NbtCompound ReadRule(ref PacketReader reader)
        {
            var rule = new NbtCompound();
            var blocks = ReadHolderSet(ref reader);
            rule.Put("blocks", blocks);
            if (reader.ReadBool())
                rule.PutFloat("speed", reader.ReadFloat());

            if (reader.ReadBool())
                rule.PutBool("correct_for_drops", reader.ReadBool());

            return rule;
        }

        private static void WriteRule(ref PacketWriter writer, NbtCompound rule)
        {
            WriteHolderSet(ref writer, rule.GetList("blocks"));
            if (rule.TryGet("speed", out NbtFloat? f))
            {
                writer.WriteBool(true);
                writer.WriteFloat(f.Value);
            }
            else
                writer.WriteBool(false);

            if (rule.TryGet("correct_for_drops", out NbtByte? b))
            {
                writer.WriteBool(true);
                writer.WriteBool(b.Value != 0);
            }
            else
                writer.WriteBool(false);

        }

        // Holder set: VarInt (count+1); 0 means a named tag (VarInt-prefixed string) follows; n means n direct holder ids. Preserved as an int list (or a single string) inside an NBT list.
        private static NbtList ReadHolderSet(ref PacketReader reader)
        {
            var list = new NbtList(NbtTagType.Int);
            int marker = reader.ReadVarInt();
            if (marker == 0)
            {
                var tagged = new NbtList(NbtTagType.String);
                tagged.Add(new NbtString(reader.ReadString()));
                return tagged;
            }

            int count = marker - 1;
            for (int i = 0; i < count; i++)
                list.Add(new NbtInt(reader.ReadVarInt()));

            return list;
        }

        private static void WriteHolderSet(ref PacketWriter writer, NbtList? holders)
        {
            if (holders is { ElementType: NbtTagType.String } tagged && tagged.Count > 0)
            {
                writer.WriteVarInt(0);
                writer.WriteString(((NbtString)tagged[0]).Value);
                return;
            }

            int count = holders?.Count ?? 0;
            writer.WriteVarInt(count + 1);
            if (holders is not null)
                foreach (NbtTag holder in holders)
                    writer.WriteVarInt(((NbtInt)holder).Value);

        }
    }

    private sealed class AttributeModifiersCodecImpl(bool hasDisplay) : ItemComponentCodec(DataComponents.AttributeModifiers)
    {
        private static readonly string[] Operations = ["add_value", "add_multiplied_base", "add_multiplied_total"];

        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int count = reader.ReadVarInt();
            var modifiers = new List<AttributeModifierEntry>(count);
            for (int i = 0; i < count; i++)
            {
                RegistryEntry<AttributeDefinition> attribute = ItemCodecPrimitives.ResolveAttribute(context, ItemCodecPrimitives.ReadHolderId(ref reader));
                string modifierId = reader.ReadString();
                double amount = reader.ReadDouble();
                int operationId = reader.ReadVarInt();
                int slotId = reader.ReadVarInt();
                if (hasDisplay)
                    ReadDisplay(ref reader);

                modifiers.Add(new AttributeModifierEntry(
                    attribute, modifierId, amount,
                    operationId >= 0 && operationId < Operations.Length ? Operations[operationId] : Operations[0],
                    SlotName(slotId)));
            }

            return new AttributeModifiersComponent(modifiers);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            IReadOnlyList<AttributeModifierEntry> modifiers = ((AttributeModifiersComponent)value).Modifiers;
            writer.WriteVarInt(modifiers.Count);
            foreach (AttributeModifierEntry modifier in modifiers)
            {
                ItemCodecPrimitives.WriteHolderId(ref writer, modifier.Attribute.NetworkId);
                writer.WriteString(modifier.ModifierId);
                writer.WriteDouble(modifier.Amount);
                writer.WriteVarInt(OperationId(modifier.Operation));
                writer.WriteVarInt(SlotId(modifier.Slot));
                if (hasDisplay)
                {
                    writer.WriteVarInt(0); // display: DEFAULT
                }
            }
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<AttributeModifierEntry> modifiers = ((AttributeModifiersComponent)value).Modifiers;
            var hashes = new int[modifiers.Count];
            for (int i = 0; i < hashes.Length; i++)
            {
                AttributeModifierEntry modifier = modifiers[i];
                hashes[i] = ops.Map(
                [
                    (ops.String("type"), ops.String(modifier.Attribute.Id.ToString())),
                    (ops.String("id"), ops.String(modifier.ModifierId)),
                    (ops.String("amount"), ops.Double(modifier.Amount)),
                    (ops.String("operation"), ops.String(modifier.Operation)),
                    (ops.String("slot"), ops.String(modifier.Slot)),
                ]);
            }

            return ops.Map([(ops.String("modifiers"), ops.List(hashes))]);
        }

        private static void ReadDisplay(ref PacketReader reader)
        {
            int type = reader.ReadVarInt();
            if (type == 2)
            {
                // OVERRIDE carries a component. The display field only exists from 1.21.6 (protocol 771), which is already inside the modern interaction dialect, so the era is fixed here.
                _ = ItemCodecPrimitives.ReadNetworkComponent(ref reader, ComponentWireEra.Modern);
            }
        }

        private static string SlotName(int id) => id switch
        {
            0 => "any",
            1 => "mainhand",
            2 => "offhand",
            3 => "hand",
            4 => "feet",
            5 => "legs",
            6 => "chest",
            7 => "head",
            8 => "armor",
            9 => "body",
            10 => "saddle",
            _ => "any",
        };

        private static int SlotId(string name) => name switch
        {
            "any" => 0,
            "mainhand" => 1,
            "offhand" => 2,
            "hand" => 3,
            "feet" => 4,
            "legs" => 5,
            "chest" => 6,
            "head" => 7,
            "armor" => 8,
            "body" => 9,
            "saddle" => 10,
            _ => 0,
        };

        private static int OperationId(string operation)
        {
            for (int i = 0; i < Operations.Length; i++)
                if (string.Equals(Operations[i], operation, StringComparison.Ordinal))
                    return i;

            return 0;
        }
    }

    // Concrete codec kinds for those components

    /// <summary>One boolean. Its data form and hash use the same value.</summary>
    private sealed class BoolCodec(DataComponentType type, Func<bool, object> wrap, Func<object, bool> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) => wrap(reader.ReadBool());

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteBool(unwrap(value));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => ops.Boolean(unwrap(value));
    }

    /// <summary>One four-byte big-endian float.</summary>
    private sealed class FloatCodec(DataComponentType type, Func<float, object> wrap, Func<object, float> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) => wrap(reader.ReadFloat());

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteFloat(unwrap(value));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => ops.Float(unwrap(value));
    }

    /// <summary>One UTF-8 string holding a namespaced id. The data form is the same string, so the hash is the string.</summary>
    private sealed class IdentifierCodec(DataComponentType type, Func<Identifier, object> wrap, Func<object, Identifier> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            wrap(Identifier.Parse(reader.ReadString()));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteString(unwrap(value).ToString());

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            ops.String(unwrap(value).ToString());
    }

    /// <summary>Zero payload bytes; the data form is the empty map.</summary>
    private sealed class UnitMarkerCodec(DataComponentType type) : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) => UnitMarkerComponent.Instance;

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => ops.EmptyMap;
    }

    /// <summary>One network NBT tag for a component with no specialized network form. Unlike <see cref="NbtCarrierCodec"/> this keeps the tag as-is rather than coercing it to a compound, because these payloads are not all compounds (<c>recipes</c> is a list).</summary>
    private sealed class NbtPayloadCodec(DataComponentType type) : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            new NbtPayloadComponent(reader.ReadNbt(ItemCodecPrimitives.NetworkNbt));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteNbt(((NbtPayloadComponent)value).Tag, ItemCodecPrimitives.NetworkNbt);

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            ItemCodecPrimitives.HashNbt(ops, ((NbtPayloadComponent)value).Tag);
    }

    /// <summary>A VarInt dye-color id (0 white .. 15 black); the data form is the canonical name string.</summary>
    private sealed class DyeColorCodec(DataComponentType type, Func<string, object> wrap, Func<object, string> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            wrap(DyeColorNames.Name(reader.ReadVarInt()));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteVarInt(DyeColorNames.Id(unwrap(value)));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => ops.String(unwrap(value));
    }

    /// <summary>The dye-color VarInt id order, 0 through 15, is unchanged across 766/767/770/776. An out-of-range id maps to white, so this mirrors that behavior rather than faulting.</summary>
    internal static class DyeColorNames
    {
        private static readonly string[] Names =
        [
            "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
            "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black",
        ];

        public static string Name(int id) => id >= 0 && id < Names.Length ? Names[id] : Names[0];

        public static int Id(string name)
        {
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i], name, StringComparison.Ordinal))
                    return i;

            return 0;
        }
    }

    /// <summary>A VarInt enum id: 0 = LOCK, 1 = SCALE.</summary>
    private sealed class MapPostProcessingCodecImpl() : ItemComponentCodec(DataComponents.MapPostProcessing)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int id = reader.ReadVarInt();

            // An out-of-range id maps to the first constant rather than failing.
            return new MapPostProcessingComponent(id == 1 ? MapPostProcessingKind.Scale : MapPostProcessingKind.Lock);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteVarInt((int)((MapPostProcessingComponent)value).Kind);

        // This value has no persistent form, so there is no canonical data form to mirror;
        // the numeric id is the only stable structure. It never reaches a click echo, because the server consumes the component when it materializes the map.
        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            ops.Int((int)((MapPostProcessingComponent)value).Kind);
    }

    /// <summary>BOOL then a VarInt-counted list of component wire ids.</summary>
    private sealed class TooltipDisplayCodecImpl() : ItemComponentCodec(DataComponents.TooltipDisplay)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            bool hideTooltip = reader.ReadBool();
            int[] hidden = reader.ReadList(static (ref PacketReader r) => r.ReadVarInt());
            return new TooltipDisplayComponent(hideTooltip, hidden);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var display = (TooltipDisplayComponent)value;
            writer.WriteBool(display.HideTooltip);
            writer.WriteList(display.HiddenComponents, static (ref PacketWriter w, int id) => w.WriteVarInt(id));
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            // The data form is {hide_tooltip: bool, hidden_components: [component id strings]}, both optional fields that vanilla omits at their defaults.
            var display = (TooltipDisplayComponent)value;
            var map = new List<(int, int)>(2);
            if (display.HideTooltip)
                map.Add((ops.String("hide_tooltip"), ops.Boolean(true)));

            if (display.HiddenComponents.Count > 0)
            {
                var hashes = new int[display.HiddenComponents.Count];
                for (int i = 0; i < hashes.Length; i++)
                    hashes[i] = ops.Int(display.HiddenComponents[i]);

                map.Add((ops.String("hidden_components"), ops.List(hashes)));
            }

            return ops.Map(map);
        }
    }

    /// <summary>Four lists: float, bool, string, and fixed-width int (1.21.4+ form).</summary>
    private sealed class CustomModelDataCodecImpl() : ItemComponentCodec(DataComponents.CustomModelData)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            float[] floats = reader.ReadList(static (ref PacketReader r) => r.ReadFloat());
            bool[] flags = reader.ReadList(static (ref PacketReader r) => r.ReadBool());
            string[] strings = reader.ReadList(static (ref PacketReader r) => r.ReadString());
            int[] colors = reader.ReadList(static (ref PacketReader r) => r.ReadInt());
            return new CustomModelDataComponent(floats, flags, strings, colors);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var data = (CustomModelDataComponent)value;
            writer.WriteList(data.Floats, static (ref PacketWriter w, float f) => w.WriteFloat(f));
            writer.WriteList(data.Flags, static (ref PacketWriter w, bool b) => w.WriteBool(b));
            writer.WriteList(data.Strings, static (ref PacketWriter w, string t) => w.WriteString(t));
            writer.WriteList(data.Colors, static (ref PacketWriter w, int c) => w.WriteInt(c));
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            // Every list has an empty default, so an empty list contributes no field.
            var data = (CustomModelDataComponent)value;
            var map = new List<(int, int)>(4);

            if (data.Floats.Count > 0)
            {
                var hashes = new int[data.Floats.Count];
                for (int i = 0; i < hashes.Length; i++)
                    hashes[i] = ops.Float(data.Floats[i]);

                map.Add((ops.String("floats"), ops.List(hashes)));
            }

            if (data.Flags.Count > 0)
            {
                var hashes = new int[data.Flags.Count];
                for (int i = 0; i < hashes.Length; i++)
                    hashes[i] = ops.Boolean(data.Flags[i]);

                map.Add((ops.String("flags"), ops.List(hashes)));
            }

            if (data.Strings.Count > 0)
            {
                var hashes = new int[data.Strings.Count];
                for (int i = 0; i < hashes.Length; i++)
                    hashes[i] = ops.String(data.Strings[i]);

                map.Add((ops.String("strings"), ops.List(hashes)));
            }

            if (data.Colors.Count > 0)
            {
                var hashes = new int[data.Colors.Count];
                for (int i = 0; i < hashes.Length; i++)
                    hashes[i] = ops.Int(data.Colors[i]);

                map.Add((ops.String("colors"), ops.List(hashes)));
            }

            return ops.Map(map);
        }
    }

    /// <summary>The pre-1.21.4 single-VarInt form, carried in the shared record's float list.</summary>
    private sealed class LegacyCustomModelDataCodecImpl() : ItemComponentCodec(DataComponents.CustomModelData)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            new CustomModelDataComponent([reader.ReadVarInt()], [], [], []);

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            IReadOnlyList<float> floats = ((CustomModelDataComponent)value).Floats;
            writer.WriteVarInt(floats.Count > 0 ? (int)floats[0] : 0);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<float> floats = ((CustomModelDataComponent)value).Floats;
            return ops.Int(floats.Count > 0 ? (int)floats[0] : 0);
        }
    }

    private sealed class NbtCarrierCodec(DataComponentType type, Func<NbtTag, object> wrap, Func<object, NbtCompound> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            wrap(reader.ReadNbt(ItemCodecPrimitives.NetworkNbt));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteNbt(unwrap(value), ItemCodecPrimitives.NetworkNbt);

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            ItemCodecPrimitives.HashNbt(ops, unwrap(value));
    }

    /// <summary>From protocol 773, the payload is a VarInt registry type id then a network-NBT compound. The compound has no <c>id</c> member because the type is the separate leading field), so the compound this reads never contains it and the re-encode is byte-exact without any tag rewriting here.</summary>
    private sealed class TypedNbtCarrierCodec(
        DataComponentType type,
        Func<int, NbtCompound, object> wrap,
        Func<object, (int? TypeId, NbtCompound Data)> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int typeId = reader.ReadVarInt();
            return wrap(typeId, AsCompound(reader.ReadNbt(ItemCodecPrimitives.NetworkNbt)));
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            (int? typeId, NbtCompound data) = unwrap(value);
            if (typeId is not { } id)
            {
                // The leading type id has no default on this era. Emitting a guess would desynchronize the rest of the component list, so this faults with the identity instead. A decoded component always has it.
                throw new ProtocolViolationException(
                    $"Component '{Type.Id}' needs a type id on this protocol (1.21.9 moved it onto the wire).");
            }

            writer.WriteVarInt(id);
            writer.WriteNbt(data, ItemCodecPrimitives.NetworkNbt);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            // The data form folds the type back into the compound under "id" as a resource string rather than the wire's numeric id. The registry name is not available to this layer, so the type is omitted from the hash: a hash disagreement costs a server-driven slot resync, never the session.
            (_, NbtCompound data) = unwrap(value);
            return ItemCodecPrimitives.HashNbt(ops, data);
        }
    }
}
