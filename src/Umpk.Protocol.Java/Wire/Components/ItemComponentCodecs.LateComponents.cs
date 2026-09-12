using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Codecs for fifteen item components and the shared readers used by their payloads. Unsupported component payloads are handled by the packet-scoped policy: one packet plus one warning, never the session.</summary>
/// <remarks>
/// Each codec is bound only to eras with the same wire shape. Where a record's shape moved, the codec takes an era flag and both forms are documented on it.
/// <para><c>minecraft:can_break</c> and <c>minecraft:can_place_on</c> are bound on 766-769 ONLY. See <see cref="AdventureModePredicateCodecImpl"/> for the reason: protocol 770 added a matcher field whose second half is an open dispatch over recursive compact component payloads that cannot be skipped past (the predicate-dispatch half is self-delimiting NBT and could be).</para>
/// </remarks>
internal static partial class ItemComponentCodecs
{
    // Component codecs

    /// <summary><c>minecraft:banner_patterns</c> on every component era: a VarInt-counted list of pattern-holder VarInts and dye-color VarInts. The layout is unchanged on protocols 766-776.</summary>
    public static ItemComponentCodec BannerPatterns { get; } = new BannerPatternsCodecImpl();

    /// <summary><c>minecraft:bees</c> on 766-772: a bare compound tag per occupant.</summary>
    public static ItemComponentCodec Bees { get; } = new BeesCodecImpl(typedEntityData: false);

    /// <summary><c>minecraft:bees</c> from 773 (1.21.9): a VarInt entity-type id precedes each occupant's compound. This is the same 1.21.9 move that reshaped <c>entity_data</c>, and it rides the same <c>ItemComponentLayout.TypedEntityData</c> axis.</summary>
    public static ItemComponentCodec BeesV1_21_9 { get; } = new BeesCodecImpl(typedEntityData: true);

    /// <summary><c>minecraft:can_break</c> on 766-769.</summary>
    public static ItemComponentCodec CanBreakV1_20_5 { get; } = new AdventureModePredicateCodecImpl(DataComponents.CanBreak);

    /// <summary><c>minecraft:can_place_on</c> on 766-769.</summary>
    public static ItemComponentCodec CanPlaceOnV1_20_5 { get; } = new AdventureModePredicateCodecImpl(DataComponents.CanPlaceOn);

    /// <summary><c>minecraft:consumable</c> on protocols 768-776: float seconds, animation VarInt, sound holder, particle boolean, then a VarInt-counted list of consume effects.</summary>
    public static ItemComponentCodec Consumable { get; } = new ConsumableCodecImpl();

    /// <summary><c>minecraft:damage_resistant</c> on 768-774: a bare tag identifier string.</summary>
    public static ItemComponentCodec DamageResistant { get; } = new DamageResistantCodecImpl(holderSetForm: false);

    /// <summary><c>minecraft:damage_resistant</c> on 775/776: a holder set whose marker VarInt precedes either a tag identifier or direct holder ids.</summary>
    public static ItemComponentCodec DamageResistantV26_1 { get; } = new DamageResistantCodecImpl(holderSetForm: true);

    /// <summary><c>minecraft:death_protection</c> from 768: a VarInt-counted list of consume effects, unchanged through protocol 776.</summary>
    public static ItemComponentCodec DeathProtection { get; } = new DeathProtectionCodecImpl();

    /// <summary><c>minecraft:enchantable</c> from 768: one VarInt, unchanged through protocol 776.</summary>
    public static ItemComponentCodec Enchantable { get; } = new VarIntCodec(
        DataComponents.Enchantable,
        static v => new EnchantableComponent(v),
        static v => ((EnchantableComponent)v).Value);

    /// <summary><c>minecraft:equippable</c> on 768/769: slot, equip sound, optional asset/model id, optional camera overlay, optional entity-type holder set, then dispensable/swappable/damage_on_hurt. 1.21.4 renamed the third field from <c>model</c> to <c>assetId</c>; it remains the same identifier string on the wire.</summary>
    public static ItemComponentCodec EquippableV1_21_2 { get; } = new EquippableCodecImpl(hasEquipOnInteract: false, hasShearing: false);

    /// <summary><c>minecraft:equippable</c> on 770: 1.21.5 appended <c>equipOnInteract</c>.</summary>
    public static ItemComponentCodec EquippableV1_21_5 { get; } = new EquippableCodecImpl(hasEquipOnInteract: true, hasShearing: false);

    /// <summary><c>minecraft:equippable</c> from 771: 1.21.6 appended <c>canBeSheared</c> and a <c>shearingSound</c>. Unchanged through 26.2.</summary>
    public static ItemComponentCodec EquippableV1_21_6 { get; } = new EquippableCodecImpl(hasEquipOnInteract: true, hasShearing: true);

    /// <summary><c>minecraft:instrument</c> on 766/767: a holder marker; the inline branch is a sound holder, VarInt use duration in ticks, and float range.</summary>
    public static ItemComponentCodec InstrumentV1_20_5 { get; } =
        new InstrumentCodecImpl(v1_21_2Form: false, eitherWrapped: false, ComponentWireEra.Legacy);

    /// <summary><c>minecraft:instrument</c> on 768/769: 1.21.2 changed the direct form's use duration from a VarInt tick count to a FLOAT second count AND appended a description component.</summary>
    public static ItemComponentCodec InstrumentV1_21_2 { get; } =
        new InstrumentCodecImpl(v1_21_2Form: true, eitherWrapped: false, ComponentWireEra.Legacy);

    /// <summary><c>minecraft:instrument</c> on 770-774: a leading boolean chooses between the holder form and a bare registry-key identifier.</summary>
    public static ItemComponentCodec InstrumentV1_21_5 { get; } =
        new InstrumentCodecImpl(v1_21_2Form: true, eitherWrapped: true, ComponentWireEra.Modern);

    /// <summary><c>minecraft:instrument</c> on 775/776: the leading branch boolean is absent, leaving the holder form directly.</summary>
    public static ItemComponentCodec InstrumentV26_1 { get; } =
        new InstrumentCodecImpl(v1_21_2Form: true, eitherWrapped: false, ComponentWireEra.Modern);

    /// <summary><c>minecraft:jukebox_playable</c> on 767-769: a leading boolean chooses a holder or bare registry-key identifier, followed by a <c>showInTooltip</c> boolean. The component does not exist on 766.</summary>
    public static ItemComponentCodec JukeboxPlayableV1_21 { get; } =
        new JukeboxPlayableCodecImpl(eitherWrapped: true, hasShowInTooltip: true, ComponentWireEra.Legacy);

    /// <summary><c>minecraft:jukebox_playable</c> on 770-774: 1.21.5 dropped the trailing <c>showInTooltip</c> BOOL (it moved to <c>tooltip_display</c>) and kept the holder-or-inline wrapper.</summary>
    public static ItemComponentCodec JukeboxPlayableV1_21_5 { get; } =
        new JukeboxPlayableCodecImpl(eitherWrapped: true, hasShowInTooltip: false, ComponentWireEra.Modern);

    /// <summary><c>minecraft:jukebox_playable</c> on 775/776: both wrapper booleans are absent, leaving the holder form directly.</summary>
    public static ItemComponentCodec JukeboxPlayableV26_1 { get; } =
        new JukeboxPlayableCodecImpl(eitherWrapped: false, hasShowInTooltip: false, ComponentWireEra.Modern);

    /// <summary><c>minecraft:lodestone_tracker</c> on every component era: an optional dimension identifier and packed block-position long, followed by a tracked boolean. The layout is unchanged on 766-776.</summary>
    public static ItemComponentCodec LodestoneTracker { get; } = new LodestoneTrackerCodecImpl();

    /// <summary><c>minecraft:repairable</c> from 768: a holder set, unchanged through protocol 776.</summary>
    public static ItemComponentCodec Repairable { get; } = new HolderSetComponentCodec(
        DataComponents.Repairable,
        static set => new RepairableComponent(set),
        static v => ((RepairableComponent)v).Items);

    /// <summary><c>minecraft:suspicious_stew_effects</c> on every component era: a VarInt-counted list of mob-effect holder ids and VarInt durations. The layout is unchanged on 766-776.</summary>
    public static ItemComponentCodec SuspiciousStewEffects { get; } = new SuspiciousStewEffectsCodecImpl();

    /// <summary><c>minecraft:use_cooldown</c> from 768: float seconds plus an optional cooldown-group identifier, unchanged through protocol 776.</summary>
    public static ItemComponentCodec UseCooldown { get; } = new UseCooldownCodecImpl();

    // Shared wire readers and writers

    /// <summary>A leading VarInt of 0 selects the inline branch; any other value <c>n</c> is a registry reference with network id <c>n - 1</c>.</summary>
    private static SoundEventRef ReadSoundEvent(ref PacketReader reader)
    {
        int marker = reader.ReadVarInt();
        if (marker != 0)
            return new SoundEventRef(marker - 1);

        var soundId = Identifier.Parse(reader.ReadString());
        float? fixedRange = reader.ReadBool() ? reader.ReadFloat() : null;
        return new SoundEventRef(null, soundId, fixedRange);
    }

    private static void WriteSoundEvent(ref PacketWriter writer, SoundEventRef sound)
    {
        if (sound.RegistryId is { } id)
        {
            writer.WriteVarInt(id + 1);
            return;
        }

        writer.WriteVarInt(0);
        writer.WriteString((sound.SoundId ?? Identifier.Minecraft("empty")).ToString());
        writer.WriteOptionalStruct(sound.FixedRange, static (ref PacketWriter w, float r) => w.WriteFloat(r));
    }

    private static int HashSoundEvent(in HashOps ops, SoundEventRef sound) =>
        sound.RegistryId is { } id
            ? ops.Int(id)
            : ops.String((sound.SoundId ?? Identifier.Minecraft("empty")).ToString());

    /// <summary>A holder set starts with a VarInt marker: 0 means a tag identifier follows, while <c>n</c> means <c>n - 1</c> direct holder ids. The zero value selects the tag branch rather than an empty list.</summary>
    private static HolderSetRef ReadHolderSetRef(ref PacketReader reader)
    {
        int marker = reader.ReadVarInt();
        if (marker == 0)
            return new HolderSetRef(Identifier.Parse(reader.ReadString()), []);

        int count = marker - 1;
        if (count < 0)
            throw new ProtocolViolationException($"A holder set carries a negative entry count ({count}).");

        var ids = new int[count];
        for (int i = 0; i < count; i++)
            ids[i] = reader.ReadVarInt();

        return new HolderSetRef(null, ids);
    }

    private static void WriteHolderSetRef(ref PacketWriter writer, HolderSetRef set)
    {
        if (set.Tag is { } tag)
        {
            writer.WriteVarInt(0);
            writer.WriteString(tag.ToString());
            return;
        }

        writer.WriteVarInt(set.Ids.Count + 1);
        foreach (int id in set.Ids)
            writer.WriteVarInt(id);

    }

    private static int HashHolderSetRef(in HashOps ops, HolderSetRef set)
    {
        if (set.Tag is { } tag)
            return ops.String("#" + tag);

        var hashes = new int[set.Ids.Count];
        for (int i = 0; i < hashes.Length; i++)
            hashes[i] = ops.Int(set.Ids[i]);

        return ops.List(hashes);
    }

    /// <summary>One consume effect: a plain VarInt type id followed by the selected payload. The five types and their id order are unchanged on protocols 768-776.</summary>
    /// <remarks>The <c>apply_effects</c> branch reuses the potion effect reader and writer. That shared model has no field for the optional recursive hidden effect: decoding consumes it, while encoding always emits "absent". An effect carrying a hidden sub-effect therefore does not re-encode byte-for-byte. Consume effects normally omit that branch; supporting it requires widening the shared potion model.</remarks>
    private static ConsumeEffectEntry ReadConsumeEffect(ref PacketReader reader)
    {
        int typeId = reader.ReadVarInt();
        switch (typeId)
        {
            case 0:
                {
                    // apply_effects: effect list followed by a float probability.
                    MobEffectDetail[] effects = reader.ReadList(static (ref PacketReader r) => PotionContentsCodecImpl.ReadEffect(ref r));
                    float probability = reader.ReadFloat();
                    return new ConsumeEffectEntry(typeId, effects, probability);
                }

            case 1:
                return new ConsumeEffectEntry(typeId, RemoveEffects: ReadHolderSetRef(ref reader));

            case 2:
                // clear_all_effects has zero payload bytes.
                return new ConsumeEffectEntry(typeId);

            case 3:
                return new ConsumeEffectEntry(typeId, TeleportDiameter: reader.ReadFloat());

            case 4:
                return new ConsumeEffectEntry(typeId, Sound: ReadSoundEvent(ref reader));

            default:
                // A type id outside the five vanilla ones cannot be parsed and the compact wire has no length prefix to skip past, so this is a genuine framing dead end rather than a modeling gap. Faulting here is the same call the era table makes for an out-of-range component id.
                throw new ProtocolViolationException(
                    $"Consume-effect type id {typeId} is not one of the five vanilla types, so the rest of the payload cannot be located.");
        }
    }

    private static void WriteConsumeEffect(ref PacketWriter writer, ConsumeEffectEntry effect)
    {
        writer.WriteVarInt(effect.TypeId);
        switch (effect.TypeId)
        {
            case 0:
                writer.WriteList(
                    effect.ApplyEffects ?? [],
                    static (ref PacketWriter w, MobEffectDetail e) => PotionContentsCodecImpl.WriteEffect(ref w, e));
                writer.WriteFloat(effect.Probability);
                break;

            case 1:
                WriteHolderSetRef(ref writer, effect.RemoveEffects ?? new HolderSetRef(null, []));
                break;

            case 2:
                break;

            case 3:
                writer.WriteFloat(effect.TeleportDiameter);
                break;

            case 4:
                WriteSoundEvent(ref writer, effect.Sound ?? new SoundEventRef(0));
                break;

            default:
                throw new ProtocolViolationException(
                    $"Consume-effect type id {effect.TypeId} is not one of the five vanilla types and cannot be encoded.");
        }
    }

    private static int HashConsumeEffect(in HashOps ops, ConsumeEffectEntry effect) =>
        ops.Map([(ops.String("type"), ops.Int(effect.TypeId))]);

    // Concrete codec kinds

    /// <summary>A component whose entire payload is one holder set (<c>minecraft:repairable</c>, and <c>minecraft:damage_resistant</c> from 26.1).</summary>
    private sealed class HolderSetComponentCodec(DataComponentType type, Func<HolderSetRef, object> wrap, Func<object, HolderSetRef> unwrap)
        : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) => wrap(ReadHolderSetRef(ref reader));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            WriteHolderSetRef(ref writer, unwrap(value));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) => HashHolderSetRef(ops, unwrap(value));
    }

    /// <summary><c>minecraft:banner_patterns</c>: a VarInt-counted list of (pattern holder, dye-color VarInt). The pattern holder's inline branch is an asset identifier followed by a translation key; both values are retained so the inline branch re-encodes exactly.</summary>
    private sealed class BannerPatternsCodecImpl() : ItemComponentCodec(DataComponents.BannerPatterns)
    {
        private const string ReferencePrefix = "banner_pattern_";

        private const string DirectId = "banner_pattern_direct";

        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            BannerPatternLayer[] layers = reader.ReadList(static (ref PacketReader r) => ReadLayer(ref r));
            return new BannerPatternsComponent(layers);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteList(((BannerPatternsComponent)value).Layers, static (ref PacketWriter w, BannerPatternLayer l) => WriteLayer(ref w, l));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<BannerPatternLayer> layers = ((BannerPatternsComponent)value).Layers;
            var hashes = new int[layers.Count];
            for (int i = 0; i < hashes.Length; i++)
            {
                // Vanilla's data form is {pattern: <id or inline>, color: <name>}; the registry name behind a reference is not available at this layer, so the synthetic id string stands in (the same compromise TrimCodecImpl documents). A hash disagreement costs a slot resync.
                hashes[i] = ops.Map(
                [
                    (ops.String("pattern"), ops.String(layers[i].Pattern.ToString())),
                    (ops.String("color"), ops.String(layers[i].Color)),
                ]);
            }

            return ops.List(hashes);
        }

        private static BannerPatternLayer ReadLayer(ref PacketReader reader)
        {
            int marker = reader.ReadVarInt();
            if (marker != 0)
                return new BannerPatternLayer(
                    new Identifier("umpk", $"{ReferencePrefix}{marker - 1}"),
                    DyeColorNames.Name(reader.ReadVarInt()));

            var assetId = Identifier.Parse(reader.ReadString());
            string translationKey = reader.ReadString();
            return new BannerPatternLayer(
                new Identifier("umpk", DirectId), DyeColorNames.Name(reader.ReadVarInt()), assetId, translationKey);
        }

        private static void WriteLayer(ref PacketWriter writer, BannerPatternLayer layer)
        {
            if (layer.AssetId is { } assetId)
            {
                writer.WriteVarInt(0);
                writer.WriteString(assetId.ToString());
                writer.WriteString(layer.TranslationKey ?? string.Empty);
            }
            else
                writer.WriteVarInt(ParseReference(layer.Pattern) + 1);

            writer.WriteVarInt(DyeColorNames.Id(layer.Color));
        }

        private static int ParseReference(Identifier id) =>
            string.Equals(id.Namespace, "umpk", StringComparison.Ordinal)
            && id.Path.StartsWith(ReferencePrefix, StringComparison.Ordinal)
            && int.TryParse(id.Path.AsSpan(ReferencePrefix.Length), out int raw)
                ? raw
                : 0;
    }

    /// <summary><c>minecraft:bees</c>: a VarInt-counted list of occupants, each an entity-data payload then two VarInts (ticks in hive, minimum ticks in hive). The entity-data payload is a bare network-NBT compound through 772 and a VarInt entity-type id plus the compound from 773.</summary>
    private sealed class BeesCodecImpl(bool typedEntityData) : ItemComponentCodec(DataComponents.Bees)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            bool typed = typedEntityData;
            BeeOccupant[] occupants = reader.ReadList((ref PacketReader r) =>
            {
                int? entityTypeId = typed ? r.ReadVarInt() : null;
                NbtCompound data = AsCompound(r.ReadNbt(ItemCodecPrimitives.NetworkNbt));
                int ticksInHive = r.ReadVarInt();
                int minTicksInHive = r.ReadVarInt();
                return new BeeOccupant(data, ticksInHive, minTicksInHive, entityTypeId);
            });

            return new BeesComponent(occupants);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            bool typed = typedEntityData;
            writer.WriteList(((BeesComponent)value).Occupants, (ref PacketWriter w, BeeOccupant o) =>
            {
                if (typed)
                {
                    if (o.EntityTypeId is not { } typeId)
                    {
                        // 1.21.9 put the entity type on the wire ahead of the tag and gave it no default;
                        // guessing one would desynchronize the rest of the component list.
                        throw new ProtocolViolationException(
                            "A bee occupant needs an entity-type id on this protocol (1.21.9 moved it onto the wire).");
                    }

                    w.WriteVarInt(typeId);
                }

                w.WriteNbt(o.EntityData, ItemCodecPrimitives.NetworkNbt);
                w.WriteVarInt(o.TicksInHive);
                w.WriteVarInt(o.MinTicksInHive);
            });
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<BeeOccupant> occupants = ((BeesComponent)value).Occupants;
            var hashes = new int[occupants.Count];
            for (int i = 0; i < hashes.Length; i++)
                hashes[i] = ops.Map(
                [
                    (ops.String("entity_data"), ItemCodecPrimitives.HashNbt(ops, occupants[i].EntityData)),
                    (ops.String("ticks_in_hive"), ops.Int(occupants[i].TicksInHive)),
                    (ops.String("min_ticks_in_hive"), ops.Int(occupants[i].MinTicksInHive)),
                ]);

            return ops.List(hashes);
        }
    }

    /// <summary><c>minecraft:can_break</c> and <c>minecraft:can_place_on</c> on 766-769 only: a VarInt-counted predicate list followed by <c>showInTooltip</c>. The layout is unchanged across those protocols.</summary>
    /// <remarks>This codec is deliberately unbound on 770-776. Those protocols remove the trailing tooltip boolean and append component matchers to every block predicate. The predicate-dispatch half is a VarInt-counted list of type VarInts and self-delimiting NBT values, so unknown predicate types could be skipped. The exact-match half instead carries component-type VarInts followed by compact, recursive component payloads with no length prefix. An unknown component therefore loses framing. A partial model would turn one dropped packet into a session-level framing failure.</remarks>
    private sealed class AdventureModePredicateCodecImpl(DataComponentType type) : ItemComponentCodec(type)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            BlockPredicateEntry[] predicates = reader.ReadList(static (ref PacketReader r) => ReadPredicate(ref r));
            bool showInTooltip = reader.ReadBool();
            return new AdventureModePredicateComponent(predicates, showInTooltip);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var predicate = (AdventureModePredicateComponent)value;
            writer.WriteList(predicate.Predicates, static (ref PacketWriter w, BlockPredicateEntry p) => WritePredicate(ref w, p));
            writer.WriteBool(predicate.ShowInTooltip);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            // Not reachable in practice: hashing only runs on the 1.21.5+ hashed-slot click path, and this codec is bound only on 766-769. Kept structural rather than throwing.
            var predicate = (AdventureModePredicateComponent)value;
            return ops.Map([(ops.String("predicates"), ops.Int(predicate.Predicates.Count))]);
        }

        /// <summary>One 766-769 block predicate: optional block holder set, optional state-property matchers, then an optional NBT predicate.</summary>
        private static BlockPredicateEntry ReadPredicate(ref PacketReader reader)
        {
            HolderSetRef? blocks = reader.ReadBool() ? ReadHolderSetRef(ref reader) : null;
            StatePropertyMatcher[]? properties = reader.ReadBool()
                ? reader.ReadList(static (ref PacketReader r) => ReadMatcher(ref r))
                : null;
            NbtCompound? nbt = reader.ReadBool() ? AsCompound(reader.ReadNbt(ItemCodecPrimitives.NetworkNbt)) : null;
            return new BlockPredicateEntry(blocks, properties, nbt);
        }

        private static void WritePredicate(ref PacketWriter writer, BlockPredicateEntry predicate)
        {
            if (predicate.Blocks is { } blocks)
            {
                writer.WriteBool(true);
                WriteHolderSetRef(ref writer, blocks);
            }
            else
                writer.WriteBool(false);

            if (predicate.Properties is { } properties)
            {
                writer.WriteBool(true);
                writer.WriteList(properties, static (ref PacketWriter w, StatePropertyMatcher m) => WriteMatcher(ref w, m));
            }
            else
                writer.WriteBool(false);

            if (predicate.Nbt is { } nbt)
            {
                writer.WriteBool(true);
                writer.WriteNbt(nbt, ItemCodecPrimitives.NetworkNbt);
            }
            else
                writer.WriteBool(false);

        }

        /// <summary>One state-property matcher: a name followed by a boolean that selects either one exact string or a pair of optional range-bound strings.</summary>
        private static StatePropertyMatcher ReadMatcher(ref PacketReader reader)
        {
            string name = reader.ReadString();
            if (reader.ReadBool())
                return new StatePropertyMatcher(name, Exact: true, reader.ReadString());

            string? min = reader.ReadOptional(static (ref PacketReader r) => r.ReadString());
            string? max = reader.ReadOptional(static (ref PacketReader r) => r.ReadString());
            return new StatePropertyMatcher(name, Exact: false, null, min, max);
        }

        private static void WriteMatcher(ref PacketWriter writer, StatePropertyMatcher matcher)
        {
            writer.WriteString(matcher.Name);
            if (matcher.Exact)
            {
                writer.WriteBool(true);
                writer.WriteString(matcher.Value ?? string.Empty);
                return;
            }

            writer.WriteBool(false);
            writer.WriteOptional(matcher.MinValue, static (ref PacketWriter w, string s) => w.WriteString(s));
            writer.WriteOptional(matcher.MaxValue, static (ref PacketWriter w, string s) => w.WriteString(s));
        }
    }

    /// <summary><c>minecraft:consumable</c> from 768.</summary>
    private sealed class ConsumableCodecImpl() : ItemComponentCodec(DataComponents.Consumable)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            float consumeSeconds = reader.ReadFloat();
            int animation = reader.ReadVarInt();
            SoundEventRef sound = ReadSoundEvent(ref reader);
            bool hasConsumeParticles = reader.ReadBool();
            ConsumeEffectEntry[] effects = reader.ReadList(static (ref PacketReader r) => ReadConsumeEffect(ref r));
            return new ConsumableComponent(consumeSeconds, animation, sound, hasConsumeParticles, effects);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var consumable = (ConsumableComponent)value;
            writer.WriteFloat(consumable.ConsumeSeconds);
            writer.WriteVarInt(consumable.Animation);
            WriteSoundEvent(ref writer, consumable.Sound);
            writer.WriteBool(consumable.HasConsumeParticles);
            writer.WriteList(consumable.OnConsumeEffects, static (ref PacketWriter w, ConsumeEffectEntry e) => WriteConsumeEffect(ref w, e));
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var consumable = (ConsumableComponent)value;
            var effectHashes = new int[consumable.OnConsumeEffects.Count];
            for (int i = 0; i < effectHashes.Length; i++)
                effectHashes[i] = HashConsumeEffect(ops, consumable.OnConsumeEffects[i]);

            return ops.Map(
            [
                (ops.String("consume_seconds"), ops.Float(consumable.ConsumeSeconds)),
                (ops.String("animation"), ops.Int(consumable.Animation)),
                (ops.String("sound"), HashSoundEvent(ops, consumable.Sound)),
                (ops.String("has_consume_particles"), ops.Boolean(consumable.HasConsumeParticles)),
                (ops.String("on_consume_effects"), ops.List(effectHashes)),
            ]);
        }
    }

    /// <summary><c>minecraft:damage_resistant</c>. On 768-774 the payload is a bare tag identifier; from 775 it is a full holder set. The two shapes share one record: the tag form decodes into <see cref="HolderSetRef.Tag"/>.</summary>
    private sealed class DamageResistantCodecImpl(bool holderSetForm) : ItemComponentCodec(DataComponents.DamageResistant)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            new DamageResistantComponent(
                holderSetForm ? ReadHolderSetRef(ref reader) : new HolderSetRef(Identifier.Parse(reader.ReadString()), []));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            HolderSetRef types = ((DamageResistantComponent)value).Types;
            if (holderSetForm)
            {
                WriteHolderSetRef(ref writer, types);
                return;
            }

            if (types.Tag is not { } tag)
            {
                // The pre-26.1 wire has room for a tag key and nothing else, so a direct id list here could not be expressed. A value decoded on this era always has the tag.
                throw new ProtocolViolationException(
                    "damage_resistant carries a bare tag key on this protocol, but the value has a direct holder set instead.");
            }

            writer.WriteString(tag.ToString());
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context) =>
            ops.Map([(ops.String("types"), HashHolderSetRef(ops, ((DamageResistantComponent)value).Types))]);
    }

    /// <summary><c>minecraft:death_protection</c> from 768: a list of consume effects.</summary>
    private sealed class DeathProtectionCodecImpl() : ItemComponentCodec(DataComponents.DeathProtection)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context) =>
            new DeathProtectionComponent(reader.ReadList(static (ref PacketReader r) => ReadConsumeEffect(ref r)));

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteList(
                ((DeathProtectionComponent)value).DeathEffects,
                static (ref PacketWriter w, ConsumeEffectEntry e) => WriteConsumeEffect(ref w, e));

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<ConsumeEffectEntry> effects = ((DeathProtectionComponent)value).DeathEffects;
            var hashes = new int[effects.Count];
            for (int i = 0; i < hashes.Length; i++)
                hashes[i] = HashConsumeEffect(ops, effects[i]);

            return ops.Map([(ops.String("death_effects"), ops.List(hashes))]);
        }
    }

    /// <summary><c>minecraft:equippable</c> from 768; three era shapes, see the bound properties.</summary>
    private sealed class EquippableCodecImpl(bool hasEquipOnInteract, bool hasShearing) : ItemComponentCodec(DataComponents.Equippable)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int slot = reader.ReadVarInt();
            SoundEventRef equipSound = ReadSoundEvent(ref reader);
            Identifier? assetId = ReadOptionalIdentifier(ref reader);
            Identifier? cameraOverlay = ReadOptionalIdentifier(ref reader);
            HolderSetRef? allowedEntities = reader.ReadBool() ? ReadHolderSetRef(ref reader) : null;
            bool dispensable = reader.ReadBool();
            bool swappable = reader.ReadBool();
            bool damageOnHurt = reader.ReadBool();
            bool equipOnInteract = hasEquipOnInteract && reader.ReadBool();
            bool canBeSheared = hasShearing && reader.ReadBool();
            SoundEventRef? shearingSound = hasShearing ? ReadSoundEvent(ref reader) : null;
            return new EquippableComponent(
                slot, equipSound, assetId, cameraOverlay, allowedEntities,
                dispensable, swappable, damageOnHurt, equipOnInteract, canBeSheared, shearingSound);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var equippable = (EquippableComponent)value;
            writer.WriteVarInt(equippable.Slot);
            WriteSoundEvent(ref writer, equippable.EquipSound);
            WriteOptionalIdentifier(ref writer, equippable.AssetId);
            WriteOptionalIdentifier(ref writer, equippable.CameraOverlay);
            if (equippable.AllowedEntities is { } allowed)
            {
                writer.WriteBool(true);
                WriteHolderSetRef(ref writer, allowed);
            }
            else
                writer.WriteBool(false);

            writer.WriteBool(equippable.Dispensable);
            writer.WriteBool(equippable.Swappable);
            writer.WriteBool(equippable.DamageOnHurt);
            if (hasEquipOnInteract)
                writer.WriteBool(equippable.EquipOnInteract);

            if (hasShearing)
            {
                writer.WriteBool(equippable.CanBeSheared);
                WriteSoundEvent(ref writer, equippable.ShearingSound ?? new SoundEventRef(0));
            }
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var equippable = (EquippableComponent)value;
            return ops.Map(
            [
                (ops.String("slot"), ops.Int(equippable.Slot)),
                (ops.String("equip_sound"), HashSoundEvent(ops, equippable.EquipSound)),
                (ops.String("dispensable"), ops.Boolean(equippable.Dispensable)),
                (ops.String("swappable"), ops.Boolean(equippable.Swappable)),
                (ops.String("damage_on_hurt"), ops.Boolean(equippable.DamageOnHurt)),
            ]);
        }
    }

    /// <summary><c>minecraft:instrument</c>; four era shapes, see the bound properties.</summary>
    private sealed class InstrumentCodecImpl(bool v1_21_2Form, bool eitherWrapped, ComponentWireEra era)
        : ItemComponentCodec(DataComponents.Instrument)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            if (eitherWrapped && !reader.ReadBool())
            {
                // The wrapper's right branch is a bare registry-key identifier.
                return new InstrumentComponent(null, null, Identifier.Parse(reader.ReadString()));
            }

            int marker = reader.ReadVarInt();
            if (marker != 0)
                return new InstrumentComponent(marker - 1, null);

            SoundEventRef sound = ReadSoundEvent(ref reader);
            int? useDurationTicks = v1_21_2Form ? null : reader.ReadVarInt();
            float? useDurationSeconds = v1_21_2Form ? reader.ReadFloat() : null;
            float range = reader.ReadFloat();
            Component? description = v1_21_2Form ? ItemCodecPrimitives.ReadNetworkComponent(ref reader, era) : null;
            return new InstrumentComponent(
                null, new InstrumentDetails(sound, useDurationTicks, useDurationSeconds, range, description));
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var instrument = (InstrumentComponent)value;
            if (eitherWrapped)
            {
                if (instrument.ReferenceKey is { } key)
                {
                    writer.WriteBool(false);
                    writer.WriteString(key.ToString());
                    return;
                }

                writer.WriteBool(true);
            }

            if (instrument.HolderId is { } id)
            {
                writer.WriteVarInt(id + 1);
                return;
            }

            InstrumentDetails details = instrument.Direct
                ?? throw new ProtocolViolationException("An instrument component carries neither a holder id nor an inline instrument.");

            writer.WriteVarInt(0);
            WriteSoundEvent(ref writer, details.Sound);
            if (v1_21_2Form)
                writer.WriteFloat(details.UseDurationSeconds ?? 0.0f);

            else
                writer.WriteVarInt(details.UseDurationTicks ?? 0);

            writer.WriteFloat(details.Range);
            if (v1_21_2Form)
                ItemCodecPrimitives.WriteNetworkComponent(ref writer, details.Description ?? Component.Text(string.Empty), era);

        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var instrument = (InstrumentComponent)value;
            if (instrument.HolderId is { } id)
                return ops.Int(id);

            return instrument.ReferenceKey is { } key
                ? ops.String(key.ToString())
                : ops.Map([(ops.String("sound_event"), HashSoundEvent(ops, instrument.Direct!.Sound))]);
        }
    }

    /// <summary><c>minecraft:jukebox_playable</c> from 767; three era shapes, see the bound properties.</summary>
    private sealed class JukeboxPlayableCodecImpl(bool eitherWrapped, bool hasShowInTooltip, ComponentWireEra era)
        : ItemComponentCodec(DataComponents.JukeboxPlayable)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            int? holderId = null;
            JukeboxSongDetails? direct = null;
            Identifier? referenceKey = null;

            if (eitherWrapped && !reader.ReadBool())
                referenceKey = Identifier.Parse(reader.ReadString());

            else
            {
                int marker = reader.ReadVarInt();
                if (marker != 0)
                    holderId = marker - 1;

                else
                {
                    SoundEventRef sound = ReadSoundEvent(ref reader);
                    Component description = ItemCodecPrimitives.ReadNetworkComponent(ref reader, era);
                    float lengthInSeconds = reader.ReadFloat();
                    int comparatorOutput = reader.ReadVarInt();
                    direct = new JukeboxSongDetails(sound, description, lengthInSeconds, comparatorOutput);
                }
            }

            bool showInTooltip = !hasShowInTooltip || reader.ReadBool();
            return new JukeboxPlayableComponent(holderId, direct, referenceKey, showInTooltip);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var playable = (JukeboxPlayableComponent)value;
            if (eitherWrapped && playable.ReferenceKey is { } key)
            {
                writer.WriteBool(false);
                writer.WriteString(key.ToString());
            }
            else
            {
                if (eitherWrapped)
                    writer.WriteBool(true);

                if (playable.HolderId is { } id)
                    writer.WriteVarInt(id + 1);

                else
                {
                    JukeboxSongDetails details = playable.Direct
                        ?? throw new ProtocolViolationException("A jukebox_playable component carries neither a holder id nor an inline song.");

                    writer.WriteVarInt(0);
                    WriteSoundEvent(ref writer, details.Sound);
                    ItemCodecPrimitives.WriteNetworkComponent(ref writer, details.Description, era);
                    writer.WriteFloat(details.LengthInSeconds);
                    writer.WriteVarInt(details.ComparatorOutput);
                }
            }

            if (hasShowInTooltip)
                writer.WriteBool(playable.ShowInTooltip);

        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var playable = (JukeboxPlayableComponent)value;
            if (playable.HolderId is { } id)
                return ops.Int(id);

            return playable.ReferenceKey is { } key
                ? ops.String(key.ToString())
                : ops.Map([(ops.String("sound_event"), HashSoundEvent(ops, playable.Direct!.Sound))]);
        }
    }

    /// <summary><c>minecraft:lodestone_tracker</c>: an optional dimension identifier and 1.14+ packed block-position long (x, z, y), followed by a tracked boolean.</summary>
    private sealed class LodestoneTrackerCodecImpl() : ItemComponentCodec(DataComponents.LodestoneTracker)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            Identifier? dimension = null;
            BlockPos? position = null;
            if (reader.ReadBool())
            {
                dimension = Identifier.Parse(reader.ReadString());
                position = reader.ReadBlockPos(BlockPosLayout.Packed114);
            }

            return new LodestoneTrackerComponent(dimension, position, reader.ReadBool());
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var tracker = (LodestoneTrackerComponent)value;
            if (tracker.Dimension is { } dimension && tracker.Position is { } position)
            {
                writer.WriteBool(true);
                writer.WriteString(dimension.ToString());
                writer.WriteBlockPos(position, BlockPosLayout.Packed114);
            }
            else
                writer.WriteBool(false);

            writer.WriteBool(tracker.Tracked);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var tracker = (LodestoneTrackerComponent)value;
            var entries = new List<(int, int)>();
            if (tracker.Dimension is { } dimension && tracker.Position is { } position)
            {
                entries.Add((ops.String("dimension"), ops.String(dimension.ToString())));
                entries.Add((ops.String("pos"), ops.IntList([position.X, position.Y, position.Z])));
            }

            entries.Add((ops.String("tracked"), ops.Boolean(tracker.Tracked)));
            return ops.Map(entries);
        }
    }

    /// <summary><c>minecraft:suspicious_stew_effects</c>: a list of (mob-effect holder id, VarInt duration).</summary>
    private sealed class SuspiciousStewEffectsCodecImpl() : ItemComponentCodec(DataComponents.SuspiciousStewEffects)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            MobEffectDetail[] effects = reader.ReadList(static (ref PacketReader r) =>
            {
                int effectId = ItemCodecPrimitives.ReadHolderId(ref r);
                int duration = r.ReadVarInt();

                // The stew wire carries only the effect and its duration; the remaining MobEffectDetail members keep their defaults and are not written back, so the round trip stays exact.
                return new MobEffectDetail(new Identifier("umpk", $"effect_{effectId}"), 0, duration);
            });

            return new SuspiciousStewEffectsComponent(effects);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context) =>
            writer.WriteList(((SuspiciousStewEffectsComponent)value).Effects, static (ref PacketWriter w, MobEffectDetail e) =>
            {
                ItemCodecPrimitives.WriteHolderId(ref w, ParseEffectId(e.Effect));
                w.WriteVarInt(e.Duration);
            });

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            IReadOnlyList<MobEffectDetail> effects = ((SuspiciousStewEffectsComponent)value).Effects;
            var hashes = new int[effects.Count];
            for (int i = 0; i < hashes.Length; i++)
                hashes[i] = ops.Map(
                [
                    (ops.String("id"), ops.String(effects[i].Effect.ToString())),
                    (ops.String("duration"), ops.Int(effects[i].Duration)),
                ]);

            return ops.List(hashes);
        }

        private static int ParseEffectId(Identifier id)
        {
            const string prefix = "effect_";
            return string.Equals(id.Namespace, "umpk", StringComparison.Ordinal)
                && id.Path.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(id.Path.AsSpan(prefix.Length), out int raw)
                    ? raw
                    : 0;
        }
    }

    /// <summary><c>minecraft:use_cooldown</c> from 768: a FLOAT then an optional identifier.</summary>
    private sealed class UseCooldownCodecImpl() : ItemComponentCodec(DataComponents.UseCooldown)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            float seconds = reader.ReadFloat();
            return new UseCooldownComponent(seconds, ReadOptionalIdentifier(ref reader));
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var cooldown = (UseCooldownComponent)value;
            writer.WriteFloat(cooldown.Seconds);
            WriteOptionalIdentifier(ref writer, cooldown.CooldownGroup);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var cooldown = (UseCooldownComponent)value;
            var entries = new List<(int, int)> { (ops.String("seconds"), ops.Float(cooldown.Seconds)) };
            if (cooldown.CooldownGroup is { } group)
                entries.Add((ops.String("cooldown_group"), ops.String(group.ToString())));

            return ops.Map(entries);
        }
    }

    private static Identifier? ReadOptionalIdentifier(ref PacketReader reader) =>
        reader.ReadBool() ? Identifier.Parse(reader.ReadString()) : null;

    private static void WriteOptionalIdentifier(ref PacketWriter writer, Identifier? id)
    {
        if (id is { } present)
        {
            writer.WriteBool(true);
            writer.WriteString(present.ToString());
        }
        else
            writer.WriteBool(false);

    }
}
