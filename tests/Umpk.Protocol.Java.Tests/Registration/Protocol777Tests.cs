using Umpk;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>26.3 (protocol 777) wire boundary. Vanilla 26.3 (server jar SHA-1 <c>33680f5f2ac32864d6d7cf5e56a705fdb3e05f4c</c>, <c>version.json</c> <c>protocol_version</c> 777) adds four packets, removes one, and appends one entity-metadata serializer: <c>minecraft:post_effects</c> (configuration clientbound 10, play clientbound 83; <c>ClientboundPostEffectsPacket(List&lt;Identifier&gt;)</c>), <c>minecraft:add_transient_block</c> (play clientbound 37; BlockPos plus block-state VarInt), <c>minecraft:swing_animation</c> (play clientbound 123; VarInt entity id, hand, animation), <c>minecraft:punch</c> (play serverbound 46; empty body, replacing <c>minecraft:swing</c>), and <c>DYE_COLOR</c> (<c>EntityDataSerializer.forValueType(DyeColor.STREAM_CODEC)</c>, a VarInt dye id, registered last in <c>EntityDataSerializers</c>).</summary>
public sealed class Protocol777Tests
{
    private const int P777 = 777;

    private static BoundPacketCodec PlayEntry(string identifier) =>
        BoundCodec.EntryAt(P777, PacketFlow.Clientbound, identifier);

    [Fact]
    public void PostEffects_Play_IsImplementedAt777()
    {
        Assert.True(PlayEntry("post_effects").IsImplemented, "minecraft:post_effects is a marker at protocol 777 (play).");
    }

    [Fact]
    public void PostEffects_Configuration_IsImplementedAt777()
    {
        ConfigBound(P777, PacketFlow.Clientbound, "post_effects");
    }

    [Fact]
    public void AddTransientBlock_IsImplementedAt777()
    {
        Assert.True(PlayEntry("add_transient_block").IsImplemented, "minecraft:add_transient_block is a marker at protocol 777.");
    }

    [Fact]
    public void SwingAnimation_IsImplementedAt777()
    {
        Assert.True(PlayEntry("swing_animation").IsImplemented, "minecraft:swing_animation is a marker at protocol 777.");
    }

    [Fact]
    public void Punch_IsImplementedAt777()
    {
        BoundPacketCodec bound = BoundCodec.EntryAt(P777, PacketFlow.Serverbound, "punch");
        Assert.True(bound.IsImplemented, "minecraft:punch is a marker at protocol 777.");
    }

    [Fact]
    public void SetEntityData_DyeColorSerializer_DecodesVarIntAt777()
    {
        // Serializer id 43 is DYE_COLOR on 777 (a VarInt dye id); on 776 the table ends at 42, so this entry raw-tails there.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(1);   // entity id
        w.WriteByte(0);     // index 0
        w.WriteVarInt(43);  // serializer DYE_COLOR on 777
        w.WriteVarInt(3);   // light_blue
        w.WriteByte(0xFF);  // terminator
        byte[] frame = buffer.WrittenSpan.ToArray();

        var packet = Assert.IsType<ClientboundSetEntityDataPacket>(BoundCodec.At(P777, PacketFlow.Clientbound, "set_entity_data").DecodeFrame(frame));
        Assert.Single(packet.Metadata.Entries);
        Assert.Equal(3, packet.Metadata.Entries[0].Value.AsVarInt());
    }

    [Fact]
    public void PostEffects_PlayAndConfiguration_ShareOneByteExactBody()
    {
        // Identifier.STREAM_CODEC: VarInt length + UTF-8. "minecraft:night_vision" is 22 bytes.
        byte[] expected = [0x01, 0x16, .. "minecraft:night_vision"u8.ToArray()];
        var packet = new ClientboundPostEffectsPacket([Identifier.Minecraft("night_vision")]);

        byte[] play = BoundCodec.At(P777, PacketFlow.Clientbound, "post_effects").Encode(packet);
        Assert.Equal(expected, play);
        var decoded = Assert.IsType<ClientboundPostEffectsPacket>(
            BoundCodec.At(P777, PacketFlow.Clientbound, "post_effects").DecodeFrame(play));
        Assert.Equal(packet.Effects, decoded.Effects);

        var config = new ClientboundConfigPostEffectsPacket([Identifier.Minecraft("night_vision")]);
        byte[] configBytes = ConfigBound(P777, PacketFlow.Clientbound, "post_effects").Encode(config);
        Assert.Equal(expected, configBytes);
    }

    [Fact]
    public void Punch_IsAnEmptyBody()
    {
        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Serverbound, "punch");
        Assert.Empty(bound.Encode(new ServerboundPunchPacket()));
        Assert.IsType<ServerboundPunchPacket>(bound.DecodeFrame([]));
    }

    [Fact]
    public void SwingAnimation_RoundTripsVarInts()
    {
        // entity 300, off hand, stab, 10 ticks.
        byte[] expected = [0xAC, 0x02, 0x01, 0x02, 0x0A];
        var packet = new ClientboundSwingAnimationPacket(EntityId: 300, Hand: 1, AnimationType: 2, Duration: 10);

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "swing_animation");
        Assert.Equal(expected, bound.Encode(packet));
        var decoded = Assert.IsType<ClientboundSwingAnimationPacket>(bound.DecodeFrame(expected));
        Assert.Equal((300, 1, 2, 10), (decoded.EntityId, decoded.Hand, decoded.AnimationType, decoded.Duration));
    }

    [Fact]
    public void AddTransientBlock_RoundTripsPackedPosAndState()
    {
        // BlockPos(1, 2, 3) packs X,Z,Y as 0x0000004000003002, then state 400 as 90 03.
        byte[] expected = [0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x30, 0x02, 0x90, 0x03];
        var packet = new ClientboundAddTransientBlockPacket(new Umpk.Geometry.BlockPos(1, 2, 3), BlockStateId: 400);

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "add_transient_block");
        Assert.Equal(expected, bound.Encode(packet));
        var decoded = Assert.IsType<ClientboundAddTransientBlockPacket>(bound.DecodeFrame(expected));
        Assert.Equal(new Umpk.Geometry.BlockPos(1, 2, 3), decoded.Position);
        Assert.Equal(400, decoded.BlockStateId);
    }

    [Fact]
    public void PunchAndRecipeBookChangeSettings_RejectEachOthersFrames()
    {
        // Wire id 46 is recipe_book_change_settings (VarInt + bool + bool) on 776 and the empty punch on 777.
        BoundPacketCodec punch777 = BoundCodec.At(P777, PacketFlow.Serverbound, "punch");
        BoundPacketCodec recipe776 = BoundCodec.At(776, PacketFlow.Serverbound, "recipe_book_change_settings");

        Assert.ThrowsAny<Exception>(() => recipe776.DecodeFrame([]));
        byte[] recipe = recipe776.Encode(new ServerboundRecipeBookChangeSettingsPacket(BookType: 1, Open: true, Filtering: false));
        Assert.ThrowsAny<Exception>(() => punch777.DecodeFrame(recipe));
    }

    [Fact]
    public void TagHolderSet_NamedTag_777()
    {
        // 26.3 SlotDisplay.TagSlotDisplay is a HolderSet<Item>: VarInt 0 then the tag identifier string.
        // One entry, stonecutter display, tag input, empty result/station, no group, category 0, no requirements, no flags.
        byte[] frame = StonecutterFrameWithTagSlot(w =>
        {
            w.WriteVarInt(6); // tag slot (id 6 on the shared 11-entry table)
            w.WriteVarInt(0); // holder set: named tag follows
            w.WriteString("minecraft:planks");
        });

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "recipe_book_add");
        var packet = Assert.IsType<ClientboundRecipeBookAddPacket>(bound.DecodeFrame(frame));
        Assert.Equal(0, packet.UndecodedEntries);
        SlotDisplay.Tag tag = Assert.IsType<SlotDisplay.Tag>(Assert.Single(GetStonecutterInputs(packet)));
        Assert.Equal("minecraft:planks", tag.Name);
        Assert.Equal(frame, bound.Encode(packet));
    }

    private static byte[] StonecutterFrameWithTagSlot(Action<PacketWriter> writeTagSlot)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(1); // entry count
        w.WriteVarInt(7); // RecipeDisplayId
        w.WriteVarInt(3); // stonecutter display type
        writeTagSlot(w); // input: the tag slot under test
        w.WriteVarInt(0); // result: empty
        w.WriteVarInt(0); // station: empty
        w.WriteVarInt(0); // group: absent
        w.WriteVarInt(0); // category
        w.WriteBool(false); // no crafting requirements
        w.WriteByte(0); // flags
        w.WriteBool(false); // replace
        return buffer.WrittenSpan.ToArray();
    }

    private static System.Collections.Generic.IReadOnlyList<SlotDisplay> GetStonecutterInputs(ClientboundRecipeBookAddPacket packet)
    {
        var displays = new System.Collections.Generic.List<SlotDisplay>();
        foreach (RecipeBookEntry entry in packet.Entries)
            if (entry.Display is RecipeDisplay.Stonecutter stonecutter)
                displays.Add(stonecutter.Input);
        return displays;
    }

    [Fact]
    public void TagHolderSet_InlineIds_777()
    {
        // Holder-set count+1 of 3 means two inline item network ids follow.
        byte[] frame = StonecutterFrameWithTagSlot(w =>
        {
            w.WriteVarInt(6); // tag slot
            w.WriteVarInt(3); // holder set: two inline ids
            w.WriteVarInt(1);
            w.WriteVarInt(2);
        });

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "recipe_book_add");
        var packet = Assert.IsType<ClientboundRecipeBookAddPacket>(bound.DecodeFrame(frame));
        Assert.Equal(0, packet.UndecodedEntries);
        SlotDisplay.TagItems tag = Assert.IsType<SlotDisplay.TagItems>(Assert.Single(GetStonecutterInputs(packet)));
        Assert.Equal([1, 2], tag.ItemIds);
        Assert.Equal(frame, bound.Encode(packet));
    }

    [Fact]
    public void AcceptTeleport_777_RejectsBareConfirm()
    {
        // A hand-built confirm without the destination has no 777 wire form: the writer must refuse, not emit zeroes.
        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Serverbound, "accept_teleportation");
        Assert.ThrowsAny<Exception>(() => bound.Encode(new ServerboundAcceptTeleportationPacket(9)));
    }

    [Fact]
    public void MoveEntityPos_777_SingleStep_RoundTrip()
    {
        // properties 3: one step, on ground. Step: ticks 2, deltas 577/287/0. No angles, no trailing bool.
        byte[] frame = [0x0C, 0x03, 0x02, 0x02, 0x41, 0x01, 0x1F, 0x00, 0x00];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "move_entity_pos");
        var decoded = Assert.IsType<ClientboundMoveEntityPosPacket>(bound.DecodeFrame(frame));
        Assert.Equal(12, decoded.EntityId);
        Assert.True(decoded.OnGround);
        EntityMoveStep step = Assert.Single(decoded.Steps);
        Assert.Equal((2, (short)577, (short)287, (short)0), (step.Ticks, step.DeltaX, step.DeltaY, step.DeltaZ));
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void MoveEntityPos_777_ZeroSteps_RoundTrip()
    {
        // properties 0: no steps, not on ground. Bare three shorts.
        byte[] frame = [0x0C, 0x00, 0x02, 0x41, 0x01, 0x1F, 0x00, 0x00];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "move_entity_pos");
        var decoded = Assert.IsType<ClientboundMoveEntityPosPacket>(bound.DecodeFrame(frame));
        Assert.Equal(12, decoded.EntityId);
        Assert.False(decoded.OnGround);
        Assert.Equal(0x0241, decoded.DeltaX);
        Assert.Empty(decoded.Steps);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void MoveEntityPos_776_Rejects777Frame()
    {
        byte[] frame = [0x0C, 0x03, 0x02, 0x02, 0x41, 0x01, 0x1F, 0x00, 0x00];

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "move_entity_pos");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void MoveEntityPos_777_Rejects776Frame()
    {
        // The 776 form (trailing on-ground bool) overruns the 777 zero-step body by exactly one byte.
        byte[] frame = [0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "move_entity_pos");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void MoveEntityPosRot_777_SingleStep_RoundTrip()
    {
        // properties 3: one step, on ground. Step: ticks 2, deltas 577/287/0. Then angle bytes.
        byte[] frame = [0x0C, 0x03, 0x02, 0x02, 0x41, 0x01, 0x1F, 0x00, 0x00, 0xC0, 0x00];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "move_entity_pos_rot");
        var decoded = Assert.IsType<ClientboundMoveEntityPosRotPacket>(bound.DecodeFrame(frame));
        Assert.Equal(12, decoded.EntityId);
        Assert.True(decoded.OnGround);
        Assert.Equal(577, decoded.DeltaX);
        Assert.Equal(287, decoded.DeltaY);
        Assert.Equal(0, decoded.DeltaZ);
        EntityMoveStep step = Assert.Single(decoded.Steps);
        Assert.Equal((2, (short)577, (short)287, (short)0), (step.Ticks, step.DeltaX, step.DeltaY, step.DeltaZ));
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void MoveEntityPosRot_777_ZeroSteps_RoundTrip()
    {
        // properties 0: no steps, not on ground. Bare three shorts then angles, no trailing bool.
        byte[] frame = [0x0C, 0x00, 0x02, 0x41, 0x01, 0x1F, 0x00, 0x00, 0xC0, 0x00];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "move_entity_pos_rot");
        var decoded = Assert.IsType<ClientboundMoveEntityPosRotPacket>(bound.DecodeFrame(frame));
        Assert.Equal(12, decoded.EntityId);
        Assert.False(decoded.OnGround);
        Assert.Equal(0x0241, decoded.DeltaX);
        Assert.Equal(0x011F, decoded.DeltaY);
        Assert.Equal(0, decoded.DeltaZ);
        Assert.Empty(decoded.Steps);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void MoveEntityPosRot_776_Rejects777Frame()
    {
        byte[] frame = [0x0C, 0x03, 0x02, 0x02, 0x41, 0x01, 0x1F, 0x00, 0x00, 0xC0, 0x00];

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "move_entity_pos_rot");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void MoveEntityPosRot_777_Rejects776Frame()
    {
        // The 776 form (trailing on-ground bool) overruns the 777 zero-step body by exactly one byte.
        byte[] frame = [0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "move_entity_pos_rot");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void EntityPositionSync_777_Stepped_RoundTrip()
    {
        // id 15, STEPPED, one step: (1.0, 2.0, -3.0), tick 3, yaw 90, pitch 0, on ground.
        byte[] frame =
        [
            0x0F, 0x01, 0x01,
            0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03,
            0x42, 0xB4, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x01,
        ];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "entity_position_sync");
        var decoded = Assert.IsType<ClientboundEntityPositionSyncPacket>(bound.DecodeFrame(frame));
        Assert.Equal(15, decoded.EntityId);
        Assert.True(decoded.OnGround);
        EntityPositionPath.Stepped stepped = Assert.IsType<EntityPositionPath.Stepped>(decoded.Path);
        PositionPathStep step = Assert.Single(stepped.Steps);
        Assert.Equal(new Vec3d(1.0, 2.0, -3.0), step.Position);
        Assert.Equal(3, step.TickOffset);
        Assert.Equal(new Vec3d(1.0, 2.0, -3.0), decoded.Values.Position);
        Assert.Equal(90f, decoded.Values.YRot);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void EntityPositionSync_777_Linear_RoundTrip()
    {
        // id 15, LINEAR: three doubles, yaw 90, pitch 0, not on ground.
        byte[] frame =
        [
            0x0F, 0x00,
            0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x42, 0xB4, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00,
        ];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "entity_position_sync");
        var decoded = Assert.IsType<ClientboundEntityPositionSyncPacket>(bound.DecodeFrame(frame));
        Assert.Equal(15, decoded.EntityId);
        Assert.False(decoded.OnGround);
        EntityPositionPath.Linear linear = Assert.IsType<EntityPositionPath.Linear>(decoded.Path);
        Assert.Equal(new Vec3d(1.0, 2.0, -3.0), linear.Position);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void EntityPositionSync_777_EmptySteppedPath_Rejects()
    {
        // A stepped path with zero knots has no wire form (vanilla calls getLast on the list).
        byte[] frame = [0x0F, 0x01, 0x00];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "entity_position_sync");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void EntityPositionSync_776_Rejects777Frame()
    {
        byte[] frame = [0x0F, 0x01, 0x01, 0x00];

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "entity_position_sync");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void EntityPositionSync_777_Rejects776Frame()
    {
        // A 776 body starts with a double (0x40...): 64 is no position-path ordinal.
        byte[] frame = [0x0F, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "entity_position_sync");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void LightUpdate_777_ByteArrayMasks_RoundTrip()
    {
        // 26.3 BitSet masks are VarInt-length-prefixed byte arrays (little-endian, trailing zeros stripped).
        // Sky mask bytes 03 01 set bits 0, 1 and 8: three 2048-byte sections follow.
        byte[] section = new byte[2048];
        Array.Fill(section, (byte)0xFF);
        byte[] frame = LightFrame(skyMask: [0x03, 0x01], blockMask: [], emptySkyMask: [], emptyBlockMask: [],
            skySections: [section, section, section], blockSections: []);

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "light_update");
        var decoded = Assert.IsType<ClientboundLightUpdatePacket>(bound.DecodeFrame(frame));
        Assert.Equal([1L << 0 | 1L << 1 | 1L << 8], decoded.Light.SkyYMask);
        Assert.Empty(decoded.Light.BlockYMask);
        Assert.Equal(3, decoded.Light.SkyUpdates.Count);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void LightUpdate_777_MultiByteMask_MapsLittleEndian()
    {
        // Mask bytes 00 03 set bits 8 and 9: the long is 0x300, and two sections follow.
        byte[] frame = LightFrame(skyMask: [0x00, 0x03], blockMask: [], emptySkyMask: [], emptyBlockMask: [],
            skySections: [new byte[2048], new byte[2048]], blockSections: []);

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "light_update");
        var decoded = Assert.IsType<ClientboundLightUpdatePacket>(bound.DecodeFrame(frame));
        Assert.Equal([0x300L], decoded.Light.SkyYMask);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void LightUpdate_777_Encode_StripsTrailingZeroLongs()
    {
        // java.util.BitSet.toByteArray canonical form: trailing zero longs (and zero bytes) are not emitted.
        // Mask 0x301 sets bits 0, 8 and 9: two mask bytes, three sections.
        var packet = new ClientboundLightUpdatePacket(0, 0, new LightUpdateData(
            [0x301L, 0L, 0L], [], [], [],
            [new byte[2048], new byte[2048], new byte[2048]],
            []));

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "light_update");
        byte[] bytes = bound.Encode(packet);

        // Mask is two bytes (01 03), then sky count 3 and three 2048-byte sections.
        Assert.Equal(0x02, bytes[2]);
        Assert.Equal(0x01, bytes[3]);
        Assert.Equal(0x03, bytes[4]);
        Assert.Equal(bytes, bound.Encode(Assert.IsType<ClientboundLightUpdatePacket>(bound.DecodeFrame(bytes))));
    }

    [Fact]
    public void LightUpdate_777_TruncatedMask_Throws()
    {
        // The mask claims four bytes but only two arrive.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(0);
        w.WriteVarInt(0);
        w.WriteVarInt(4);
        w.WriteByte(0x01);
        w.WriteByte(0x02);
        byte[] frame = buffer.WrittenSpan.ToArray();

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "light_update");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    [Fact]
    public void LightUpdate_776_LongArrayMasks_Unaffected()
    {
        // The pre-26.3 form (VarInt count + big-endian longs) still round-trips on 776: one long, bit 0 set, one section.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(0);
        w.WriteVarInt(0);
        w.WriteVarInt(1);
        w.WriteLong(1L);
        w.WriteVarInt(0);
        w.WriteVarInt(0);
        w.WriteVarInt(0);
        w.WriteVarInt(1);
        w.WriteByteArray(new byte[2048]);
        w.WriteVarInt(0);
        byte[] frame = buffer.WrittenSpan.ToArray();

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "light_update");
        var decoded = Assert.IsType<ClientboundLightUpdatePacket>(bound.DecodeFrame(frame));
        Assert.Equal([1L], decoded.Light.SkyYMask);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void LightUpdate_776_RejectsByteArrayMaskFrame()
    {
        // A 777 mask frame must fault on 776: the length-prefixed bytes parse as a long count.
        byte[] frame = LightFrame(skyMask: [0x03, 0x01], blockMask: [], emptySkyMask: [], emptyBlockMask: [],
            skySections: [new byte[2048], new byte[2048], new byte[2048]], blockSections: []);

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "light_update");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(frame));
    }

    private static byte[] LightFrame(
        byte[] skyMask, byte[] blockMask, byte[] emptySkyMask, byte[] emptyBlockMask,
        byte[][] skySections, byte[][] blockSections)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(0); // chunk x
        w.WriteVarInt(0); // chunk z
        w.WriteByteArray(skyMask);
        w.WriteByteArray(blockMask);
        w.WriteByteArray(emptySkyMask);
        w.WriteByteArray(emptyBlockMask);
        w.WriteVarInt(skySections.Length);
        foreach (byte[] section in skySections)
            w.WriteByteArray(section);
        w.WriteVarInt(blockSections.Length);
        foreach (byte[] section in blockSections)
            w.WriteByteArray(section);
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public void UpdateAdvancements_777_PositionedEntries_RoundTrip()
    {
        // 26.3 added elements are holder (id + node) followed by Float x and Float y. No display, no criteria, empty requirements, no telemetry.
        byte[] frame = AdvancementFrame(x: 1.5f, y: -2.25f);

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "update_advancements");
        var decoded = Assert.IsType<ClientboundUpdateAdvancementsPacket>(bound.DecodeFrame(frame));
        AdvancementEntry entry = Assert.Single(decoded.Added);
        Assert.Equal(1.5f, entry.PositionX);
        Assert.Equal(-2.25f, entry.PositionY);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void UpdateAdvancements_777_VisibleDisplay_HasNoInnerCoordinates()
    {
        // 26.3 DisplayInfo ends after the optional background: title, description, template icon, frame, flags. The tab position rides only on the outer entry (1.5, -2.25). Under the old double-coordinate assumption the inner read consumes requirements/telemetry/outer-x as x/y and the frame faults.
        byte[] frame = VisibleAdvancementFrame(
            withInnerCoordinates: false, withOuterCoordinates: true, entryX: 1.5f, entryY: -2.25f);

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Clientbound, "update_advancements");
        var decoded = Assert.IsType<ClientboundUpdateAdvancementsPacket>(bound.DecodeFrame(frame));
        AdvancementEntry entry = Assert.Single(decoded.Added);
        Assert.Equal(1.5f, entry.PositionX);
        Assert.Equal(-2.25f, entry.PositionY);
        AdvancementDisplayInfo? display = entry.Value.Display;
        Assert.NotNull(display);
        Assert.Equal("T", display!.Title.ToPlainText());
        Assert.Equal("D", display.Description.ToPlainText());
        Assert.Equal(1, display.Icon.Item.NetworkId);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void UpdateAdvancements_776_VisibleDisplay_KeepsInnerCoordinates()
    {        // Through 776 the inner display carries its own x/y (9.5, 8.25) and entries carry none.
        byte[] frame = VisibleAdvancementFrame(
            withInnerCoordinates: true, withOuterCoordinates: false, entryX: 0f, entryY: 0f);

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "update_advancements");
        var decoded = Assert.IsType<ClientboundUpdateAdvancementsPacket>(bound.DecodeFrame(frame));
        AdvancementEntry entry = Assert.Single(decoded.Added);
        Assert.Null(entry.PositionX);
        Assert.Null(entry.PositionY);
        AdvancementDisplayInfo? display776 = entry.Value.Display;
        Assert.NotNull(display776);
        Assert.Equal(9.5f, display776!.X);
        Assert.Equal(8.25f, display776.Y);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    private static byte[] TextComponentBytes(string text)
    {
        // Network-NBT modern component for plain text: a bare string tag (type 0x08, u16-big-endian length, bytes).
        // NBT strings are u16-big-endian prefixed (PacketWriter.WriteString is VarInt-prefixed and must not be used here).
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteByte(0x08);
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
        w.WriteShort((short)utf8.Length);
        w.WriteBytes(utf8);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] VisibleAdvancementFrame(
        bool withInnerCoordinates, bool withOuterCoordinates, float entryX, float entryY, float innerX = 9.5f, float innerY = 8.25f)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteBool(false); // reset
        w.WriteVarInt(1); // added count
        w.WriteString("minecraft:test/visible");
        w.WriteBool(false); // parent: absent
        w.WriteBool(true); // display: present
        w.WriteBytes(TextComponentBytes("T")); // title
        w.WriteBytes(TextComponentBytes("D")); // description
        w.WriteVarInt(1); // icon: stone holder id in the item test registries
        w.WriteVarInt(1); // icon count
        w.WriteVarInt(0); // icon added
        w.WriteVarInt(0); // icon removed
        w.WriteVarInt(0); // frame: task
        w.WriteInt(0); // flags: no background, no toast, shown
        if (withInnerCoordinates)
        {
            w.WriteFloat(innerX);
            w.WriteFloat(innerY);
        }

        w.WriteVarInt(0); // requirements: none
        w.WriteBool(false); // telemetry
        if (withOuterCoordinates)
        {
            w.WriteFloat(entryX);
            w.WriteFloat(entryY);
        }

        w.WriteVarInt(0); // removed: none
        w.WriteVarInt(0); // progress: none
        w.WriteBool(true); // show advancements
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public void UpdateAdvancements_776_RejectsPositionedFrame()
    {
        // The positioned tail has no reader on 776: the floats must fault there, not parse as the removed set.
        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "update_advancements");
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(AdvancementFrame(x: 1.5f, y: -2.25f)));
    }

    [Fact]
    public void UpdateAdvancements_776_ShortFrame_Unaffected()
    {
        // The pre-26.3 element (holder only) still round-trips on 776.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteBool(false); // reset
        w.WriteVarInt(1); // added count
        w.WriteString("minecraft:test/root");
        w.WriteBool(false); // parent: absent
        w.WriteBool(false); // display: absent
        w.WriteVarInt(0); // requirements: none
        w.WriteBool(false); // telemetry
        w.WriteVarInt(0); // removed: none
        w.WriteVarInt(0); // progress: none
        w.WriteBool(true); // show advancements
        byte[] frame = buffer.WrittenSpan.ToArray();

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, "update_advancements");
        var decoded = Assert.IsType<ClientboundUpdateAdvancementsPacket>(bound.DecodeFrame(frame));
        Assert.Single(decoded.Added);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    private static byte[] AdvancementFrame(float x, float y)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteBool(false); // reset
        w.WriteVarInt(1); // added count
        w.WriteString("minecraft:test/root");
        w.WriteBool(false); // parent: absent
        w.WriteBool(false); // display: absent
        w.WriteVarInt(0); // requirements: none
        w.WriteBool(false); // telemetry
        w.WriteFloat(x);
        w.WriteFloat(y);
        w.WriteVarInt(0); // removed: none
        w.WriteVarInt(0); // progress: none
        w.WriteBool(true); // show advancements
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public void AcceptTeleport_777_FullFrame()
    {
        // VarInt id, three doubles, two floats.
        byte[] frame =
        [
            0x09,
            0x40, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 10.0
            0x40, 0x50, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, // 65.0
            0xC0, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // -3.0
            0x42, 0xB4, 0x00, 0x00, // 90.0f
            0x00, 0x00, 0x00, 0x00, // 0.0f
        ];

        BoundPacketCodec bound = BoundCodec.At(P777, PacketFlow.Serverbound, "accept_teleportation");
        var decoded = Assert.IsType<ServerboundAcceptTeleportationPacket>(bound.DecodeFrame(frame));
        Assert.Equal(9, decoded.TeleportId);
        Assert.True(decoded.HasDestination);
        Assert.Equal((10.0, 65.0, -3.0, 90f, 0f), (decoded.X, decoded.Y, decoded.Z, decoded.YRot, decoded.XRot));
        Assert.Equal(frame, bound.Encode(decoded));
    }

    private static BoundPacketCodec ConfigBound(int protocol, PacketFlow flow, string identifier)
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Configuration, flow, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(ProtocolPhase.Configuration, flow).TryGetInbound(0, out BoundPacketCodec? bound));
        Assert.True(bound.IsImplemented, $"{identifier} is a configuration marker at protocol {protocol}");
        return bound;
    }
}
