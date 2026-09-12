using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Game.Items.Components;

// The vanilla data-component payload records (the full 1.20.5..26.2 union of the well-known components). One record per component; only the wire codec is versioned in the Java data/codec layer. Records give value equality and with-expressions, which the component map relies on
// for prototype/patch comparison. Components whose payloads reference another module's model (entity
// data, block-entity data, custom_data) carry a NbtCompound opaquely. The keys that bind these records to their Identifier live on DataComponents.

/// <summary>Accumulated durability damage (<c>minecraft:damage</c>).</summary>
/// <param name="Value">The damage value; 0 is undamaged.</param>
public sealed record DamageComponent(int Value);

/// <summary>Maximum durability (<c>minecraft:max_damage</c>).</summary>
/// <param name="Value">The maximum damage before the item breaks.</param>
public sealed record MaxDamageComponent(int Value);

/// <summary>Maximum stack size override (<c>minecraft:max_stack_size</c>).</summary>
/// <param name="Value">The maximum stack size (1..99).</param>
public sealed record MaxStackSizeComponent(int Value);

/// <summary>Repair cost in anvil levels (<c>minecraft:repair_cost</c>).</summary>
/// <param name="Value">The prior-work penalty.</param>
public sealed record RepairCostComponent(int Value);

/// <summary>The applied enchantments (<c>minecraft:enchantments</c>).</summary>
/// <param name="Enchantments">The enchantment instances.</param>
/// <param name="ShowInTooltip">Whether the tooltip shows them (pre-1.21.5 flag; kept for round-trip).</param>
public sealed record EnchantmentsComponent(IReadOnlyList<EnchantmentInstance> Enchantments, bool ShowInTooltip = true);

/// <summary>Enchantments stored on an enchanted book (<c>minecraft:stored_enchantments</c>).</summary>
/// <param name="Enchantments">The stored enchantment instances.</param>
/// <param name="ShowInTooltip">Whether the tooltip shows them (pre-1.21.5 flag; kept for round-trip).</param>
public sealed record StoredEnchantmentsComponent(IReadOnlyList<EnchantmentInstance> Enchantments, bool ShowInTooltip = true);

/// <summary>A custom display name (<c>minecraft:custom_name</c>).</summary>
/// <param name="Name">The name component.</param>
public sealed record CustomNameComponent(Component Name);

/// <summary>A forced item name override (<c>minecraft:item_name</c>).</summary>
/// <param name="Name">The name component.</param>
public sealed record ItemNameComponent(Component Name);

/// <summary>Tooltip lore lines (<c>minecraft:lore</c>).</summary>
/// <param name="Lines">The lore components, top to bottom.</param>
public sealed record LoreComponent(IReadOnlyList<Component> Lines);

/// <summary>The unbreakable marker (<c>minecraft:unbreakable</c>).</summary>
/// <param name="ShowInTooltip">Whether the tooltip shows the marker (pre-1.21.5 flag; kept for round-trip).</param>
public sealed record UnbreakableComponent(bool ShowInTooltip = true);

/// <summary>Item rarity (<c>minecraft:rarity</c>).</summary>
/// <param name="Rarity">The rarity name (common, uncommon, rare, epic).</param>
public sealed record RarityComponent(string Rarity);

/// <summary>Custom-model-data selector (<c>minecraft:custom_model_data</c>).</summary>
/// <param name="Floats">The float selectors (1.21.4+ multi-value form; a single legacy int lands here as one float).</param>
/// <param name="Flags">The boolean selectors (1.21.4+).</param>
/// <param name="Strings">The string selectors (1.21.4+).</param>
/// <param name="Colors">The color selectors as packed RGB ints (1.21.4+).</param>
public sealed record CustomModelDataComponent(
    IReadOnlyList<float> Floats,
    IReadOnlyList<bool> Flags,
    IReadOnlyList<string> Strings,
    IReadOnlyList<int> Colors);

/// <summary>Attribute modifiers granted by the item (<c>minecraft:attribute_modifiers</c>).</summary>
/// <param name="Modifiers">The modifier entries.</param>
/// <param name="ShowInTooltip">Whether the tooltip shows them (pre-1.21.5 flag; kept for round-trip).</param>
public sealed record AttributeModifiersComponent(IReadOnlyList<AttributeModifierEntry> Modifiers, bool ShowInTooltip = true);

/// <summary>The food properties (<c>minecraft:food</c>).</summary>
/// <param name="Nutrition">Hunger points restored.</param>
/// <param name="Saturation">Saturation modifier.</param>
/// <param name="CanAlwaysEat">Whether the item can be eaten with a full hunger bar.</param>
public sealed record FoodComponent(int Nutrition, float Saturation, bool CanAlwaysEat = false);

/// <summary>The tool behavior (<c>minecraft:tool</c>). Rule payloads are opaque so this record stays stable across versions.</summary>
/// <param name="DefaultMiningSpeed">The mining speed used when no rule matches.</param>
/// <param name="DamagePerBlock">Durability lost per block mined.</param>
/// <param name="Rules">The raw mining-rule payloads, retained opaquely for round-trip.</param>
public sealed record ToolComponent(float DefaultMiningSpeed, int DamagePerBlock, IReadOnlyList<NbtCompound> Rules);

/// <summary>Potion contents (<c>minecraft:potion_contents</c>).</summary>
/// <param name="PotionId">The base potion id, when present.</param>
/// <param name="CustomColor">The overridden potion color as a packed RGB int, when present.</param>
/// <param name="CustomEffects">Additional custom effects.</param>
/// <param name="CustomName">The custom potion name suffix, when present.</param>
public sealed record PotionContentsComponent(
    Identifier? PotionId,
    int? CustomColor,
    IReadOnlyList<MobEffectDetail> CustomEffects,
    string? CustomName = null);

/// <summary>Suspicious-stew effects (<c>minecraft:suspicious_stew_effects</c>).</summary>
/// <param name="Effects">The effect entries.</param>
public sealed record SuspiciousStewEffectsComponent(IReadOnlyList<MobEffectDetail> Effects);

/// <summary>Armor-trim decoration (<c>minecraft:trim</c>).</summary>
/// <param name="Material">The trim material id.</param>
/// <param name="Pattern">The trim pattern id.</param>
/// <param name="ShowInTooltip">Whether the tooltip shows it (pre-1.21.5 flag; kept for round-trip).</param>
public sealed record TrimComponent(Identifier Material, Identifier Pattern, bool ShowInTooltip = true);

/// <summary>Leather dye color (<c>minecraft:dyed_color</c>).</summary>
/// <param name="Rgb">The packed RGB color.</param>
/// <param name="ShowInTooltip">Whether the tooltip shows it (pre-1.21.5 flag; kept for round-trip).</param>
public sealed record DyedColorComponent(int Rgb, bool ShowInTooltip = true);

/// <summary>The base color of a shield or banner (<c>minecraft:base_color</c>).</summary>
/// <param name="Color">The dye-color name.</param>
public sealed record BaseColorComponent(string Color);

/// <summary>Map color override (<c>minecraft:map_color</c>).</summary>
/// <param name="Rgb">The packed RGB color.</param>
public sealed record MapColorComponent(int Rgb);

/// <summary>Banner pattern layers (<c>minecraft:banner_patterns</c>).</summary>
/// <param name="Layers">The pattern layers, bottom to top.</param>
public sealed record BannerPatternsComponent(IReadOnlyList<BannerPatternLayer> Layers);

/// <summary>The contents of a container item such as a shulker box (<c>minecraft:container</c>).</summary>
/// <param name="Items">The contained stacks with their slots.</param>
public sealed record ContainerComponent(IReadOnlyList<ContainerSlotEntry> Items);

/// <summary>The contents of a bundle (<c>minecraft:bundle_contents</c>).</summary>
/// <param name="Items">The contained stacks, in order.</param>
public sealed record BundleContentsComponent(IReadOnlyList<ItemStack> Items);

/// <summary>Projectiles loaded into a crossbow (<c>minecraft:charged_projectiles</c>).</summary>
/// <param name="Projectiles">The charged stacks, in order.</param>
public sealed record ChargedProjectilesComponent(IReadOnlyList<ItemStack> Projectiles);

/// <summary>What an item turns into when it is used up (<c>minecraft:use_remainder</c>, 1.21.2+): a bucket becomes an empty bucket, a honey bottle a glass bottle.</summary>
/// <param name="ConvertInto">The replacement stack. Never empty on the wire.</param>
public sealed record UseRemainderComponent(ItemStack ConvertInto);

/// <summary>The block a sulfur cube has absorbed (<c>minecraft:sulfur_cube_content</c>, 26.2+).</summary>
/// <param name="AbsorbedBlockItemStack">The absorbed block's item stack. Never empty on the wire.</param>
public sealed record SulfurCubeContentComponent(ItemStack AbsorbedBlockItemStack);

/// <summary>The four decorated-pot faces (<c>minecraft:pot_decorations</c>), as raw item network ids in wire order (back, left, right, front; a brick face is the plain brick item). Raw ids keep the component registry-independent: an id outside the session item registry still round-trips.</summary>
/// <param name="SherdItemIds">The per-face item network ids, up to four.</param>
public sealed record PotDecorationsComponent(IReadOnlyList<int> SherdItemIds);

/// <summary>A player head profile (<c>minecraft:profile</c>).</summary>
/// <param name="Profile">The resolvable profile.</param>
public sealed record ProfileComponent(ResolvableProfile Profile);

/// <summary>A writable book's pages (<c>minecraft:writable_book_content</c>).</summary>
/// <param name="Pages">The raw pages.</param>
public sealed record WritableBookContentComponent(IReadOnlyList<BookPage> Pages);

/// <summary>A written book's content (<c>minecraft:written_book_content</c>).</summary>
/// <param name="Title">The book title (the raw, unfiltered title).</param>
/// <param name="Author">The book author.</param>
/// <param name="Generation">The copy generation (0 original .. 3).</param>
/// <param name="Pages">The component pages.</param>
/// <param name="Resolved">Whether the pages have been resolved server-side.</param>
/// <param name="FilteredTitle">The optional filtered variant of the title, when the server sent one.</param>
public sealed record WrittenBookContentComponent(
    string Title,
    string Author,
    int Generation,
    IReadOnlyList<WrittenBookPage> Pages,
    bool Resolved = false,
    string? FilteredTitle = null);

/// <summary>The placed block-state overrides carried on a block item (<c>minecraft:block_state</c>).</summary>
/// <param name="Properties">The property name/value overrides.</param>
public sealed record BlockStateComponent(IReadOnlyDictionary<string, string> Properties);

/// <summary>Firework rocket data (<c>minecraft:fireworks</c>).</summary>
/// <param name="FlightDuration">The flight-duration (gunpowder count).</param>
/// <param name="Explosions">The explosion layers.</param>
public sealed record FireworksComponent(int FlightDuration, IReadOnlyList<FireworkExplosion> Explosions);

/// <summary>A single firework star explosion (<c>minecraft:firework_explosion</c>).</summary>
/// <param name="Explosion">The explosion layer.</param>
public sealed record FireworkExplosionComponent(FireworkExplosion Explosion);

/// <summary>Opaque custom NBT (<c>minecraft:custom_data</c>). Carries an order-preserving compound verbatim.</summary>
/// <param name="Data">The raw compound.</param>
public sealed record CustomDataComponent(NbtCompound Data);

/// <summary>The legacy escape hatch: the residual pre-1.20.5 item NBT that the bridge did not map to a well-known component. Retaining it verbatim makes re-encoding byte-faithful for legacy servers and for click echoes, where the server compares the client's stack against its own. This is not a vanilla wire component; it exists only in the model for legacy round-trip.</summary>
/// <param name="Nbt">The residual, order-preserving compound.</param>
public sealed record LegacyNbtComponent(NbtCompound Nbt);

/// <summary>Opaque block-entity data (<c>minecraft:block_entity_data</c>); references the world module, carried as NBT.</summary>
/// <param name="Data">The raw compound.</param>
/// <param name="TypeId">The block-entity type registry id moved ahead of the tag on the 1.21.9 wire. Null on 1.20.5-1.21.8, where the component is the bare compound and the type rides inside it as the <c>id</c> member.</param>
public sealed record BlockEntityDataComponent(NbtCompound Data, int? TypeId = null);

/// <summary>Opaque entity data (<c>minecraft:entity_data</c>); references the entity module, carried as NBT.</summary>
/// <param name="Data">The raw compound.</param>
/// <param name="TypeId">The entity type registry id moved ahead of the tag on the 1.21.9 wire. Null on 1.20.5-1.21.8, where the component is the bare compound and the type rides inside it as the <c>id</c> member.</param>
public sealed record EntityDataComponent(NbtCompound Data, int? TypeId = null);

/// <summary>Opaque bucketed-entity data (<c>minecraft:bucket_entity_data</c>); references the entity module, carried as NBT.</summary>
/// <param name="Data">The raw compound.</param>
public sealed record BucketEntityDataComponent(NbtCompound Data);

// Every payload below preserves the fields for each era it applies to. The per-era wire form is recorded on each codec in ItemComponentCodecs.

/// <summary>The saved-map id a filled map points at (<c>minecraft:map_id</c>).</summary>
/// <param name="Id">The map id; the wire form is a VarInt on every era from 1.20.5 to 26.2.</param>
public sealed record MapIdComponent(int Id);

/// <summary>The post-processing a freshly crafted map still owes (<c>minecraft:map_post_processing</c>). The server clears it once the map is materialized, so it is only ever seen in transit.</summary>
/// <param name="Kind">The pending operation.</param>
public sealed record MapPostProcessingComponent(MapPostProcessingKind Kind);

/// <summary>The pending map post-processing operation. The numeric values are the vanilla wire ids.</summary>
public enum MapPostProcessingKind
{
    /// <summary>Lock the map on materialization (<c>LOCK</c>, wire id 0).</summary>
    Lock = 0,

    /// <summary>Zoom the map out one level on materialization (<c>SCALE</c>, wire id 1).</summary>
    Scale = 1,
}

/// <summary>Forces the enchantment glint on or off (<c>minecraft:enchantment_glint_override</c>).</summary>
/// <param name="ShowGlint">True to force the glint on, false to force it off.</param>
public sealed record EnchantmentGlintOverrideComponent(bool ShowGlint);

/// <summary>The bad-omen amplifier an ominous bottle grants (<c>minecraft:ominous_bottle_amplifier</c>).</summary>
/// <param name="Amplifier">The effect amplifier (0..4 in vanilla).</param>
public sealed record OminousBottleAmplifierComponent(int Amplifier);

/// <summary>A multiplier on the duration of the effects this item applies (<c>minecraft:potion_duration_scale</c>).</summary>
/// <param name="Scale">The duration multiplier.</param>
public sealed record PotionDurationScaleComponent(float Scale);

/// <summary>The model an item renders with (<c>minecraft:item_model</c>).</summary>
/// <param name="Model">The model asset id.</param>
public sealed record ItemModelComponent(Identifier Model);

/// <summary>The tooltip background style (<c>minecraft:tooltip_style</c>).</summary>
/// <param name="Style">The tooltip style asset id.</param>
public sealed record TooltipStyleComponent(Identifier Style);

/// <summary>The sound a note block plays with this head on it (<c>minecraft:note_block_sound</c>).</summary>
/// <param name="Sound">The sound event id.</param>
public sealed record NoteBlockSoundComponent(Identifier Sound);

/// <summary>Which tooltip sections are hidden (<c>minecraft:tooltip_display</c>, 1.21.5+). Replaces the pre-1.21.5 per-component <c>showInTooltip</c> booleans and the <c>hide_tooltip</c> / <c>hide_additional_tooltip</c> markers.</summary>
/// <param name="HideTooltip">True to hide the whole tooltip.</param>
/// <param name="HiddenComponents">The components whose tooltip lines are hidden, as this era's component wire ids. Raw ids keep the record era-independent and make the round trip exact even for a component UMPK does not model.</param>
public sealed record TooltipDisplayComponent(bool HideTooltip, IReadOnlyList<int> HiddenComponents);

/// <summary>A component whose whole payload is a single network NBT tag. Carrying the tag verbatim is byte-exact in both directions.</summary>
/// <param name="Tag">The payload tag, in wire order.</param>
public sealed record NbtPayloadComponent(NbtTag Tag);

/// <summary>A marker component whose payload is zero bytes.</summary>
public sealed record UnitMarkerComponent
{
    /// <summary>The single marker value.</summary>
    public static UnitMarkerComponent Instance { get; } = new();
}

/// <summary>A dye-color valued component. Several components share this payload: the wolf/cat collar, the sheep and shulker colors, both tropical-fish colors, and 26.2's <c>minecraft:dye</c>. All of them are <c>DyeColor.STREAM_CODEC</c> on the wire (a VarInt id) and the vanilla color name in data form.</summary>
/// <param name="Color">The vanilla dye-color name (white .. black).</param>
public sealed record DyeColorValueComponent(string Color);

/// <summary>The charge fraction an attack needs before it counts as fully charged (<c>minecraft:minimum_attack_charge</c>, 26.2).</summary>
/// <param name="Value">The minimum charge.</param>
public sealed record MinimumAttackChargeComponent(float Value);

/// <summary>An extra cost added to a villager trade (<c>minecraft:additional_trade_cost</c>, 26.2).</summary>
/// <param name="Value">The added cost.</param>
public sealed record AdditionalTradeCostComponent(int Value);

// These component records preserve the fields required for byte-exact re-encoding.

/// <summary>The bees inside a beehive or bee-nest item (<c>minecraft:bees</c>).</summary>
/// <param name="Occupants">The occupants, in wire order.</param>
public sealed record BeesComponent(IReadOnlyList<BeeOccupant> Occupants);

/// <summary>An adventure-mode block predicate list (<c>minecraft:can_break</c> and <c>minecraft:can_place_on</c> share this payload).</summary>
/// <param name="Predicates">The block predicates, in wire order.</param>
/// <param name="ShowInTooltip">The trailing pre-1.21.5 tooltip flag. 1.21.5 removed it from this payload (it moved to <c>minecraft:tooltip_display</c>), so it is only read/written on 766-769.</param>
public sealed record AdventureModePredicateComponent(
    IReadOnlyList<BlockPredicateEntry> Predicates,
    bool ShowInTooltip = true);

/// <summary>How an item is eaten or drunk (<c>minecraft:consumable</c>, 1.21.2+).</summary>
/// <param name="ConsumeSeconds">How long consuming takes, in seconds.</param>
/// <param name="Animation">The use-animation id (<c>ItemUseAnimation</c>), kept numeric to round-trip exactly.</param>
/// <param name="Sound">The sound played while consuming.</param>
/// <param name="HasConsumeParticles">Whether consuming spawns particles.</param>
/// <param name="OnConsumeEffects">The effects applied on consumption.</param>
public sealed record ConsumableComponent(
    float ConsumeSeconds,
    int Animation,
    SoundEventRef Sound,
    bool HasConsumeParticles,
    IReadOnlyList<ConsumeEffectEntry> OnConsumeEffects);

/// <summary>The damage types an item protects its holder from (<c>minecraft:damage_resistant</c>, 1.21.2+).</summary>
/// <param name="Types">The damage-type set. Through protocol 774 the wire carries a bare tag identifier, which lands in <see cref="HolderSetRef.Tag"/>; 26.1 widened it to a full holder set.</param>
public sealed record DamageResistantComponent(HolderSetRef Types);

/// <summary>What a totem-like item does when it saves its holder (<c>minecraft:death_protection</c>, 1.21.2+).</summary>
/// <param name="DeathEffects">The effects applied when the protection triggers.</param>
public sealed record DeathProtectionComponent(IReadOnlyList<ConsumeEffectEntry> DeathEffects);

/// <summary>The enchanting-table value of an item (<c>minecraft:enchantable</c>, 1.21.2+).</summary>
/// <param name="Value">The enchantability value.</param>
public sealed record EnchantableComponent(int Value);

/// <summary>How an item is worn (<c>minecraft:equippable</c>, 1.21.2+).</summary>
/// <param name="Slot">The equipment-slot id, kept numeric to round-trip exactly.</param>
/// <param name="EquipSound">The sound played on equipping.</param>
/// <param name="AssetId">The optional equipment asset (a model identifier before 1.21.4).</param>
/// <param name="CameraOverlay">The optional first-person camera overlay texture.</param>
/// <param name="AllowedEntities">The optional entity types allowed to wear it.</param>
/// <param name="Dispensable">Whether a dispenser can equip it.</param>
/// <param name="Swappable">Whether right-click swaps it into its slot.</param>
/// <param name="DamageOnHurt">Whether it takes durability damage when its wearer is hurt.</param>
/// <param name="EquipOnInteract">Whether interacting with an entity equips it (1.21.5+).</param>
/// <param name="CanBeSheared">Whether shears can remove it (1.21.6+).</param>
/// <param name="ShearingSound">The sound played when it is sheared off (1.21.6+).</param>
public sealed record EquippableComponent(
    int Slot,
    SoundEventRef EquipSound,
    Identifier? AssetId,
    Identifier? CameraOverlay,
    HolderSetRef? AllowedEntities,
    bool Dispensable,
    bool Swappable,
    bool DamageOnHurt,
    bool EquipOnInteract = false,
    bool CanBeSheared = false,
    SoundEventRef? ShearingSound = null);

/// <summary>The goat-horn instrument an item plays (<c>minecraft:instrument</c>).</summary>
/// <remarks>Three branches across the eras, and the decode records which one it took so the re-encode is exact: a registry REFERENCE (<paramref name="HolderId"/>), an INLINE instrument (<paramref name="Direct"/>), or - only on 770-774, where the payload is an <c>EitherHolder</c> - a bare registry KEY identifier (<paramref name="ReferenceKey"/>).</remarks>
/// <param name="HolderId">The instrument registry network id, for the reference branch.</param>
/// <param name="Direct">The inline instrument, for the direct branch.</param>
/// <param name="ReferenceKey">The registry key identifier, for the 770-774 either-right branch.</param>
public sealed record InstrumentComponent(int? HolderId, InstrumentDetails? Direct, Identifier? ReferenceKey = null);

/// <summary>The music-disc song an item plays in a jukebox (<c>minecraft:jukebox_playable</c>, 1.21+).</summary>
/// <param name="HolderId">The jukebox-song registry network id, for the reference branch.</param>
/// <param name="Direct">The inline song, for the direct branch.</param>
/// <param name="ReferenceKey">The registry key identifier, for the 767-774 either-right branch.</param>
/// <param name="ShowInTooltip">The trailing tooltip flag, present only on 767-769.</param>
public sealed record JukeboxPlayableComponent(
    int? HolderId,
    JukeboxSongDetails? Direct,
    Identifier? ReferenceKey = null,
    bool ShowInTooltip = true);

/// <summary>Where a lodestone compass points (<c>minecraft:lodestone_tracker</c>).</summary>
/// <param name="Dimension">The target dimension identifier, when a target is set.</param>
/// <param name="Position">The target block position, when a target is set.</param>
/// <param name="Tracked">Whether the compass keeps tracking its lodestone.</param>
public sealed record LodestoneTrackerComponent(Identifier? Dimension, BlockPos? Position, bool Tracked);

/// <summary>The items that repair this one in an anvil (<c>minecraft:repairable</c>, 1.21.2+).</summary>
/// <param name="Items">The repairing item set.</param>
public sealed record RepairableComponent(HolderSetRef Items);

/// <summary>The cooldown applied after using an item (<c>minecraft:use_cooldown</c>, 1.21.2+).</summary>
/// <param name="Seconds">The cooldown length, in seconds.</param>
/// <param name="CooldownGroup">The optional shared cooldown group identifier.</param>
public sealed record UseCooldownComponent(float Seconds, Identifier? CooldownGroup);
