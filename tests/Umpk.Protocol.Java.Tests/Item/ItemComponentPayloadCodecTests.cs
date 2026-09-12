using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Pins the wire form of fifteen item components on every era that declares them, from bytes composed at the field level and pushed through the codec the registrar actually binds - the same convention <c>PotDecorationsComponentTests</c> uses.</summary>
/// <remarks>
/// Every assertion here is frame-exact in BOTH directions: the decode is checked against the typed value, then <c>Encode(decoded)</c> is compared byte-for-byte with the composed frame. That is the bar for these components, because almost nothing downstream reads them yet - a codec that decoded plausibly but re-encoded differently would be worse than none.
/// <para>Wire ids come from the era tables. Where a component's record shape moves between eras it gets one theory per shape rather than one theory with a branch, so a failure names the affected era.</para>
/// </remarks>
public class ItemComponentPayloadCodecTests
{
    private const string SetSlot = "minecraft:container_set_slot";

    // banner_patterns has the same wire form on all ten eras.

    /// <summary>(protocol, <c>minecraft:banner_patterns</c> wire id) for every component era. This is the component at wire id 48 on protocol 766.</summary>
    public static TheoryData<int, int> BannerPatternsWireIds => new()
    {
        { 766, 48 }, { 767, 49 }, { 768, 59 }, { 769, 59 },
        { 770, 63 }, { 771, 63 }, { 773, 63 }, { 774, 70 }, { 775, 72 }, { 776, 72 },
    };

    /// <summary>A banner whose layers are registry references, which is the only branch vanilla itself emits. Each layer contains a banner-pattern holder followed by a dye color. The holder reference branch writes the network id PLUS ONE.</summary>
    [Theory]
    [MemberData(nameof(BannerPatternsWireIds))]
    public void BannerPatterns_ReferenceLayers_DecodeRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(2);
            w.WriteVarInt(6);   // holder marker: registry id 5
            w.WriteVarInt(9);   // cyan
            w.WriteVarInt(3);   // holder marker: registry id 2
            w.WriteVarInt(8);   // light_gray
        });

        BannerPatternsComponent patterns = Decode(protocol, frame, DataComponents.BannerPatterns, out byte[] reEncoded);

        Assert.Equal(
            [
                new BannerPatternLayer(new Identifier("umpk", "banner_pattern_5"), "cyan"),
                new BannerPatternLayer(new Identifier("umpk", "banner_pattern_2"), "light_gray"),
            ],
            patterns.Layers);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>The INLINE branch of the pattern holder (marker 0, then the asset id and translation key of the inline banner-pattern form). Servers send references, but the wire allows this and a datapack-defined pattern takes it, so it has to round-trip rather than be flattened.</summary>
    [Theory]
    [InlineData(766, 48)]
    [InlineData(776, 72)]
    public void BannerPatterns_InlineLayer_RoundTripsTheAssetAndTranslationKey(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(0);   // holder marker: the pattern follows inline
            w.WriteString("example:swirl");
            w.WriteString("block.example.banner.swirl");
            w.WriteVarInt(15);  // black
        });

        BannerPatternsComponent patterns = Decode(protocol, frame, DataComponents.BannerPatterns, out byte[] reEncoded);

        BannerPatternLayer layer = Assert.Single(patterns.Layers);
        Assert.Equal(new Identifier("example", "swirl"), layer.AssetId);
        Assert.Equal("block.example.banner.swirl", layer.TranslationKey);
        Assert.Equal("black", layer.Color);
        Assert.Equal(frame, reEncoded);
    }

    // bees - the occupant's entity data moved to TypedEntityData at 1.21.9, exactly like entity_data.

    /// <summary>766-772: a bare entity-data compound followed by two VarInts for ticks in hive and minimum ticks.</summary>
    [Theory]
    [InlineData(766, 53)]
    [InlineData(767, 54)]
    [InlineData(768, 64)]
    [InlineData(769, 64)]
    [InlineData(770, 68)]
    [InlineData(771, 68)]
    public void Bees_BareEntityData_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(1);
            w.WriteNbt(BeeTag(), NbtWireFormat.JavaUnnamedRoot);
            w.WriteVarInt(120);
            w.WriteVarInt(600);
        });

        BeesComponent bees = Decode(protocol, frame, DataComponents.Bees, out byte[] reEncoded);

        BeeOccupant occupant = Assert.Single(bees.Occupants);
        Assert.Null(occupant.EntityTypeId);
        Assert.Equal(120, occupant.TicksInHive);
        Assert.Equal(600, occupant.MinTicksInHive);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>From protocol 773, a VarInt entity-type id precedes the beehive occupant's compound.</summary>
    [Theory]
    [InlineData(773, 68)]
    [InlineData(774, 75)]
    [InlineData(775, 77)]
    [InlineData(776, 77)]
    public void Bees_TypedEntityData_CarriesTheLeadingTypeId(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(7);   // entity-type registry id
            w.WriteNbt(BeeTag(), NbtWireFormat.JavaUnnamedRoot);
            w.WriteVarInt(0);
            w.WriteVarInt(2400);
        });

        BeesComponent bees = Decode(protocol, frame, DataComponents.Bees, out byte[] reEncoded);

        BeeOccupant occupant = Assert.Single(bees.Occupants);
        Assert.Equal(7, occupant.EntityTypeId);
        Assert.Equal(2400, occupant.MinTicksInHive);
        Assert.Equal(frame, reEncoded);
    }

    // can_break / can_place_on - 766-769 only; see the codec's remarks for why 770+ stays untyped.

    /// <summary>The adventure-mode predicate on 766-769: a list of block predicates then a trailing <c>showInTooltip</c> BOOL. The predicate exercised here fills all three optional fields and both state-matcher branches, so a codec that skipped or mis-ordered any of them cannot pass.</summary>
    [Theory]
    [InlineData(766, 11, 10)]
    [InlineData(767, 11, 10)]
    [InlineData(768, 12, 11)]
    [InlineData(769, 12, 11)]
    public void AdventureModePredicates_DecodeRatherThanCostingThePacket(int protocol, int canBreakId, int canPlaceOnId)
    {
        foreach ((int wireId, DataComponentType<AdventureModePredicateComponent> key) in
            new (int, DataComponentType<AdventureModePredicateComponent>)[]
            {
                (canBreakId, DataComponents.CanBreak),
                (canPlaceOnId, DataComponents.CanPlaceOn),
            })
        {
            byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
            {
                w.WriteVarInt(1);       // one block predicate

                w.WriteBool(true);      // blocks present
                w.WriteVarInt(3);       // holder set: two direct ids
                w.WriteVarInt(41);
                w.WriteVarInt(42);

                w.WriteBool(true);      // state properties present
                w.WriteVarInt(2);
                w.WriteString("facing");
                w.WriteBool(true);      // exact matcher
                w.WriteString("north");
                w.WriteString("age");
                w.WriteBool(false);     // ranged matcher
                w.WriteBool(true);
                w.WriteString("2");
                w.WriteBool(false);     // no maximum

                w.WriteBool(true);      // nbt predicate present
                w.WriteNbt(BeeTag(), NbtWireFormat.JavaUnnamedRoot);

                w.WriteBool(false);     // showInTooltip
            });

            AdventureModePredicateComponent predicate = Decode(protocol, frame, key, out byte[] reEncoded);

            BlockPredicateEntry entry = Assert.Single(predicate.Predicates);
            Assert.Equal([41, 42], entry.Blocks!.Ids);
            Assert.Null(entry.Blocks.Tag);
            Assert.Equal(
                [
                    new StatePropertyMatcher("facing", Exact: true, "north"),
                    new StatePropertyMatcher("age", Exact: false, null, "2"),
                ],
                entry.Properties!);
            Assert.NotNull(entry.Nbt);
            Assert.False(predicate.ShowInTooltip);
            Assert.Equal(frame, reEncoded);
        }
    }

    /// <summary><c>can_break</c> still costs the packet on 770-776, because 1.21.5 appended <c>DataComponentMatchers</c> to <c>BlockPredicate</c>, and its exact-match half is a list of <c>TypedDataComponent</c> whose recursive compact payloads can name unmodeled components and cannot be skipped (the predicate-dispatch half is self-delimiting NBT and could be; see the codec's remarks).</summary>
    [Theory]
    [InlineData(770, 12)]
    [InlineData(776, 15)]
    public void CanBreak_StillCostsThePacketOn1_21_5AndUp(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) => w.WriteVarInt(0));

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var ex = Assert.Throws<UnmodeledItemComponentException>(() => bound.DecodeFrame(frame));
        Assert.Equal(Identifier.Minecraft("can_break"), ex.ComponentId);
    }

    // consumable / death_protection - both carry the ConsumeEffect dispatch.

    /// <summary><c>minecraft:consumable</c> on every era that declares it. The consume-effect list here carries all five vanilla types in registration order, so the dispatch is covered end to end.</summary>
    [Theory]
    [InlineData(768, 22)]
    [InlineData(769, 22)]
    [InlineData(770, 21)]
    [InlineData(771, 21)]
    [InlineData(773, 21)]
    [InlineData(774, 24)]
    [InlineData(775, 24)]
    [InlineData(776, 24)]
    public void Consumable_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteFloat(1.6f);
            w.WriteVarInt(1);        // ItemUseAnimation id
            w.WriteVarInt(13);       // equip sound: registry id 12
            w.WriteBool(true);       // has consume particles
            WriteAllFiveConsumeEffects(ref w);
        });

        ConsumableComponent consumable = Decode(protocol, frame, DataComponents.Consumable, out byte[] reEncoded);

        Assert.Equal(1.6f, consumable.ConsumeSeconds);
        Assert.Equal(1, consumable.Animation);
        Assert.Equal(12, consumable.Sound.RegistryId);
        Assert.True(consumable.HasConsumeParticles);
        Assert.Equal([0, 1, 2, 3, 4], consumable.OnConsumeEffects.Select(static e => e.TypeId));
        Assert.Equal(frame, reEncoded);
    }

    /// <summary><c>minecraft:death_protection</c>: the same consume-effect list, alone.</summary>
    [Theory]
    [InlineData(768, 32)]
    [InlineData(769, 32)]
    [InlineData(770, 32)]
    [InlineData(773, 32)]
    [InlineData(774, 36)]
    [InlineData(776, 36)]
    public void DeathProtection_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) => WriteAllFiveConsumeEffects(ref w));

        DeathProtectionComponent protection = Decode(protocol, frame, DataComponents.DeathProtection, out byte[] reEncoded);

        Assert.Equal([0, 1, 2, 3, 4], protection.DeathEffects.Select(static e => e.TypeId));
        Assert.Equal(new Identifier("minecraft", "poison"), protection.DeathEffects[1].RemoveEffects!.Tag);
        Assert.Equal(5.5f, protection.DeathEffects[3].TeleportDiameter);
        Assert.Equal(frame, reEncoded);
    }

    // damage_resistant - a bare tag key through 774, a full holder set from 775.

    /// <summary>768-774: one damage-type tag identifier string and nothing else.</summary>
    [Theory]
    [InlineData(768, 25)]
    [InlineData(769, 25)]
    [InlineData(770, 24)]
    [InlineData(771, 24)]
    [InlineData(773, 24)]
    [InlineData(774, 27)]
    public void DamageResistant_TagKeyForm_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) => w.WriteString("minecraft:is_fire"));

        DamageResistantComponent resistant = Decode(protocol, frame, DataComponents.DamageResistant, out byte[] reEncoded);

        Assert.Equal(new Identifier("minecraft", "is_fire"), resistant.Types.Tag);
        Assert.Empty(resistant.Types.Ids);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>Protocols 775/776 widen the field to a damage-type holder set. Reusing the 774 codec here would read the holder set's leading marker VarInt as a string length.</summary>
    [Theory]
    [InlineData(775, 27)]
    [InlineData(776, 27)]
    public void DamageResistant_HolderSetForm_DecodesBothBranches(int protocol, int wireId)
    {
        byte[] tagged = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(0);
            w.WriteString("minecraft:is_fire");
        });

        DamageResistantComponent byTag = Decode(protocol, tagged, DataComponents.DamageResistant, out byte[] reEncodedTag);
        Assert.Equal(new Identifier("minecraft", "is_fire"), byTag.Types.Tag);
        Assert.Equal(tagged, reEncodedTag);

        byte[] direct = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(3);
            w.WriteVarInt(4);
            w.WriteVarInt(9);
        });

        DamageResistantComponent byIds = Decode(protocol, direct, DataComponents.DamageResistant, out byte[] reEncodedIds);
        Assert.Null(byIds.Types.Tag);
        Assert.Equal([4, 9], byIds.Types.Ids);
        Assert.Equal(direct, reEncodedIds);
    }

    // enchantable / repairable / use_cooldown - the small ones, unchanged 768-776.

    /// <summary><c>minecraft:enchantable</c>: one VarInt.</summary>
    [Theory]
    [InlineData(768, 27)]
    [InlineData(769, 27)]
    [InlineData(770, 27)]
    [InlineData(773, 27)]
    [InlineData(774, 31)]
    [InlineData(775, 31)]
    [InlineData(776, 31)]
    public void Enchantable_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) => w.WriteVarInt(22));

        EnchantableComponent enchantable = Decode(protocol, frame, DataComponents.Enchantable, out byte[] reEncoded);

        Assert.Equal(22, enchantable.Value);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary><c>minecraft:repairable</c>: one item holder set, both branches.</summary>
    [Theory]
    [InlineData(768, 29)]
    [InlineData(770, 29)]
    [InlineData(774, 33)]
    [InlineData(776, 33)]
    public void Repairable_DecodesBothHolderSetBranches(int protocol, int wireId)
    {
        byte[] tagged = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(0);
            w.WriteString("minecraft:planks");
        });

        RepairableComponent byTag = Decode(protocol, tagged, DataComponents.Repairable, out byte[] reEncodedTag);
        Assert.Equal(new Identifier("minecraft", "planks"), byTag.Items.Tag);
        Assert.Equal(tagged, reEncodedTag);

        byte[] direct = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(2);
            w.WriteVarInt(770);
        });

        RepairableComponent byIds = Decode(protocol, direct, DataComponents.Repairable, out byte[] reEncodedIds);
        Assert.Equal([770], byIds.Items.Ids);
        Assert.Equal(direct, reEncodedIds);
    }

    /// <summary><c>minecraft:use_cooldown</c>: a FLOAT then an optional identifier, both branches.</summary>
    [Theory]
    [InlineData(768, 24)]
    [InlineData(769, 24)]
    [InlineData(770, 23)]
    [InlineData(773, 23)]
    [InlineData(774, 26)]
    [InlineData(776, 26)]
    public void UseCooldown_DecodesWithAndWithoutAGroup(int protocol, int wireId)
    {
        byte[] withGroup = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteFloat(3.25f);
            w.WriteBool(true);
            w.WriteString("minecraft:goat_horn");
        });

        UseCooldownComponent grouped = Decode(protocol, withGroup, DataComponents.UseCooldown, out byte[] reEncodedGrouped);
        Assert.Equal(3.25f, grouped.Seconds);
        Assert.Equal(new Identifier("minecraft", "goat_horn"), grouped.CooldownGroup);
        Assert.Equal(withGroup, reEncodedGrouped);

        byte[] bare = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteFloat(0.5f);
            w.WriteBool(false);
        });

        UseCooldownComponent ungrouped = Decode(protocol, bare, DataComponents.UseCooldown, out byte[] reEncodedBare);
        Assert.Null(ungrouped.CooldownGroup);
        Assert.Equal(bare, reEncodedBare);
    }

    // equippable - three era shapes, each one a strictly longer record than the last.

    /// <summary>768/769: no <c>equipOnInteract</c>, no shearing pair.</summary>
    [Theory]
    [InlineData(768, 28)]
    [InlineData(769, 28)]
    public void Equippable_V1_21_2Shape_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) => WriteEquippableCore(ref w));

        EquippableComponent equippable = Decode(protocol, frame, DataComponents.Equippable, out byte[] reEncoded);

        AssertEquippableCore(equippable);
        Assert.False(equippable.EquipOnInteract);
        Assert.False(equippable.CanBeSheared);
        Assert.Null(equippable.ShearingSound);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>770: 1.21.5 appended <c>equipOnInteract</c>.</summary>
    [Theory]
    [InlineData(770, 28)]
    public void Equippable_V1_21_5Shape_CarriesEquipOnInteract(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            WriteEquippableCore(ref w);
            w.WriteBool(true);
        });

        EquippableComponent equippable = Decode(protocol, frame, DataComponents.Equippable, out byte[] reEncoded);

        AssertEquippableCore(equippable);
        Assert.True(equippable.EquipOnInteract);
        Assert.False(equippable.CanBeSheared);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>771+: 1.21.6 appended <c>canBeSheared</c> and a <c>shearingSound</c> holder.</summary>
    [Theory]
    [InlineData(771, 28)]
    [InlineData(773, 28)]
    [InlineData(774, 32)]
    [InlineData(775, 32)]
    [InlineData(776, 32)]
    public void Equippable_V1_21_6Shape_CarriesTheShearingPair(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            WriteEquippableCore(ref w);
            w.WriteBool(true);   // equipOnInteract
            w.WriteBool(true);   // canBeSheared
            w.WriteVarInt(0);    // shearing sound: inline
            w.WriteString("minecraft:entity.sheep.shear");
            w.WriteBool(false);  // no fixed range
        });

        EquippableComponent equippable = Decode(protocol, frame, DataComponents.Equippable, out byte[] reEncoded);

        AssertEquippableCore(equippable);
        Assert.True(equippable.CanBeSheared);
        Assert.Equal(new Identifier("minecraft", "entity.sheep.shear"), equippable.ShearingSound!.SoundId);
        Assert.Null(equippable.ShearingSound.FixedRange);
        Assert.Equal(frame, reEncoded);
    }

    // instrument - four shapes: the 1.21.2 record change, the 1.21.5 EitherHolder, the 26.1 unwrap.

    /// <summary>766/767: the inline instrument's use duration is a VarInt tick count and there is no description.</summary>
    [Theory]
    [InlineData(766, 40)]
    [InlineData(767, 40)]
    public void Instrument_V1_20_5Shape_UsesAVarIntDurationAndNoDescription(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(0);   // inline holder
            w.WriteVarInt(31);  // sound: registry id 30
            w.WriteVarInt(140); // useDuration, in ticks
            w.WriteFloat(256.0f);
        });

        InstrumentComponent instrument = Decode(protocol, frame, DataComponents.Instrument, out byte[] reEncoded);

        Assert.Equal(140, instrument.Direct!.UseDurationTicks);
        Assert.Null(instrument.Direct.UseDurationSeconds);
        Assert.Null(instrument.Direct.Description);
        Assert.Equal(30, instrument.Direct.Sound.RegistryId);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>768/769: 1.21.2 made the duration a FLOAT and appended a description component.</summary>
    [Theory]
    [InlineData(768, 50)]
    [InlineData(769, 50)]
    public void Instrument_V1_21_2Shape_UsesAFloatDurationAndADescription(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(0);
            w.WriteVarInt(31);
            w.WriteFloat(7.0f);
            w.WriteFloat(256.0f);
            w.WriteComponent(Component.Text("Ponder"), ComponentWireEra.Legacy, NbtWireFormat.JavaUnnamedRoot);
        });

        InstrumentComponent instrument = Decode(protocol, frame, DataComponents.Instrument, out byte[] reEncoded);

        Assert.Null(instrument.Direct!.UseDurationTicks);
        Assert.Equal(7.0f, instrument.Direct.UseDurationSeconds);
        Assert.NotNull(instrument.Direct.Description);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>770-774: a leading BOOL chooses between the instrument holder and a bare registry-key identifier. Both branches are checked.</summary>
    [Theory]
    [InlineData(770, 52)]
    [InlineData(771, 52)]
    [InlineData(773, 52)]
    [InlineData(774, 59)]
    public void Instrument_V1_21_5Shape_DecodesBothEitherBranches(int protocol, int wireId)
    {
        byte[] holder = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteBool(true);   // either: left, a holder
            w.WriteVarInt(4);    // registry id 3
        });

        InstrumentComponent byHolder = Decode(protocol, holder, DataComponents.Instrument, out byte[] reEncodedHolder);
        Assert.Equal(3, byHolder.HolderId);
        Assert.Null(byHolder.ReferenceKey);
        Assert.Equal(holder, reEncodedHolder);

        byte[] key = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteBool(false);  // either: right, a registry key
            w.WriteString("minecraft:ponder_goat_horn");
        });

        InstrumentComponent byKey = Decode(protocol, key, DataComponents.Instrument, out byte[] reEncodedKey);
        Assert.Equal(new Identifier("minecraft", "ponder_goat_horn"), byKey.ReferenceKey);
        Assert.Null(byKey.HolderId);
        Assert.Equal(key, reEncodedKey);
    }

    /// <summary>775/776: 26.1 dropped the <c>EitherHolder</c>, so the payload is a plain holder again.</summary>
    [Theory]
    [InlineData(775, 61)]
    [InlineData(776, 61)]
    public void Instrument_V26_1Shape_HasNoEitherWrapper(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) => w.WriteVarInt(4));

        InstrumentComponent instrument = Decode(protocol, frame, DataComponents.Instrument, out byte[] reEncoded);

        Assert.Equal(3, instrument.HolderId);
        Assert.Equal(frame, reEncoded);
    }

    // jukebox_playable - the trailing tooltip flag went at 1.21.5, the EitherHolder at 26.1.

    /// <summary>767-769: <c>EitherHolder</c> then a trailing <c>showInTooltip</c> BOOL.</summary>
    [Theory]
    [InlineData(767, 42)]
    [InlineData(768, 52)]
    [InlineData(769, 52)]
    public void JukeboxPlayable_V1_21Shape_CarriesTheTrailingTooltipFlag(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteBool(false);  // either: right, a registry key
            w.WriteString("minecraft:pigstep");
            w.WriteBool(false);  // showInTooltip
        });

        JukeboxPlayableComponent playable = Decode(protocol, frame, DataComponents.JukeboxPlayable, out byte[] reEncoded);

        Assert.Equal(new Identifier("minecraft", "pigstep"), playable.ReferenceKey);
        Assert.False(playable.ShowInTooltip);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>770-774: the tooltip flag is gone but the <c>EitherHolder</c> stays. The inline song branch is covered here, because it is the only place the inline jukebox-song field order (sound, description, length, comparator output) is observable.</summary>
    [Theory]
    [InlineData(770, 55)]
    [InlineData(773, 55)]
    [InlineData(774, 62)]
    public void JukeboxPlayable_V1_21_5Shape_DecodesTheInlineSong(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteBool(true);   // either: left, a holder
            w.WriteVarInt(0);    // holder: inline
            w.WriteVarInt(0);    // sound: inline
            w.WriteString("minecraft:music_disc.pigstep");
            w.WriteBool(true);
            w.WriteFloat(64.0f);
            w.WriteComponent(Component.Text("Lena Raine - Pigstep"), ComponentWireEra.Modern, NbtWireFormat.JavaUnnamedRoot);
            w.WriteFloat(149.0f);
            w.WriteVarInt(13);
        });

        JukeboxPlayableComponent playable = Decode(protocol, frame, DataComponents.JukeboxPlayable, out byte[] reEncoded);

        Assert.Equal(new Identifier("minecraft", "music_disc.pigstep"), playable.Direct!.Sound.SoundId);
        Assert.Equal(64.0f, playable.Direct.Sound.FixedRange);
        Assert.Equal(149.0f, playable.Direct.LengthInSeconds);
        Assert.Equal(13, playable.Direct.ComparatorOutput);
        Assert.Equal(frame, reEncoded);
    }

    /// <summary>775/776: 26.1 dropped the <c>EitherHolder</c> here too.</summary>
    [Theory]
    [InlineData(775, 64)]
    [InlineData(776, 64)]
    public void JukeboxPlayable_V26_1Shape_HasNoEitherWrapper(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) => w.WriteVarInt(8));

        JukeboxPlayableComponent playable = Decode(protocol, frame, DataComponents.JukeboxPlayable, out byte[] reEncoded);

        Assert.Equal(7, playable.HolderId);
        Assert.Equal(frame, reEncoded);
    }

    // lodestone_tracker / suspicious_stew_effects - identical on all ten eras.

    /// <summary>(protocol, <c>minecraft:lodestone_tracker</c> wire id) for every component era.</summary>
    public static TheoryData<int, int> LodestoneWireIds => new()
    {
        { 766, 43 }, { 767, 44 }, { 768, 54 }, { 769, 54 },
        { 770, 58 }, { 771, 58 }, { 773, 58 }, { 774, 65 }, { 775, 67 }, { 776, 67 },
    };

    /// <summary><c>optional(GlobalPos) + BOOL</c>, where <c>GlobalPos</c> is a dimension identifier and a packed 1.14+ <c>BlockPos</c> long. Both the tracked and the untracked shapes are checked, because the optional's leading BOOL is the only thing separating them.</summary>
    [Theory]
    [MemberData(nameof(LodestoneWireIds))]
    public void LodestoneTracker_DecodesWithAndWithoutATarget(int protocol, int wireId)
    {
        byte[] tracked = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteBool(true);
            w.WriteString("minecraft:the_nether");
            w.WriteBlockPos(new BlockPos(-142, 71, 903), BlockPosLayout.Packed114);
            w.WriteBool(true);
        });

        LodestoneTrackerComponent withTarget = Decode(protocol, tracked, DataComponents.LodestoneTracker, out byte[] reEncodedTracked);
        Assert.Equal(new Identifier("minecraft", "the_nether"), withTarget.Dimension);
        Assert.Equal(new BlockPos(-142, 71, 903), withTarget.Position);
        Assert.True(withTarget.Tracked);
        Assert.Equal(tracked, reEncodedTracked);

        byte[] untracked = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteBool(false);
            w.WriteBool(false);
        });

        LodestoneTrackerComponent withoutTarget = Decode(protocol, untracked, DataComponents.LodestoneTracker, out byte[] reEncodedUntracked);
        Assert.Null(withoutTarget.Position);
        Assert.False(withoutTarget.Tracked);
        Assert.Equal(untracked, reEncodedUntracked);
    }

    /// <summary>(protocol, <c>minecraft:suspicious_stew_effects</c> wire id) for every component era.</summary>
    public static TheoryData<int, int> StewWireIds => new()
    {
        { 766, 32 }, { 767, 32 }, { 768, 42 }, { 769, 42 },
        { 770, 44 }, { 771, 44 }, { 773, 44 }, { 774, 51 }, { 775, 53 }, { 776, 53 },
    };

    /// <summary>A list of (mob-effect holder id, VarInt duration) and nothing else.</summary>
    [Theory]
    [MemberData(nameof(StewWireIds))]
    public void SuspiciousStewEffects_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(protocol, wireId, static (ref PacketWriter w) =>
        {
            w.WriteVarInt(2);
            w.WriteVarInt(15);
            w.WriteVarInt(160);
            w.WriteVarInt(19);
            w.WriteVarInt(140);
        });

        SuspiciousStewEffectsComponent stew = Decode(protocol, frame, DataComponents.SuspiciousStewEffects, out byte[] reEncoded);

        Assert.Equal(
            [
                new MobEffectDetail(new Identifier("umpk", "effect_15"), 0, 160),
                new MobEffectDetail(new Identifier("umpk", "effect_19"), 0, 140),
            ],
            stew.Effects);
        Assert.Equal(frame, reEncoded);
    }

    // Helpers.

    private delegate void PayloadWriter(ref PacketWriter writer);

    private static NbtCompound BeeTag()
    {
        var tag = new NbtCompound();
        tag.PutString("CustomName", "Buzz");
        tag.PutInt("Age", 3);
        return tag;
    }

    /// <summary>All five consume-effect variants, in registration (= id) order, so a codec that got the dispatch order wrong or mis-sized any branch desynchronizes and fails the frame-exact assertion.</summary>
    private static void WriteAllFiveConsumeEffects(ref PacketWriter w)
    {
        w.WriteVarInt(5);

        w.WriteVarInt(0);       // apply_effects
        w.WriteVarInt(1);       // one MobEffectInstance
        w.WriteVarInt(10);      // effect holder id
        w.WriteVarInt(1);       // amplifier
        w.WriteVarInt(200);     // duration
        w.WriteBool(false);     // ambient
        w.WriteBool(true);      // show particles
        w.WriteBool(true);      // show icon
        w.WriteBool(false);     // no hidden effect
        w.WriteFloat(0.75f);    // probability

        w.WriteVarInt(1);       // remove_effects
        w.WriteVarInt(0);       // holder set: a named tag
        w.WriteString("minecraft:poison");

        w.WriteVarInt(2);       // clear_all_effects, zero payload bytes

        w.WriteVarInt(3);       // teleport_randomly
        w.WriteFloat(5.5f);

        w.WriteVarInt(4);       // play_sound
        w.WriteVarInt(18);      // sound: registry id 17
    }

    private static void WriteEquippableCore(ref PacketWriter w)
    {
        w.WriteVarInt(5);       // equipment slot id
        w.WriteVarInt(13);      // equip sound: registry id 12
        w.WriteBool(true);      // asset id present
        w.WriteString("minecraft:iron");
        w.WriteBool(false);     // no camera overlay
        w.WriteBool(true);      // allowed entities present
        w.WriteVarInt(0);       // holder set: a named tag
        w.WriteString("minecraft:skeletons");
        w.WriteBool(true);      // dispensable
        w.WriteBool(false);     // swappable
        w.WriteBool(true);      // damage on hurt
    }

    private static void AssertEquippableCore(EquippableComponent equippable)
    {
        Assert.Equal(5, equippable.Slot);
        Assert.Equal(12, equippable.EquipSound.RegistryId);
        Assert.Equal(new Identifier("minecraft", "iron"), equippable.AssetId);
        Assert.Null(equippable.CameraOverlay);
        Assert.Equal(new Identifier("minecraft", "skeletons"), equippable.AllowedEntities!.Tag);
        Assert.True(equippable.Dispensable);
        Assert.False(equippable.Swappable);
        Assert.True(equippable.DamageOnHurt);
    }

    /// <summary>Pushes the frame through the codec the registrar actually binds for that protocol, pulls the component out of the decoded stack, and hands back the re-encoded bytes for the frame-exact assertion each caller makes.</summary>
    private static T Decode<T>(int protocol, byte[] frame, DataComponentType<T> key, out byte[] reEncoded)
        where T : class
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));
        Assert.True(packet.Item.Components.TryGet(key, out T? value));
        reEncoded = bound.Encode(packet);
        return value!;
    }

    /// <summary>Composes a container_set_slot frame at the field level, carrying exactly one component. 766/767 write a signed-byte container id and 768+ a VarInt; that split is the container-id width and is unrelated to the component-table split, which is why the threshold here is 768 (the same helper <c>PotDecorationsComponentTests</c> uses).</summary>
    private static byte[] SetSlotFrame(int protocol, int componentWireId, PayloadWriter write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        if (protocol < 768)
            w.WriteByte(0);

        else
            w.WriteVarInt(0);

        w.WriteVarInt(1);            // state id
        w.WriteShort(3);             // slot
        w.WriteVarInt(1);            // count
        w.WriteVarInt(ItemTestRegistries.Stone);
        w.WriteVarInt(1);            // components added
        w.WriteVarInt(0);            // components removed
        w.WriteVarInt(componentWireId);
        write(ref w);

        return buffer.WrittenSpan.ToArray();
    }
}
