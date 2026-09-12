using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Game.Items.Components;

// Shared value types that appear inside more than one component record. These are version-agnostic model shapes: only their wire codecs are versioned in the Java data/codec layer. Payloads that reference another module's types (entity data, block-entity data) are carried opaquely as NbtCompound.

/// <summary>A potion effect instance carried by a potion or a stew.</summary>
/// <param name="Effect">The mob-effect registry entry.</param>
/// <param name="Amplifier">The effect amplifier (0-based).</param>
/// <param name="Duration">Duration in ticks; a negative value means infinite.</param>
/// <param name="Ambient">Whether the effect is ambient (beacon-style).</param>
/// <param name="ShowParticles">Whether particles are shown.</param>
/// <param name="ShowIcon">Whether the HUD icon is shown.</param>
public sealed record MobEffectDetail(
    Identifier Effect,
    int Amplifier = 0,
    int Duration = 0,
    bool Ambient = false,
    bool ShowParticles = true,
    bool ShowIcon = true);

/// <summary>One firework explosion layer (also usable as a standalone firework_explosion component).</summary>
/// <param name="Shape">The explosion shape name (small_ball, large_ball, star, creeper, burst).</param>
/// <param name="Colors">The primary colors, as packed RGB ints.</param>
/// <param name="FadeColors">The fade colors, as packed RGB ints.</param>
/// <param name="HasTrail">Whether the explosion trails.</param>
/// <param name="HasTwinkle">Whether the explosion twinkles (flickers).</param>
public sealed record FireworkExplosion(
    string Shape,
    IReadOnlyList<int> Colors,
    IReadOnlyList<int> FadeColors,
    bool HasTrail = false,
    bool HasTwinkle = false);

/// <summary>One banner pattern layer.</summary>
/// <remarks>The wire form has two branches. A REFERENCE carries only the pattern's registry network id, kept here as the synthetic identifier <c>umpk:banner_pattern_&lt;id&gt;</c> (the same convention <c>minecraft:trim</c> and <c>minecraft:potion_contents</c> already use for registry ids this layer cannot resolve). A DIRECT holder carries the pattern inline instead, as an asset identifier plus a translation key; those land in <paramref name="AssetId"/> and <paramref name="TranslationKey"/>, and their presence is what tells the encoder to re-emit the inline branch.</remarks>
/// <param name="Pattern">The pattern registry id (or an inline asset id on newer versions).</param>
/// <param name="Color">The dye-color name.</param>
/// <param name="AssetId">The inline pattern's asset identifier, for the direct-holder branch.</param>
/// <param name="TranslationKey">The inline pattern's translation key, for the direct-holder branch.</param>
public sealed record BannerPatternLayer(
    Identifier Pattern,
    string Color,
    Identifier? AssetId = null,
    string? TranslationKey = null);

/// <summary>One page of a WRITABLE book (<c>minecraft:writable_book_content</c>). The wire carries plain strings here, not components, so the model keeps strings; the written-book counterpart is <see cref="WrittenBookPage"/>.</summary>
/// <param name="Raw">The raw page text.</param>
/// <param name="Filtered">The optional filtered variant of the page.</param>
public sealed record BookPage(string Raw, string? Filtered = null);

/// <summary>One page of a WRITTEN book (<c>minecraft:written_book_content</c>). The wire carries a text component per page, so the model keeps a <see cref="Component"/> rather than flattening to text: flattening loses styling and makes re-encoding non-byte-exact.</summary>
/// <param name="Raw">The page component.</param>
/// <param name="Filtered">The optional filtered variant of the page.</param>
public sealed record WrittenBookPage(Component Raw, Component? Filtered = null);

/// <summary>A player game profile as carried by the <c>minecraft:profile</c> component.</summary>
/// <param name="Name">The profile name, when present.</param>
/// <param name="Id">The profile UUID, when present.</param>
/// <param name="Properties">The signed profile properties (textures, ...), in order.</param>
/// <param name="Resolved">True when the server sent a fully resolved game profile rather than a partial lookup key. 1.21.9 rewrote the component's wire form as either a complete or partial profile, and the two branches carry their fields in DIFFERENT orders (a resolved profile is uuid-then-name, a partial is optional-name-then-optional-uuid), so which branch it was has to survive the decode for a re-encode to stay byte-exact. Always false below protocol 773, where the wire has only the partial form.</param>
/// <param name="SkinPatch">The 1.21.9+ trailing skin patch; null on 1.20.5-1.21.8, where the component has no such field.</param>
public sealed record ResolvableProfile(
    string? Name,
    Guid? Id,
    IReadOnlyList<ProfileProperty> Properties,
    bool Resolved = false,
    PlayerSkinPatch? SkinPatch = null);

/// <summary>The 1.21.9+ per-profile skin override carried after the profile itself. Every field is optional and the all-empty patch is the common case.</summary>
/// <param name="Body">The body texture asset id.</param>
/// <param name="Cape">The cape texture asset id.</param>
/// <param name="Elytra">The elytra texture asset id.</param>
/// <param name="SlimModel">True for the slim player model, false for the wide one, null for unset.</param>
public sealed record PlayerSkinPatch(
    Identifier? Body = null,
    Identifier? Cape = null,
    Identifier? Elytra = null,
    bool? SlimModel = null)
{
    /// <summary>The patch that overrides nothing, which is what an unmodified head carries.</summary>
    public static PlayerSkinPatch Empty { get; } = new();
}

/// <summary>One entry of a <see cref="ResolvableProfile"/>'s property list.</summary>
/// <param name="Name">The property name (e.g. <c>textures</c>).</param>
/// <param name="Value">The base64 property value.</param>
/// <param name="Signature">The optional Yggdrasil signature.</param>
public sealed record ProfileProperty(string Name, string Value, string? Signature = null);

/// <summary>An item held inside a container/bundle component, remembered with its slot.</summary>
/// <param name="Slot">The slot index inside the container.</param>
/// <param name="Item">The contained stack.</param>
public sealed record ContainerSlotEntry(int Slot, ItemStack Item);

/// <summary>A single attribute modifier entry of the <c>minecraft:attribute_modifiers</c> component.</summary>
/// <param name="Attribute">The attribute registry entry the modifier applies to.</param>
/// <param name="ModifierId">The modifier's namespaced id (1.21+) or legacy UUID string.</param>
/// <param name="Amount">The modifier amount.</param>
/// <param name="Operation">The operation (add_value, add_multiplied_base, add_multiplied_total).</param>
/// <param name="Slot">The equipment-slot group the modifier is active in.</param>
public sealed record AttributeModifierEntry(
    RegistryEntry<AttributeDefinition> Attribute,
    string ModifierId,
    double Amount,
    string Operation,
    string Slot);

/// <summary>A localized or literal display value that may be a raw string or a resolved component.</summary>
/// <param name="Text">The text component form.</param>
public sealed record DisplayText(Component Text);

/// <summary>An opaque NBT payload used for components that reference another module's model.</summary>
/// <param name="Nbt">The raw, order-preserving compound as received.</param>
public sealed record OpaqueNbt(NbtCompound Nbt);

// Shared component value types preserve the wire choices needed for byte-exact re-encoding.

/// <summary>A sound event holder. The wire is a VarInt: non-zero means a registry REFERENCE whose network id is that value minus one; zero means the sound is written INLINE, as an identifier plus an optional fixed range. Which branch it was has to survive the decode for a byte-exact re-encode, so exactly one of <paramref name="RegistryId"/> and <paramref name="SoundId"/> is set.</summary>
/// <param name="RegistryId">The sound-event registry network id, for the reference branch.</param>
/// <param name="SoundId">The inline sound identifier, for the direct branch.</param>
/// <param name="FixedRange">The inline sound's optional fixed attenuation range.</param>
public sealed record SoundEventRef(int? RegistryId, Identifier? SoundId = null, float? FixedRange = null);

/// <summary>A holder set carried as a VarInt marker where zero means a NAMED TAG (an identifier follows) and any other value <c>n</c> means <c>n - 1</c> direct registry ids follow. The two branches are distinguished here by whether <paramref name="Tag"/> is set.</summary>
/// <param name="Tag">The tag identifier, for the named-tag branch.</param>
/// <param name="Ids">The direct registry network ids, for the inline branch.</param>
public sealed record HolderSetRef(Identifier? Tag, IReadOnlyList<int> Ids);

/// <summary>One bee stored inside a beehive or bee-nest item.</summary>
/// <param name="EntityData">The occupant's entity NBT.</param>
/// <param name="TicksInHive">Ticks the bee has already spent in the hive.</param>
/// <param name="MinTicksInHive">Ticks the bee must stay before it may leave.</param>
/// <param name="EntityTypeId">From 1.21.9, the entity-type registry id precedes the tag on the wire; null on 766-772, where the occupant carried a bare <c>CustomData</c> compound.</param>
public sealed record BeeOccupant(NbtCompound EntityData, int TicksInHive, int MinTicksInHive, int? EntityTypeId = null);

/// <summary>One entry of a <c>ConsumeEffect</c> list (<c>minecraft:consumable</c> / <c>minecraft:death_protection</c>). Vanilla dispatches on a VarInt <c>the consume-effect registry</c> id, so <paramref name="TypeId"/> is the discriminator and only the fields belonging to that branch are populated.</summary>
/// <param name="TypeId">The consume-effect type id: 0 apply_effects, 1 remove_effects, 2 clear_all_effects, 3 teleport_randomly, 4 play_sound.</param>
/// <param name="ApplyEffects">The applied effect instances (type 0).</param>
/// <param name="Probability">The application probability (type 0).</param>
/// <param name="RemoveEffects">The removed mob-effect holder set (type 1).</param>
/// <param name="TeleportDiameter">The teleport diameter (type 3).</param>
/// <param name="Sound">The played sound (type 4).</param>
public sealed record ConsumeEffectEntry(
    int TypeId,
    IReadOnlyList<MobEffectDetail>? ApplyEffects = null,
    float Probability = 1.0f,
    HolderSetRef? RemoveEffects = null,
    float TeleportDiameter = 0.0f,
    SoundEventRef? Sound = null);

/// <summary>One state-property matcher inside a block predicate (<c>StatePropertiesPredicate.PropertyMatcher</c>). The wire is a property name then an either: the left branch is an EXACT value string, the right is a RANGE of two optional strings.</summary>
/// <param name="Name">The block-state property name.</param>
/// <param name="Exact">True for the exact-value branch, false for the ranged branch.</param>
/// <param name="Value">The exact value (exact branch).</param>
/// <param name="MinValue">The inclusive lower bound (ranged branch).</param>
/// <param name="MaxValue">The inclusive upper bound (ranged branch).</param>
public sealed record StatePropertyMatcher(
    string Name,
    bool Exact,
    string? Value = null,
    string? MinValue = null,
    string? MaxValue = null);

/// <summary>One <c>BlockPredicate</c> of an adventure-mode predicate (<c>minecraft:can_break</c> / <c>minecraft:can_place_on</c>), in its pre-1.21.5 shape: an optional block holder set, an optional list of state matchers, and an optional block-entity NBT predicate.</summary>
/// <param name="Blocks">The optional matching block set.</param>
/// <param name="Properties">The optional state-property matchers; null when the field was absent.</param>
/// <param name="Nbt">The optional block-entity NBT predicate compound.</param>
public sealed record BlockPredicateEntry(
    HolderSetRef? Blocks,
    IReadOnlyList<StatePropertyMatcher>? Properties,
    NbtCompound? Nbt);

/// <summary>The inline (direct-holder) form of a goat-horn instrument (<c>Instrument.DIRECT_STREAM_CODEC</c>). 1.21.2 re-shaped it: the use duration went from a VarInt tick count to a FLOAT second count and a description component was appended, so exactly one of the two duration fields is set.</summary>
/// <param name="Sound">The instrument's sound event.</param>
/// <param name="UseDurationTicks">The 766/767 use duration, in ticks.</param>
/// <param name="UseDurationSeconds">The 768+ use duration, in seconds.</param>
/// <param name="Range">The audible range.</param>
/// <param name="Description">The 768+ description component.</param>
public sealed record InstrumentDetails(
    SoundEventRef Sound,
    int? UseDurationTicks,
    float? UseDurationSeconds,
    float Range,
    Component? Description);

/// <summary>The inline (direct-holder) form of a jukebox song (<c>JukeboxSong.DIRECT_STREAM_CODEC</c>).</summary>
/// <param name="Sound">The song's sound event.</param>
/// <param name="Description">The song's description component.</param>
/// <param name="LengthInSeconds">The song length, in seconds.</param>
/// <param name="ComparatorOutput">The comparator signal strength the song drives.</param>
public sealed record JukeboxSongDetails(
    SoundEventRef Sound,
    Component Description,
    float LengthInSeconds,
    int ComparatorOutput);
