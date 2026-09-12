using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Round-trip tests for the new pre-flattening (1.9-1.13.2) gameplay codecs: the split-spawn family (add_mob / add_player / add_experience_orb / add_global_entity), the serverbound dig / interact / block-place / use-item / container-click / creative sends, and the era item Slot flip at 1.13.2. The clientbound reuses (block_update, entity move/motion/effect, chat, health/xp, spawn, held-slot) are pinned byte-exact by the corpus conformance suite over the 107/109/315/335/340/393/401/404 fixtures; these cover the code-built paths the conformance corpora do not (serverbound + spawns).</summary>
public class GameplayWireShapeTests
{
    private static EntityMetadataList EmptyMeta => new([], []);

    [Fact]
    public void AddMob_Pre114_RoundTripsUuidAndFields()
    {
        var mob = new ClientboundAddMobPacket(
            42, TypeId: 92, X: 10.5, Y: 64.0, Z: -7.25, Yaw: 45f, Pitch: -20f, HeadPitch: 10f,
            VelocityX: 1, VelocityY: -2, VelocityZ: 3, Metadata: EmptyMeta, Uuid: Guid.NewGuid());

        foreach (PacketCodec<ClientboundAddMobPacket> codec in new[]
                 { EntitySpawnCodecs.AddMobV1_9, EntitySpawnCodecs.AddMobV1_12, EntitySpawnCodecs.AddMobV1_13 })
        {
            ClientboundAddMobPacket d = CodecRoundTrip.Cycle(codec, mob);
            Assert.Equal(mob.EntityId, d.EntityId);
            Assert.Equal(mob.TypeId, d.TypeId);
            Assert.Equal(mob.X, d.X);
            Assert.Equal(mob.Z, d.Z);
            Assert.Equal(mob.Uuid, d.Uuid);
            Assert.Equal(mob.VelocityX, d.VelocityX);
        }
    }

    [Fact]
    public void AddPlayer_Pre114_RoundTripsWithoutHeldItem()
    {
        var player = new ClientboundAddPlayerPacket(
            7, Guid.NewGuid(), X: 1.0, Y: 2.0, Z: 3.0, Yaw: 30f, Pitch: -15f, CurrentItem: 0, Metadata: EmptyMeta);
        ClientboundAddPlayerPacket d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddPlayerV1_9, player);
        Assert.Equal(player.EntityId, d.EntityId);
        Assert.Equal(player.Uuid, d.Uuid);
        Assert.Equal(player.Y, d.Y);
    }

    [Fact]
    public void AddExperienceOrb_And_GlobalEntity_Pre114_RoundTrip()
    {
        var orb = new ClientboundAddExperienceOrbPacket(3, 12.5, 65.0, -4.0, 17);
        Assert.Equal(orb, CodecRoundTrip.Cycle(EntitySpawnCodecs.AddExperienceOrbV1_9, orb));

        var global = new ClientboundAddGlobalEntityPacket(9, 1, 20.0, 70.0, 30.0);
        Assert.Equal(global, CodecRoundTrip.Cycle(EntitySpawnCodecs.AddGlobalEntityV1_9, global));
    }

    /// <summary>The 1.8 and 1.9 global-entity (lightning) spawns have different lengths. A literal 1.9 frame (VarInt id, byte type, three doubles = 26 bytes) must decode exactly under the 1.9 codec and must NOT be accepted by the 1.8 one, which reads three fixed-point ints and would leave 12 bytes unread.</summary>
    [Fact]
    public void AddGlobalEntity_V1_9_Frame_IsRejectedByTheV1_8Codec()
    {
        byte[] frame =
        [
            0x09,                                                       // VarInt entity id 9
            0x01,                                                       // byte type 1 (thunderbolt)
            0x40, 0x34, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // x = 20.0
            0x40, 0x51, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00,             // y = 70.0
            0x40, 0x3E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // z = 30.0
        ];

        ClientboundAddGlobalEntityPacket decoded = CodecRoundTrip.Decode(EntitySpawnCodecs.AddGlobalEntityV1_9, frame);
        Assert.Equal(9, decoded.EntityId);
        Assert.Equal(1, decoded.GlobalType);
        Assert.Equal(20.0, decoded.X);
        Assert.Equal(70.0, decoded.Y);
        Assert.Equal(30.0, decoded.Z);
        Assert.Equal(frame, CodecRoundTrip.Encode(EntitySpawnCodecs.AddGlobalEntityV1_9, decoded));

        var reader = new PacketReader(frame);
        EntitySpawnCodecs.AddGlobalEntityV1_8.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(12, reader.Remaining);
    }

    [Fact]
    public void PlayerAction_Pre114_VarIntStatusNoSequence()
    {
        var dig = new ServerboundPlayerActionPacket(0, new BlockPos(100, 64, -200), Direction: 1, Sequence: null);
        ServerboundPlayerActionPacket d = CodecRoundTrip.Cycle(EntityServerboundCodecs.PlayerActionV1_9, dig);
        Assert.Equal(dig.Action, d.Action);
        Assert.Equal(dig.Position, d.Position);
        Assert.Equal(dig.Direction, d.Direction);
        Assert.Null(d.Sequence);
    }

    [Fact]
    public void Interact_Pre114_HasHandNoSecondaryAction()
    {
        var attack = new ServerboundInteractPacket(11, Action: 1, Hand: null, InteractAt: null, UsingSecondaryAction: null);
        Assert.Equal(attack, CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_9, attack));

        var interact = new ServerboundInteractPacket(11, Action: 0, Hand: 0, InteractAt: null, UsingSecondaryAction: null);
        Assert.Equal(interact, CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_9, interact));

        var interactAt = new ServerboundInteractPacket(11, Action: 2, Hand: 1, InteractAt: new Vec3d(0.5, 0.25, 0.75), UsingSecondaryAction: null);
        ServerboundInteractPacket d = CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_9, interactAt);
        Assert.Equal(interactAt.Hand, d.Hand);
        Assert.Equal(interactAt.InteractAt, d.InteractAt);
    }

    /// <summary>Both pre-1.14 cursor forms, in the ONE unit system the packet record documents: 0..1.</summary>
    /// <remarks>The packet record exposes cursor values in the 0..1 domain. Protocols 1.9-1.10 store each value in sixteenths as an unsigned byte, so the codec must scale values in both directions.</remarks>
    [Fact]
    public void UseItemOn_Pre114_ByteAndFloatCursorForms()
    {
        var place = new ServerboundUseItemOnPacket(Hand: 0, new BlockPos(5, 60, 5), Face: 1, CursorX: 0.5f, CursorY: 0.9375f, CursorZ: 0f, Inside: false, WorldBorderHit: false, Sequence: 0);
        ServerboundUseItemOnPacket i8 = CodecRoundTrip.Cycle(UseItemCodecs.UseItemOnV1_9, place);
        Assert.Equal(place.Position, i8.Position);
        Assert.Equal(place.Face, i8.Face);
        Assert.Equal(place.Hand, i8.Hand);
        // The wire granularity is a sixteenth, and both inputs sit exactly on it.
        Assert.Equal(0.5f, i8.CursorX);
        Assert.Equal(0.9375f, i8.CursorY);
        Assert.Equal(0f, i8.CursorZ);

        var placeF = new ServerboundUseItemOnPacket(Hand: 1, new BlockPos(-3, 12, 200), Face: 4, CursorX: 0.5f, CursorY: 0.25f, CursorZ: 0.9f, Inside: false, WorldBorderHit: false, Sequence: 0);
        ServerboundUseItemOnPacket f = CodecRoundTrip.Cycle(UseItemCodecs.UseItemOnV1_11, placeF);
        Assert.Equal(placeF.Position, f.Position);
        Assert.Equal(0.5f, f.CursorX);
        Assert.Equal(placeF.Hand, f.Hand);
    }

    /// <summary>The 1.9-1.10 cursor bytes are sixteenths, pinned against the exact wire byte rather than against a round trip, because encode and decode through the same wrong scale can agree.</summary>
    /// <remarks>Each cursor axis is encoded as <c>(int)(facing * 16.0F)</c> and decoded as the unsigned byte divided by 16. The 1.9-1.10 form keeps those three values after the position, direction VarInt, and hand VarInt.</remarks>
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.0625f, 1)]      // exactly one sixteenth
    [InlineData(0.5f, 8)]         // the centre of a face: the value a client sends most
    [InlineData(0.9375f, 15)]
    [InlineData(1f, 16)]          // an exact far-face hit; vanilla does not clamp this and neither do we
    public void UseItemOn_V1_9_CarriesTheCursorInSixteenths(float cursor, byte expectedByte)
    {
        var place = new ServerboundUseItemOnPacket(
            Hand: 0, new BlockPos(1, 2, 3), Face: 1, CursorX: cursor, CursorY: cursor, CursorZ: cursor,
            Inside: false, WorldBorderHit: false, Sequence: 0);

        byte[] frame = CodecRoundTrip.Encode(UseItemCodecs.UseItemOnV1_9, place);

        // pos(8) + face VarInt(1) + hand VarInt(1) + three cursor bytes = 13.
        Assert.Equal(13, frame.Length);
        Assert.Equal(expectedByte, frame[10]);
        Assert.Equal(expectedByte, frame[11]);
        Assert.Equal(expectedByte, frame[12]);

        // And the wire byte decodes back to the fraction it stands for.
        ServerboundUseItemOnPacket decoded = CodecRoundTrip.Decode(UseItemCodecs.UseItemOnV1_9, frame);
        Assert.Equal(expectedByte / 16f, decoded.CursorX);
        Assert.Equal(expectedByte / 16f, decoded.CursorY);
        Assert.Equal(expectedByte / 16f, decoded.CursorZ);
    }

    /// <summary>The 1.11-1.13.2 band carries the same 0..1 cursor as raw f32, so the identical placement produces the identical FRACTION on both sides of the 1.11 wire change. The same call must therefore target a slab's top half on both sides of the boundary.</summary>
    [Fact]
    public void UseItemOn_TheSameCursorMeansTheSameThingOnBothPre114Forms()
    {
        var place = new ServerboundUseItemOnPacket(
            Hand: 0, new BlockPos(1, 2, 3), Face: 1, CursorX: 0.5f, CursorY: 0.75f, CursorZ: 0.25f,
            Inside: false, WorldBorderHit: false, Sequence: 0);

        ServerboundUseItemOnPacket onByteBand = CodecRoundTrip.Cycle(UseItemCodecs.UseItemOnV1_9, place);
        ServerboundUseItemOnPacket onFloatBand = CodecRoundTrip.Cycle(UseItemCodecs.UseItemOnV1_11, place);

        Assert.Equal(onFloatBand.CursorX, onByteBand.CursorX);
        Assert.Equal(onFloatBand.CursorY, onByteBand.CursorY);
        Assert.Equal(onFloatBand.CursorZ, onByteBand.CursorZ);
    }

    [Fact]
    public void UseItem_HandOnlyBand_HandOnly()
    {
        var use = new ServerboundUseItemPacket(Hand: 1, Sequence: 0, YRot: 0f, XRot: 0f);
        ServerboundUseItemPacket d = CodecRoundTrip.Cycle(UseItemCodecs.UseItemV1_9, use);
        Assert.Equal(1, d.Hand);
    }

    [Fact]
    public void ContainerClick_And_Creative_Pre114_ShortIdAndPresentSlotForms()
    {
        var emptyClick = new ServerboundContainerClickPacket(1, 0, Slot: 5, Button: 0, Mode: 0, ActionNumber: 3, LegacyClickedItem: ItemStack.Empty, ChangedSlots: [], CarriedItem: null);
        foreach (PacketCodec<ServerboundContainerClickPacket> codec in new[] { ContainerCodecs.ContainerClickV1_13, ContainerCodecs.ContainerClickV1_13_2 })
        {
            ServerboundContainerClickPacket d = CodecRoundTrip.Cycle(codec, emptyClick);
            Assert.Equal(emptyClick.ContainerId, d.ContainerId);
            Assert.Equal(emptyClick.Slot, d.Slot);
            Assert.Equal(emptyClick.ActionNumber, d.ActionNumber);
        }

        var emptyCreative = new ServerboundSetCreativeModeSlotPacket(Slot: 36, Item: ItemStack.Empty);
        foreach (PacketCodec<ServerboundSetCreativeModeSlotPacket> codec in new[] { ContainerCodecs.CreativeSlotV1_13, ContainerCodecs.CreativeSlotV1_13_2 })
        {
            ServerboundSetCreativeModeSlotPacket d = CodecRoundTrip.Cycle(codec, emptyCreative);
            Assert.Equal(emptyCreative.Slot, d.Slot);
            Assert.True(d.Item.IsEmpty);
        }
    }

    /// <summary>The 1.9-1.12.2 band carries the 1.8 slot (id, count, DAMAGE, NBT), not the 1.13 short-id slot: the flattening is what drops the damage short. Only a non-empty stack separates the two forms, so this pins the byte layout with one. The empty-stack cycles above cannot see the difference because both forms encode the id short as -1.</summary>
    [Fact]
    public void Pre113_ContainerSetSlot_And_Creative_CarryTheDamageShort()
    {
        // Red wool, count 1, damage 14: (35 << 16) | 14 is a registered composite in the test registry.
        ItemStack wool = new(
            ItemTestRegistries.LegacyItem(ItemTestRegistries.LegacyBlockItemId, ItemTestRegistries.LegacyBlockSubtype), 1);

        var slot = new ClientboundContainerSetSlotPacket(ContainerId: 0, StateId: 0, Slot: 36, Item: wool);
        byte[] slotBytes = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_9, slot);
        // window 0, slot 0x0024, id 0x0023 (35), count 1, damage 0x000E (14), TAG_End.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x24, 0x00, 0x23, 0x01, 0x00, 0x0E, 0x00 }, slotBytes);
        ClientboundContainerSetSlotPacket decodedSlot = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetSlotV1_9, slot);
        Assert.Equal(wool.Item.NetworkId, decodedSlot.Item.Item.NetworkId);
        Assert.False(decodedSlot.IsLegacy, "1.9+ decodes under the container identity, not the 1.8 one.");

        var creative = new ServerboundSetCreativeModeSlotPacket(Slot: 36, Item: wool);
        byte[] creativeBytes = ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlotV1_9, creative);
        Assert.Equal(new byte[] { 0x00, 0x24, 0x00, 0x23, 0x01, 0x00, 0x0E, 0x00 }, creativeBytes);
        ServerboundSetCreativeModeSlotPacket decodedCreative = ItemCodecRoundTrip.Cycle(ContainerCodecs.CreativeSlotV1_9, creative);
        Assert.Equal(wool.Item.NetworkId, decodedCreative.Item.Item.NetworkId);
        Assert.False(decodedCreative.IsLegacy, "1.9+ decodes under the container identity, not the 1.8 one.");
    }
}
