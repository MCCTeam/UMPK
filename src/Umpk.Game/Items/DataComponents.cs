using Umpk.Game.Items.Components;

namespace Umpk.Game.Items;

/// <summary>The catalog of well-known vanilla data-component keys. Every entry is a single immutable <see cref="DataComponentType{T}"/> instance, so key identity is reference identity and the map can compare keys cheaply. This is a set of <c>static readonly</c> immutable handles, not mutable state, so it introduces no mutable statics. The Java codec layer maps a component <see cref="DataComponentType.Id"/> to the matching key here when decoding a patch, and any component id not present here is decoded into the raw <c>minecraft:custom_data</c>-style opaque carrier by the codec layer rather than modeled.</summary>
public static class DataComponents
{
    private static DataComponentType<T> Make<T>(string path)
        where T : class => new(Identifier.Minecraft(path));

    /// <summary><c>minecraft:damage</c>.</summary>
    public static DataComponentType<DamageComponent> Damage { get; } = Make<DamageComponent>("damage");

    /// <summary><c>minecraft:max_damage</c>.</summary>
    public static DataComponentType<MaxDamageComponent> MaxDamage { get; } = Make<MaxDamageComponent>("max_damage");

    /// <summary><c>minecraft:max_stack_size</c>.</summary>
    public static DataComponentType<MaxStackSizeComponent> MaxStackSize { get; } = Make<MaxStackSizeComponent>("max_stack_size");

    /// <summary><c>minecraft:repair_cost</c>.</summary>
    public static DataComponentType<RepairCostComponent> RepairCost { get; } = Make<RepairCostComponent>("repair_cost");

    /// <summary><c>minecraft:enchantments</c>.</summary>
    public static DataComponentType<EnchantmentsComponent> Enchantments { get; } = Make<EnchantmentsComponent>("enchantments");

    /// <summary><c>minecraft:stored_enchantments</c>.</summary>
    public static DataComponentType<StoredEnchantmentsComponent> StoredEnchantments { get; } = Make<StoredEnchantmentsComponent>("stored_enchantments");

    /// <summary><c>minecraft:custom_name</c>.</summary>
    public static DataComponentType<CustomNameComponent> CustomName { get; } = Make<CustomNameComponent>("custom_name");

    /// <summary><c>minecraft:item_name</c>.</summary>
    public static DataComponentType<ItemNameComponent> ItemName { get; } = Make<ItemNameComponent>("item_name");

    /// <summary><c>minecraft:lore</c>.</summary>
    public static DataComponentType<LoreComponent> Lore { get; } = Make<LoreComponent>("lore");

    /// <summary><c>minecraft:unbreakable</c>.</summary>
    public static DataComponentType<UnbreakableComponent> Unbreakable { get; } = Make<UnbreakableComponent>("unbreakable");

    /// <summary><c>minecraft:rarity</c>.</summary>
    public static DataComponentType<RarityComponent> Rarity { get; } = Make<RarityComponent>("rarity");

    /// <summary><c>minecraft:custom_model_data</c>.</summary>
    public static DataComponentType<CustomModelDataComponent> CustomModelData { get; } = Make<CustomModelDataComponent>("custom_model_data");

    /// <summary><c>minecraft:attribute_modifiers</c>.</summary>
    public static DataComponentType<AttributeModifiersComponent> AttributeModifiers { get; } = Make<AttributeModifiersComponent>("attribute_modifiers");

    /// <summary><c>minecraft:food</c>.</summary>
    public static DataComponentType<FoodComponent> Food { get; } = Make<FoodComponent>("food");

    /// <summary><c>minecraft:tool</c>.</summary>
    public static DataComponentType<ToolComponent> Tool { get; } = Make<ToolComponent>("tool");

    /// <summary><c>minecraft:potion_contents</c>.</summary>
    public static DataComponentType<PotionContentsComponent> PotionContents { get; } = Make<PotionContentsComponent>("potion_contents");

    /// <summary><c>minecraft:suspicious_stew_effects</c>.</summary>
    public static DataComponentType<SuspiciousStewEffectsComponent> SuspiciousStewEffects { get; } = Make<SuspiciousStewEffectsComponent>("suspicious_stew_effects");

    /// <summary><c>minecraft:trim</c>.</summary>
    public static DataComponentType<TrimComponent> Trim { get; } = Make<TrimComponent>("trim");

    /// <summary><c>minecraft:dyed_color</c>.</summary>
    public static DataComponentType<DyedColorComponent> DyedColor { get; } = Make<DyedColorComponent>("dyed_color");

    /// <summary><c>minecraft:base_color</c>.</summary>
    public static DataComponentType<BaseColorComponent> BaseColor { get; } = Make<BaseColorComponent>("base_color");

    /// <summary><c>minecraft:map_color</c>.</summary>
    public static DataComponentType<MapColorComponent> MapColor { get; } = Make<MapColorComponent>("map_color");

    /// <summary><c>minecraft:banner_patterns</c>.</summary>
    public static DataComponentType<BannerPatternsComponent> BannerPatterns { get; } = Make<BannerPatternsComponent>("banner_patterns");

    /// <summary><c>minecraft:container</c>.</summary>
    public static DataComponentType<ContainerComponent> Container { get; } = Make<ContainerComponent>("container");

    /// <summary><c>minecraft:bundle_contents</c>.</summary>
    public static DataComponentType<BundleContentsComponent> BundleContents { get; } = Make<BundleContentsComponent>("bundle_contents");

    /// <summary><c>minecraft:charged_projectiles</c>.</summary>
    public static DataComponentType<ChargedProjectilesComponent> ChargedProjectiles { get; } = Make<ChargedProjectilesComponent>("charged_projectiles");

    /// <summary><c>minecraft:use_remainder</c> (1.21.2+).</summary>
    public static DataComponentType<UseRemainderComponent> UseRemainder { get; } = Make<UseRemainderComponent>("use_remainder");

    /// <summary><c>minecraft:sulfur_cube_content</c> (26.2+).</summary>
    public static DataComponentType<SulfurCubeContentComponent> SulfurCubeContent { get; } = Make<SulfurCubeContentComponent>("sulfur_cube_content");

    /// <summary><c>minecraft:pot_decorations</c>.</summary>
    public static DataComponentType<PotDecorationsComponent> PotDecorations { get; } = Make<PotDecorationsComponent>("pot_decorations");

    /// <summary><c>minecraft:profile</c>.</summary>
    public static DataComponentType<ProfileComponent> Profile { get; } = Make<ProfileComponent>("profile");

    /// <summary><c>minecraft:writable_book_content</c>.</summary>
    public static DataComponentType<WritableBookContentComponent> WritableBookContent { get; } = Make<WritableBookContentComponent>("writable_book_content");

    /// <summary><c>minecraft:written_book_content</c>.</summary>
    public static DataComponentType<WrittenBookContentComponent> WrittenBookContent { get; } = Make<WrittenBookContentComponent>("written_book_content");

    /// <summary><c>minecraft:block_state</c>.</summary>
    public static DataComponentType<BlockStateComponent> BlockState { get; } = Make<BlockStateComponent>("block_state");

    /// <summary><c>minecraft:fireworks</c>.</summary>
    public static DataComponentType<FireworksComponent> Fireworks { get; } = Make<FireworksComponent>("fireworks");

    /// <summary><c>minecraft:firework_explosion</c>.</summary>
    public static DataComponentType<FireworkExplosionComponent> FireworkExplosion { get; } = Make<FireworkExplosionComponent>("firework_explosion");

    /// <summary><c>minecraft:custom_data</c>.</summary>
    public static DataComponentType<CustomDataComponent> CustomData { get; } = Make<CustomDataComponent>("custom_data");

    /// <summary>The legacy residual-NBT escape hatch (<c>umpk:legacy_nbt</c>; model-only, not a wire component).</summary>
    public static DataComponentType<LegacyNbtComponent> LegacyNbt { get; } = new(new Identifier("umpk", "legacy_nbt"));

    /// <summary><c>minecraft:block_entity_data</c>.</summary>
    public static DataComponentType<BlockEntityDataComponent> BlockEntityData { get; } = Make<BlockEntityDataComponent>("block_entity_data");

    /// <summary><c>minecraft:entity_data</c>.</summary>
    public static DataComponentType<EntityDataComponent> EntityData { get; } = Make<EntityDataComponent>("entity_data");

    /// <summary><c>minecraft:bucket_entity_data</c>.</summary>
    public static DataComponentType<BucketEntityDataComponent> BucketEntityData { get; } = Make<BucketEntityDataComponent>("bucket_entity_data");

    /// <summary><c>minecraft:map_id</c>.</summary>
    public static DataComponentType<MapIdComponent> MapId { get; } = Make<MapIdComponent>("map_id");

    /// <summary><c>minecraft:map_post_processing</c>.</summary>
    public static DataComponentType<MapPostProcessingComponent> MapPostProcessing { get; } = Make<MapPostProcessingComponent>("map_post_processing");

    /// <summary><c>minecraft:map_decorations</c>.</summary>
    public static DataComponentType<NbtPayloadComponent> MapDecorations { get; } = Make<NbtPayloadComponent>("map_decorations");

    /// <summary><c>minecraft:enchantment_glint_override</c>.</summary>
    public static DataComponentType<EnchantmentGlintOverrideComponent> EnchantmentGlintOverride { get; } = Make<EnchantmentGlintOverrideComponent>("enchantment_glint_override");

    /// <summary><c>minecraft:ominous_bottle_amplifier</c>.</summary>
    public static DataComponentType<OminousBottleAmplifierComponent> OminousBottleAmplifier { get; } = Make<OminousBottleAmplifierComponent>("ominous_bottle_amplifier");

    /// <summary><c>minecraft:potion_duration_scale</c>.</summary>
    public static DataComponentType<PotionDurationScaleComponent> PotionDurationScale { get; } = Make<PotionDurationScaleComponent>("potion_duration_scale");

    /// <summary><c>minecraft:item_model</c>.</summary>
    public static DataComponentType<ItemModelComponent> ItemModel { get; } = Make<ItemModelComponent>("item_model");

    /// <summary><c>minecraft:tooltip_style</c>.</summary>
    public static DataComponentType<TooltipStyleComponent> TooltipStyle { get; } = Make<TooltipStyleComponent>("tooltip_style");

    /// <summary><c>minecraft:note_block_sound</c>.</summary>
    public static DataComponentType<NoteBlockSoundComponent> NoteBlockSound { get; } = Make<NoteBlockSoundComponent>("note_block_sound");

    /// <summary><c>minecraft:tooltip_display</c> (1.21.5+).</summary>
    public static DataComponentType<TooltipDisplayComponent> TooltipDisplay { get; } = Make<TooltipDisplayComponent>("tooltip_display");

    /// <summary><c>minecraft:creative_slot_lock</c>; a zero-byte marker.</summary>
    public static DataComponentType<UnitMarkerComponent> CreativeSlotLock { get; } = Make<UnitMarkerComponent>("creative_slot_lock");

    /// <summary><c>minecraft:glider</c> (1.21.5+); a zero-byte marker.</summary>
    public static DataComponentType<UnitMarkerComponent> Glider { get; } = Make<UnitMarkerComponent>("glider");

    /// <summary><c>minecraft:hide_tooltip</c> (1.20.5-1.21.1); a zero-byte marker.</summary>
    public static DataComponentType<UnitMarkerComponent> HideTooltip { get; } = Make<UnitMarkerComponent>("hide_tooltip");

    /// <summary><c>minecraft:hide_additional_tooltip</c> (1.20.5-1.21.1); a zero-byte marker.</summary>
    public static DataComponentType<UnitMarkerComponent> HideAdditionalTooltip { get; } = Make<UnitMarkerComponent>("hide_additional_tooltip");

    /// <summary><c>minecraft:fire_resistant</c> (1.20.5-1.21.1); a zero-byte marker.</summary>
    public static DataComponentType<UnitMarkerComponent> FireResistant { get; } = Make<UnitMarkerComponent>("fire_resistant");

    /// <summary><c>minecraft:intangible_projectile</c>; an NBT-carried marker.</summary>
    public static DataComponentType<NbtPayloadComponent> IntangibleProjectile { get; } = Make<NbtPayloadComponent>("intangible_projectile");

    /// <summary><c>minecraft:debug_stick_state</c>.</summary>
    public static DataComponentType<NbtPayloadComponent> DebugStickState { get; } = Make<NbtPayloadComponent>("debug_stick_state");

    /// <summary><c>minecraft:recipes</c>.</summary>
    public static DataComponentType<NbtPayloadComponent> Recipes { get; } = Make<NbtPayloadComponent>("recipes");

    /// <summary><c>minecraft:lock</c>.</summary>
    public static DataComponentType<NbtPayloadComponent> Lock { get; } = Make<NbtPayloadComponent>("lock");

    /// <summary><c>minecraft:container_loot</c>.</summary>
    public static DataComponentType<NbtPayloadComponent> ContainerLoot { get; } = Make<NbtPayloadComponent>("container_loot");

    /// <summary><c>minecraft:wolf/collar</c>.</summary>
    public static DataComponentType<DyeColorValueComponent> WolfCollar { get; } = Make<DyeColorValueComponent>("wolf/collar");

    /// <summary><c>minecraft:cat/collar</c>.</summary>
    public static DataComponentType<DyeColorValueComponent> CatCollar { get; } = Make<DyeColorValueComponent>("cat/collar");

    /// <summary><c>minecraft:sheep/color</c>.</summary>
    public static DataComponentType<DyeColorValueComponent> SheepColor { get; } = Make<DyeColorValueComponent>("sheep/color");

    /// <summary><c>minecraft:shulker/color</c>.</summary>
    public static DataComponentType<DyeColorValueComponent> ShulkerColor { get; } = Make<DyeColorValueComponent>("shulker/color");

    /// <summary><c>minecraft:tropical_fish/base_color</c>.</summary>
    public static DataComponentType<DyeColorValueComponent> TropicalFishBaseColor { get; } = Make<DyeColorValueComponent>("tropical_fish/base_color");

    /// <summary><c>minecraft:tropical_fish/pattern_color</c>.</summary>
    public static DataComponentType<DyeColorValueComponent> TropicalFishPatternColor { get; } = Make<DyeColorValueComponent>("tropical_fish/pattern_color");

    /// <summary><c>minecraft:dye</c> (26.2).</summary>
    public static DataComponentType<DyeColorValueComponent> Dye { get; } = Make<DyeColorValueComponent>("dye");

    /// <summary><c>minecraft:minimum_attack_charge</c> (26.2).</summary>
    public static DataComponentType<MinimumAttackChargeComponent> MinimumAttackCharge { get; } = Make<MinimumAttackChargeComponent>("minimum_attack_charge");

    /// <summary><c>minecraft:additional_trade_cost</c> (26.2).</summary>
    public static DataComponentType<AdditionalTradeCostComponent> AdditionalTradeCost { get; } = Make<AdditionalTradeCostComponent>("additional_trade_cost");

    // The keys for the fifteen components that had no wire codec on any protocol. Two of them (banner_patterns, suspicious_stew_effects) already had a key and a record and only lacked a codec.

    /// <summary><c>minecraft:bees</c>.</summary>
    public static DataComponentType<BeesComponent> Bees { get; } = Make<BeesComponent>("bees");

    /// <summary><c>minecraft:can_break</c>.</summary>
    public static DataComponentType<AdventureModePredicateComponent> CanBreak { get; } = Make<AdventureModePredicateComponent>("can_break");

    /// <summary><c>minecraft:can_place_on</c>.</summary>
    public static DataComponentType<AdventureModePredicateComponent> CanPlaceOn { get; } = Make<AdventureModePredicateComponent>("can_place_on");

    /// <summary><c>minecraft:consumable</c> (1.21.2+).</summary>
    public static DataComponentType<ConsumableComponent> Consumable { get; } = Make<ConsumableComponent>("consumable");

    /// <summary><c>minecraft:damage_resistant</c> (1.21.2+).</summary>
    public static DataComponentType<DamageResistantComponent> DamageResistant { get; } = Make<DamageResistantComponent>("damage_resistant");

    /// <summary><c>minecraft:death_protection</c> (1.21.2+).</summary>
    public static DataComponentType<DeathProtectionComponent> DeathProtection { get; } = Make<DeathProtectionComponent>("death_protection");

    /// <summary><c>minecraft:enchantable</c> (1.21.2+).</summary>
    public static DataComponentType<EnchantableComponent> Enchantable { get; } = Make<EnchantableComponent>("enchantable");

    /// <summary><c>minecraft:equippable</c> (1.21.2+).</summary>
    public static DataComponentType<EquippableComponent> Equippable { get; } = Make<EquippableComponent>("equippable");

    /// <summary><c>minecraft:instrument</c>.</summary>
    public static DataComponentType<InstrumentComponent> Instrument { get; } = Make<InstrumentComponent>("instrument");

    /// <summary><c>minecraft:jukebox_playable</c> (1.21+).</summary>
    public static DataComponentType<JukeboxPlayableComponent> JukeboxPlayable { get; } = Make<JukeboxPlayableComponent>("jukebox_playable");

    /// <summary><c>minecraft:lodestone_tracker</c>.</summary>
    public static DataComponentType<LodestoneTrackerComponent> LodestoneTracker { get; } = Make<LodestoneTrackerComponent>("lodestone_tracker");

    /// <summary><c>minecraft:repairable</c> (1.21.2+).</summary>
    public static DataComponentType<RepairableComponent> Repairable { get; } = Make<RepairableComponent>("repairable");

    /// <summary><c>minecraft:use_cooldown</c> (1.21.2+).</summary>
    public static DataComponentType<UseCooldownComponent> UseCooldown { get; } = Make<UseCooldownComponent>("use_cooldown");

    /// <summary>Creates a key for a component the era's wire knows but UMPK does not model. Each caller owns the returned instance and must cache it for the lifetime of its era table, because <see cref="DataComponentMap"/> keys compare by reference identity. Kept as a factory rather than a shared cache so it introduces no mutable static state.</summary>
    /// <param name="id">The component's namespaced id on that era.</param>
    /// <returns>A fresh key bound to <paramref name="id"/>.</returns>
    public static DataComponentType<UnmodeledComponent> Unmodeled(Identifier id) => new(id);

    /// <summary>All well-known keys, for the codec layer to build its id-to-key lookup.</summary>
    public static IReadOnlyList<DataComponentType> All { get; } =
    [
        Damage, MaxDamage, MaxStackSize, RepairCost,
        Enchantments, StoredEnchantments,
        CustomName, ItemName, Lore, Unbreakable, Rarity, CustomModelData,
        AttributeModifiers, Food, Tool,
        PotionContents, SuspiciousStewEffects, Trim, DyedColor, BaseColor, MapColor,
        BannerPatterns, Container, BundleContents, ChargedProjectiles, PotDecorations, Profile,
        UseRemainder, SulfurCubeContent,
        WritableBookContent, WrittenBookContent, BlockState, Fireworks, FireworkExplosion,
        CustomData, BlockEntityData, EntityData, BucketEntityData,
        MapId, MapPostProcessing, MapDecorations, EnchantmentGlintOverride, OminousBottleAmplifier,
        PotionDurationScale, ItemModel, TooltipStyle, NoteBlockSound, TooltipDisplay,
        CreativeSlotLock, Glider, HideTooltip, HideAdditionalTooltip, FireResistant,
        IntangibleProjectile, DebugStickState, Recipes, Lock, ContainerLoot,
        WolfCollar, CatCollar, SheepColor, ShulkerColor, TropicalFishBaseColor,
        TropicalFishPatternColor, Dye, MinimumAttackCharge, AdditionalTradeCost,
        Bees, CanBreak, CanPlaceOn, Consumable, DamageResistant, DeathProtection, Enchantable,
        Equippable, Instrument, JukeboxPlayable, LodestoneTracker, Repairable, UseCooldown,
        LegacyNbt,
    ];
}
