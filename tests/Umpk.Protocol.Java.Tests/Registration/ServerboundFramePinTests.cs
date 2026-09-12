using System.Buffers;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>Byte-anchored (pinned-frame) tests for multi-field SERVERBOUND codecs. For each codec the EXPECTED wire bytes are hand-built field-by-field from the packet's wire order, NOT by running the encoder, so the test cannot be satisfied by an encoder that agrees with itself. Each anchor asserts both directions (Encode == expected, Decode(expected) == packet) and, where an adjacent era exists, a differential Assert.NotEqual against it.</summary>
public sealed class ServerboundFramePinTests
{
    // The 1.14+ packed position puts x in the top 26 bits, z in the middle 26, and y in the low 12.
    private static long PackBlockPos114(int x, int y, int z) =>
        ((x & 0x3FFFFFFL) << 38) | ((z & 0x3FFFFFFL) << 12) | (y & 0xFFFL);

    // The 1.8 packed position puts x in the top 26 bits, y in the middle 12, and z in the low 26.
    private static long PackBlockPos18(int x, int y, int z) =>
        ((x & 0x3FFFFFFL) << 38) | ((y & 0xFFFL) << 26) | (z & 0x3FFFFFFL);

    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    // use_item_on (modern) - the most field-heavy serverbound frame.
    // Wire order for the modern use-item-on frame: hand VarInt; packed block position; direction VarInt;
    // three cursor floats; inside boolean; world-border-hit boolean; sequence VarInt. InteractionHand{MAIN=0,OFF=1}; Direction{DOWN=0,UP=1,...}.
    [Fact]
    public void UseItemOnModern_PinnedFrame()
    {
        var packet = new ServerboundUseItemOnPacket(
            Hand: 1, Position: new BlockPos(10, 64, -30), Face: 1,
            CursorX: 0.5f, CursorY: 1.0f, CursorZ: 0.25f,
            Inside: true, WorldBorderHit: false, Sequence: 7);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(1);                            // writeEnum(hand) -> OFF_HAND = 1
            w.WriteLong(PackBlockPos114(10, 64, -30));   // writeBlockHitResult: writeBlockPos(pos)
            w.WriteVarInt(1);                            // writeEnum(direction) -> UP = 1
            w.WriteFloat(0.5f);                          // cursor x offset
            w.WriteFloat(1.0f);                          // cursor y offset
            w.WriteFloat(0.25f);                         // cursor z offset
            w.WriteBool(true);                           // inside
            w.WriteBool(false);                          // worldBorderHit
            w.WriteVarInt(7);                            // sequence
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(UseItemCodecs.UseItemOnModern, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(UseItemCodecs.UseItemOnModern, expected));
    }

    // interact (modern) - interact-at shape carries the most fields. Wire order for the modern interact-at frame: entity-id VarInt, action VarInt, three location floats, hand VarInt, and secondary-action boolean. ActionType{INTERACT=0,ATTACK=1,INTERACT_AT=2}.
    [Fact]
    public void InteractV1_16_InteractAt_PinnedFrame()
    {
        var packet = new ServerboundInteractPacket(
            EntityId: 5, Action: 2, Hand: 1, InteractAt: new Vec3d(1.0, 2.0, 3.0), UsingSecondaryAction: true);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(5);      // entityId
            w.WriteVarInt(2);      // writeEnum(ActionType) -> INTERACT_AT = 2
            w.WriteFloat(1.0f);    // location x
            w.WriteFloat(2.0f);    // location y
            w.WriteFloat(3.0f);    // location z
            w.WriteVarInt(1);      // writeEnum(hand) -> OFF_HAND = 1
            w.WriteBool(true);     // usingSecondaryAction
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.InteractV1_16, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(EntityServerboundCodecs.InteractV1_16, expected));

        // Differential vs the 1.8 era: 1.8 use-entity has no hand VarInt and no secondary bool, so the same interact-at packet encodes shorter and differently.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.InteractV1_8, packet));
    }

    // signed chat (modern) - message, timestamp, salt, optional signature, last-seen window + checksum. Wire order for the modern signed-chat frame: message UTF-8 capped at 256 characters, epoch-millis long, salt long, nullable 256-byte signature, last-seen offset VarInt, fixed 20-bit acknowledgment bitset (3 bytes), and checksum byte.
    [Fact]
    public void SignedChatV1_21_5_PinnedFrame()
    {
        var signature = new byte[256];
        for (int i = 0; i < signature.Length; i++)
            signature[i] = (byte)i;

        var lastSeen = new LastSeenMessagesUpdate(Offset: 3, Acknowledged: [0x05, 0x00, 0x00], Checksum: 0x2A);
        var packet = new ServerboundSignedChatPacket(
            Message: "hi", TimestampMillis: 1234567890123L, Salt: 0x0102030405060708L,
            Signature: signature, LastSeen: lastSeen);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(2);                          // writeUtf length prefix
            w.WriteBytes([0x68, 0x69]);                // "hi" utf-8
            w.WriteLong(1234567890123L);               // writeInstant -> epoch millis
            w.WriteLong(0x0102030405060708L);          // salt
            w.WriteBool(true);                         // writeNullable: signature present
            w.WriteBytes(signature);                   // 256-byte signature
            w.WriteVarInt(3);                          // last-seen offset
            w.WriteBytes([0x05, 0x00, 0x00]);          // fixed 20-bit ack bitset (3 bytes)
            w.WriteByte(0x2A);                         // last-seen checksum
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCodecs.SignedV1_21_5, packet));

        // Field-wise decode check: the record's default equality compares the byte[] members by reference, so assert Signature/Acknowledged with xUnit's structural collection comparison.
        ServerboundSignedChatPacket decoded = CodecRoundTrip.Decode(ChatCodecs.SignedV1_21_5, expected);
        Assert.Equal(packet.Message, decoded.Message);
        Assert.Equal(packet.TimestampMillis, decoded.TimestampMillis);
        Assert.Equal(packet.Salt, decoded.Salt);
        Assert.Equal(packet.Signature, decoded.Signature);
        Assert.Equal(packet.LastSeen.Offset, decoded.LastSeen.Offset);
        Assert.Equal(packet.LastSeen.Acknowledged, decoded.LastSeen.Acknowledged);
        Assert.Equal(packet.LastSeen.Checksum, decoded.LastSeen.Checksum);

        // Differential vs 1.20.2: that era omits the trailing last-seen checksum byte.
        byte[] preChecksum = CodecRoundTrip.Encode(ChatCodecs.SignedV1_19_3, packet);
        Assert.Equal(expected.Length - 1, preChecksum.Length);
        Assert.NotEqual(expected, preChecksum);
    }

    // move player pos+rot (modern). Wire order for the modern position-and-rotation frame: Three position doubles, yaw float, pitch float, and a flags byte with on-ground in bit 0 and horizontal-collision in bit 1.
    [Fact]
    public void MovePlayerPosRotV1_21_5_PinnedFrame()
    {
        var packet = new ServerboundMovePlayerPosRotPacket(
            X: 1.5, Y: 64.0, Z: -2.5, Yaw: 45f, Pitch: -10f, OnGround: true, HorizontalCollision: true);

        byte[] expected = Build(w =>
        {
            w.WriteDouble(1.5);
            w.WriteDouble(64.0);
            w.WriteDouble(-2.5);
            w.WriteFloat(45f);
            w.WriteFloat(-10f);
            w.WriteByte(0x03);    // packFlags(onGround=1 | horizontalCollision=2)
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.MovePlayerPosRotV1_14, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(EntityServerboundCodecs.MovePlayerPosRotV1_14, expected));

        // Differential vs the 1.8 era: 1.8 writes a bare on-ground boolean (0x01) instead of the packed flags byte (0x03), so the trailing byte differs.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.MovePlayerPosRotV1_8, packet));
    }

    // move player pos (modern). Wire order for the modern position frame: Three position doubles and the packed on-ground/horizontal-collision flags byte.
    [Fact]
    public void MovePlayerPosV1_21_5_PinnedFrame()
    {
        var packet = new ServerboundMovePlayerPosPacket(X: 1.5, Y: 64.0, Z: -2.5, OnGround: true, HorizontalCollision: true);

        byte[] expected = Build(w =>
        {
            w.WriteDouble(1.5);
            w.WriteDouble(64.0);
            w.WriteDouble(-2.5);
            w.WriteByte(0x03);    // packFlags(onGround=1 | horizontalCollision=2)
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.MovePlayerPosV1_14, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(EntityServerboundCodecs.MovePlayerPosV1_14, expected));

        // Differential vs 1.8: 1.8 writes a bare on-ground boolean (0x01) instead of the packed flags.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.MovePlayerPosV1_8, packet));
    }

    // interact (1.8 use-entity). Wire order for the 1.8 use-entity frame: entity-id VarInt, type VarInt, then three hit-vector floats for INTERACT_AT. No hand VarInt, no using-secondary boolean (those arrive with 1.9).
    [Fact]
    public void InteractV1_8_InteractAt_PinnedFrame()
    {
        var packet = new ServerboundInteractPacket(
            EntityId: 5, Action: 2, Hand: null, InteractAt: new Vec3d(1.0, 2.0, 3.0), UsingSecondaryAction: null);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(5);      // entityId
            w.WriteVarInt(2);      // action type INTERACT_AT
            w.WriteFloat(1.0f);    // hit vec x
            w.WriteFloat(2.0f);    // hit vec y
            w.WriteFloat(3.0f);    // hit vec z
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.InteractV1_8, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(EntityServerboundCodecs.InteractV1_8, expected));

        // Differential vs 1.21.5: the modern era appends a hand VarInt and a secondary-action boolean.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.InteractV1_16, packet));
    }

    // player action (modern block dig). Wire order for the modern player-action frame: Action VarInt, packed block position, direction byte, and sequence VarInt. Action enum START_DESTROY_BLOCK=0, ABORT=1, STOP=2,...
    [Fact]
    public void PlayerActionV1_21_5_PinnedFrame()
    {
        var packet = new ServerboundPlayerActionPacket(Action: 2, Position: new BlockPos(-5, 10, 300), Direction: 5, Sequence: 42);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(2);                            // writeEnum(action) -> STOP_DESTROY_BLOCK = 2
            w.WriteLong(PackBlockPos114(-5, 10, 300));   // writeBlockPos(pos)
            w.WriteByte(5);                              // direction 3D value
            w.WriteVarInt(42);                           // sequence
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerActionV1_19, packet));
        ServerboundPlayerActionPacket decoded = CodecRoundTrip.Decode(EntityServerboundCodecs.PlayerActionV1_19, expected);
        Assert.Equal(packet.Action, decoded.Action);
        Assert.Equal(packet.Position, decoded.Position);
        Assert.Equal(packet.Direction, decoded.Direction);
        Assert.Equal(packet.Sequence, decoded.Sequence);

        // Differential vs 1.8: 1.8 uses a single action byte, the pre-1.14 block-position packing, and no trailing sequence VarInt.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerActionV1_8, packet));
    }

    // player action (1.14-1.18.2 block dig). The sequence VarInt only arrived at 1.19, so this era shares the 1.14 packed block-position wire but writes NO trailing sequence. Sharing the 1.21.5 codec here appended a spurious byte the server rejected ("1 byte extra") and kicked the dig.
    [Fact]
    public void PlayerActionV1_14_PinnedFrame_NoSequence()
    {
        var packet = new ServerboundPlayerActionPacket(Action: 0, Position: new BlockPos(-5, 10, 300), Direction: 5, Sequence: 42);

        // action VarInt, 1.14 packed block position, direction byte - and nothing else.
        byte[] expected = Build(w =>
        {
            w.WriteVarInt(0);                            // START_DESTROY_BLOCK
            w.WriteLong(PackBlockPos114(-5, 10, 300));   // 1.14 packed block position
            w.WriteByte(5);                              // direction 3D value
        });

        byte[] actual = CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerActionV1_14, packet);
        Assert.Equal(expected, actual);

        // The frame is exactly the no-sequence length: the spurious 1.19 sequence byte is absent, and it is strictly shorter than the 1.21.5 frame for the same packet.
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(actual.Length < CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerActionV1_19, packet).Length);

        // Decode ignores the sequence (there is none on the wire).
        ServerboundPlayerActionPacket decoded = CodecRoundTrip.Decode(EntityServerboundCodecs.PlayerActionV1_14, expected);
        Assert.Equal(packet.Action, decoded.Action);
        Assert.Equal(packet.Position, decoded.Position);
        Assert.Equal(packet.Direction, decoded.Direction);
        Assert.Null(decoded.Sequence);
    }

    // move vehicle (serverbound, modern). Wire order for the modern move-vehicle frame: Wire order: three position doubles, yaw float, pitch float, and on-ground boolean.
    [Fact]
    public void MoveVehicle_PinnedFrame()
    {
        var packet = new ServerboundMoveVehiclePacket(X: 1.5, Y: 64.0, Z: -2.5, Yaw: 45f, Pitch: -10f, OnGround: true);

        byte[] expected = Build(w =>
        {
            w.WriteDouble(1.5);
            w.WriteDouble(64.0);
            w.WriteDouble(-2.5);
            w.WriteFloat(45f);
            w.WriteFloat(-10f);
            w.WriteBool(true);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.MoveVehicle, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(EntityServerboundCodecs.MoveVehicle, expected));
    }

    // block place (1.8). Wire order for the 1.8 block-place frame: Packed block position, placed-block-direction byte, item stack, and three facing bytes. An empty stack serialises as short -1. Block position uses the pre-1.14 long packing.
    [Fact]
    public void BlockPlaceV1_8_PinnedFrame()
    {
        var packet = new ServerboundLegacyBlockPlacePacket(
            Position: new BlockPos(10, 64, -30), Face: 1, HeldItem: ItemStack.Empty,
            CursorX: 8, CursorY: 15, CursorZ: 4);

        byte[] expected = Build(w =>
        {
            w.WriteLong(PackBlockPos18(10, 64, -30));   // writeBlockPos (1.8 long packing)
            w.WriteByte(1);                             // placed block direction (face)
            w.WriteShort(-1);                           // writeItemStackToBuffer(empty) -> short -1
            w.WriteByte(8);                             // facing X (in-block cursor)
            w.WriteByte(15);                            // facing Y
            w.WriteByte(4);                             // facing Z
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(UseItemCodecs.BlockPlaceV1_8, packet));

        ServerboundLegacyBlockPlacePacket decoded = CodecRoundTrip.Decode(UseItemCodecs.BlockPlaceV1_8, expected);
        Assert.Equal(packet.Position, decoded.Position);
        Assert.Equal(packet.Face, decoded.Face);
        Assert.True(decoded.HeldItem.IsEmpty);
        Assert.Equal(packet.CursorX, decoded.CursorX);
        Assert.Equal(packet.CursorY, decoded.CursorY);
        Assert.Equal(packet.CursorZ, decoded.CursorZ);
    }

    // sign_update - three era forms. The 1.14 form writes a block position and four strings. The 1.20 form adds An is-front-text boolean follows the block position. Pre-1.14 packs the block position with the 1.8 layout (y in the middle 12 bits).
    [Fact]
    public void SignUpdateV1_9_PinnedFrame()
    {
        var packet = new ServerboundSignUpdatePacket(
            new BlockPos(10, 64, -30), IsFrontText: true, "line1", "line2", "line3", "line4");

        byte[] expected = Build(w =>
        {
            w.WriteLong(PackBlockPos18(10, 64, -30));   // pre-1.14 block-pos packing
            w.WriteString("line1", 384);
            w.WriteString("line2", 384);
            w.WriteString("line3", 384);
            w.WriteString("line4", 384);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(UiMiscCodecs.SignUpdateV1_9, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(UiMiscCodecs.SignUpdateV1_9, expected));

        // Differential vs 1.14: the block-pos packing differs.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(UiMiscCodecs.SignUpdateV1_14, packet));
    }

    [Fact]
    public void SignUpdateV1_14_PinnedFrame()
    {
        var packet = new ServerboundSignUpdatePacket(
            new BlockPos(10, 64, -30), IsFrontText: true, "a", "b", "c", "d");

        byte[] expected = Build(w =>
        {
            w.WriteLong(PackBlockPos114(10, 64, -30));  // 1.14 block-pos packing, no front-text flag
            w.WriteString("a", 384);
            w.WriteString("b", 384);
            w.WriteString("c", 384);
            w.WriteString("d", 384);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(UiMiscCodecs.SignUpdateV1_14, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(UiMiscCodecs.SignUpdateV1_14, expected));

        // Differential vs 1.20: 1.20 inserts the front-text boolean.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(UiMiscCodecs.SignUpdateV1_20, packet));
    }

    [Fact]
    public void SignUpdateV1_20_PinnedFrame()
    {
        var packet = new ServerboundSignUpdatePacket(
            new BlockPos(10, 64, -30), IsFrontText: false, "a", "b", "c", "d");

        byte[] expected = Build(w =>
        {
            w.WriteLong(PackBlockPos114(10, 64, -30));  // 1.14 block-pos packing
            w.WriteBool(false);                         // isFrontText
            w.WriteString("a", 384);
            w.WriteString("b", 384);
            w.WriteString("c", 384);
            w.WriteString("d", 384);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(UiMiscCodecs.SignUpdateV1_20, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(UiMiscCodecs.SignUpdateV1_20, expected));
    }

    // rename_item (1.13+): one UTF-8 name.
    [Fact]
    public void RenameItemV1_13_PinnedFrame()
    {
        var packet = new ServerboundRenameItemPacket("Excalibur");

        byte[] expected = Build(w => w.WriteString("Excalibur"));

        Assert.Equal(expected, CodecRoundTrip.Encode(ContainerCodecs.RenameItemV1_13, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(ContainerCodecs.RenameItemV1_13, expected));
    }

    // teleport_to_entity (1.9+): UUID as two big-endian 64-bit halves, most significant then least significant.
    [Fact]
    public void TeleportToEntity_PinnedFrame()
    {
        var uuid = new Guid("12345678-90ab-cdef-1234-567890abcdef");
        var packet = new ServerboundTeleportToEntityPacket(uuid);

        byte[] expected = Build(w => w.WriteUuid(uuid));

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.TeleportToEntity, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(EntityServerboundCodecs.TeleportToEntity, expected));
    }

    // place_recipe - two era forms. The named-recipe form writes a byte container id, Recipe identifier and shift-down boolean. 1.21.2 switched to CONTAINER_ID (VarInt) + RecipeDisplayId (VarInt) + BOOL.
    [Fact]
    public void PlaceRecipeByNameV1_14_PinnedFrame()
    {
        var packet = new ServerboundPlaceRecipeByNamePacket(3, Identifier.Minecraft("diamond_sword"), UseMaxItems: true);

        byte[] expected = Build(w =>
        {
            w.WriteByte(3);                            // byte container id
            w.WriteString("minecraft:diamond_sword"); // resource-location recipe id
            w.WriteBool(true);                         // shiftDown / use-max-items
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.PlaceRecipeByNameV1_13, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(RecipeCodecs.PlaceRecipeByNameV1_13, expected));
    }

    [Fact]
    public void PlaceRecipeModern_PinnedFrame()
    {
        var packet = new ServerboundPlaceRecipePacket(3, 42, UseMaxItems: true);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(3);     // VarInt container id (CONTAINER_ID)
            w.WriteVarInt(42);    // VarInt recipe display id
            w.WriteBool(true);    // use-max-items
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.PlaceRecipeModern, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(RecipeCodecs.PlaceRecipeModern, expected));
    }

    // container_button_click (new action; codec pre-existed). Modern form: VarInt window, VarInt button.
    [Fact]
    public void ContainerButtonClickModern_PinnedFrame()
    {
        var packet = new ServerboundContainerButtonClickPacket(5, 2);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(5);   // container id
            w.WriteVarInt(2);   // button id
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ContainerCodecs.ContainerButtonClickModern, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(ContainerCodecs.ContainerButtonClickModern, expected));
    }

    // edit_book (new action; codec pre-existed): VAR_INT slot; list(100) of stringUtf8(1024) pages; optional(stringUtf8(32)) title.
    [Fact]
    public void EditBookV1_21_5_PinnedFrame()
    {
        var packet = new ServerboundEditBookPacket(0, ["page one", "page two"], "My Title");

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(0);                 // slot
            w.WriteVarInt(2);                 // page list length
            w.WriteString("page one", 1024);
            w.WriteString("page two", 1024);
            w.WriteBool(true);                // optional title present
            w.WriteString("My Title", 32);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(UiMiscCodecs.EditBookV1_17, packet));

        // Decode compares fields individually: the record's Pages is an IReadOnlyList, which uses reference (not structural) equality in the generated record Equals, so a whole-record Assert.Equal would fail on two distinct list instances with equal contents.
        ServerboundEditBookPacket decoded = CodecRoundTrip.Decode(UiMiscCodecs.EditBookV1_17, expected);
        Assert.Equal(packet.Slot, decoded.Slot);
        Assert.Equal(packet.Pages, decoded.Pages);
        Assert.Equal(packet.Title, decoded.Title);
    }

    // set_command_block - two era forms. The set-command-block frame writes a block position and command string, Mode VarInt ordinal, then flags byte: track_output=1, conditional=2, automatic=4. Mode enum {SEQUENCE=0, AUTO=1, REDSTONE=2}. Pre-1.14 packs the block pos with the 1.8 layout.
    [Fact]
    public void SetCommandBlockV1_13_PinnedFrame()
    {
        var packet = new ServerboundSetCommandBlockPacket(
            new BlockPos(10, 64, -30), "/say hi", Mode: 2, TrackOutput: true, Conditional: false, Automatic: true);

        byte[] expected = Build(w =>
        {
            w.WriteLong(PackBlockPos18(10, 64, -30));  // pre-1.14 block-pos packing
            w.WriteString("/say hi");
            w.WriteVarInt(2);                          // mode REDSTONE
            w.WriteByte(1 | 4);                        // track_output | automatic
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(WorldBlockCodecs.SetCommandBlockV1_13, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(WorldBlockCodecs.SetCommandBlockV1_13, expected));

        // Differential vs 1.14: block-pos packing differs.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(WorldBlockCodecs.SetCommandBlockV1_14, packet));
    }

    [Fact]
    public void SetCommandBlockV1_14_PinnedFrame()
    {
        var packet = new ServerboundSetCommandBlockPacket(
            new BlockPos(10, 64, -30), "/say hi", Mode: 1, TrackOutput: false, Conditional: true, Automatic: false);

        byte[] expected = Build(w =>
        {
            w.WriteLong(PackBlockPos114(10, 64, -30)); // 1.14 block-pos packing
            w.WriteString("/say hi");
            w.WriteVarInt(1);                          // mode AUTO
            w.WriteByte(2);                            // conditional
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(WorldBlockCodecs.SetCommandBlockV1_14, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(WorldBlockCodecs.SetCommandBlockV1_14, expected));
    }

    // chat_session_update (1.19.3+ / v3) wire order: UUID session id, epoch-millis expiry long, length-prefixed DER key, length-prefixed v2 signature.
    [Fact]
    public void ChatSessionUpdateV1_19_3_PinnedFrame()
    {
        Guid sessionId = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");
        byte[] keyDer = [0x30, 0x82, 0x01, 0x22];
        byte[] signature = [0xAA, 0xBB, 0xCC];
        var packet = new ServerboundChatSessionUpdatePacket(
            sessionId, new ProfilePublicKeyData(0x0000018ABCDEF012L, keyDer, signature));

        byte[] expected = Build(w =>
        {
            w.WriteUuid(sessionId);                 // writeUUID(sessionId)
            w.WriteLong(0x0000018ABCDEF012L);       // writeInstant -> epoch millis
            w.WriteVarInt(keyDer.Length);           // key: writeByteArray length prefix
            w.WriteBytes(keyDer);                   // DER SubjectPublicKeyInfo bytes
            w.WriteVarInt(signature.Length);        // signature: writeByteArray length prefix
            w.WriteBytes(signature);                // v2 signature bytes
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCodecs.ChatSessionUpdateV1_19_3, packet));

        ServerboundChatSessionUpdatePacket decoded =
            CodecRoundTrip.Decode(ChatCodecs.ChatSessionUpdateV1_19_3, expected);
        Assert.Equal(packet.SessionId, decoded.SessionId);
        Assert.Equal(packet.Key.ExpiresAtMillis, decoded.Key.ExpiresAtMillis);
        Assert.Equal(packet.Key.KeyDer, decoded.Key.KeyDer);
        Assert.Equal(packet.Key.KeySignature, decoded.Key.KeySignature);
    }

    // chat_command / chat_command_signed - four era forms.
    //
    // A command only executes on these packets. From 1.19, ordinary chat broadcasts text and never reaches the command dispatcher; the command handlers are the routes that execute commands.
    //
    // Every pin below uses a NON-EMPTY argument-signature list and (from 1.19.3) a NON-EMPTY last-seen window on purpose: an empty list and a zero window decode identically under the wrong era framing, so pinning the empty case would prove nothing.
    //
    // Protocol 759: command UTF-8 capped at 256 characters; epoch-millis timestamp; salt; a map of UTF-8 argument names to length-prefixed signatures; preview boolean. Protocol 760 adds a last-seen update: at most five UUID-and-length-prefixed-signature entries, then one optional last-received entry. Protocols 761-765 use fixed 256-byte argument signatures, then an offset VarInt and a fixed 20-bit acknowledgment bitset. They omit the preview flag. Protocol 766 splits the packet. The unsigned form is only the command string; the signed form keeps the 761 body. Protocol 770 appends a checksum byte to the last-seen update.
    private static byte[] CommandSignature()
    {
        var signature = new byte[256];
        for (int i = 0; i < signature.Length; i++)
            signature[i] = (byte)i;

        return signature;
    }

    private const string PinnedCommand = "msg Steve hi";
    private const long PinnedCommandTimestamp = 1234567890123L;
    private const long PinnedCommandSalt = 0x0102030405060708L;

    private static ServerboundSignedChatCommandPacket PinnedSignedCommand() =>
        new(PinnedCommand, PinnedCommandTimestamp, PinnedCommandSalt,
            [new SignedCommandArgument("message", CommandSignature())],
            new LastSeenMessagesUpdate(Offset: 3, Acknowledged: [0x05, 0x00, 0x00], Checksum: 0x2A));

    private static ServerboundChatCommandSignedPacket PinnedStandaloneCommand() =>
        new(PinnedCommand, PinnedCommandTimestamp, PinnedCommandSalt,
            [new SignedCommandArgument("message", CommandSignature())],
            new LastSeenMessagesUpdate(Offset: 3, Acknowledged: [0x05, 0x00, 0x00], Checksum: 0x2A));

    // The fields 759 and 760 share, up to and including the preview flag.
    private static void WriteV1CommandBody(PacketWriter w, byte[] signature)
    {
        w.WriteString(PinnedCommand, 256);        // writeUtf(command, 256)
        w.WriteLong(PinnedCommandTimestamp);      // writeInstant -> epoch millis
        w.WriteLong(PinnedCommandSalt);           // salt (inside ArgumentSignatures on 759)
        w.WriteVarInt(1);                         // one argument signature
        w.WriteString("message", 16);             // argument name, utf(16)
        w.WriteByteArray(signature);              // writeByteArray: VarInt length + bytes
        w.WriteBool(false);                       // signedPreview
    }

    [Fact]
    public void ChatCommandV1_19_PinnedFrame()
    {
        byte[] signature = CommandSignature();
        ServerboundSignedChatCommandPacket packet = PinnedSignedCommand();

        byte[] expected = Build(w => WriteV1CommandBody(w, signature));

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19, packet));

        ServerboundSignedChatCommandPacket decoded =
            CodecRoundTrip.Decode(ChatCommandCodecs.SignedV1_19, expected);
        Assert.Equal(packet.Command, decoded.Command);
        Assert.Equal(packet.TimestampMillis, decoded.TimestampMillis);
        Assert.Equal(packet.Salt, decoded.Salt);
        SignedCommandArgument argument = Assert.Single(decoded.ArgumentSignatures);
        Assert.Equal("message", argument.Name);
        Assert.Equal(signature, argument.Signature);

        // Differential vs 1.19.1: that era appends the last-seen collection count and the absent-last-received flag, so the same packet is exactly two bytes longer.
        byte[] v2 = CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_1, packet);
        Assert.Equal(expected.Length + 2, v2.Length);
        Assert.NotEqual(expected, v2);
    }

    [Fact]
    public void ChatCommandV1_19_1_PinnedFrame()
    {
        byte[] signature = CommandSignature();
        ServerboundSignedChatCommandPacket packet = PinnedSignedCommand();

        byte[] expected = Build(w =>
        {
            WriteV1CommandBody(w, signature);
            w.WriteVarInt(0);     // LastSeenMessages entries: none acknowledged
            w.WriteBool(false);   // no last-received entry
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_1, packet));

        ServerboundSignedChatCommandPacket decoded =
            CodecRoundTrip.Decode(ChatCommandCodecs.SignedV1_19_1, expected);
        Assert.Equal(packet.Command, decoded.Command);
        Assert.Equal(signature, Assert.Single(decoded.ArgumentSignatures).Signature);
    }

    /// <summary>The 1.19.1/1.19.2 window is a LIST of (uuid, signature) entries plus an optional last-received entry, not the 1.19.3 offset/bitset. This decodes a frame that carries a NON-EMPTY window through the frame-exact decoder: any other framing (a 1.19.3 offset + 3-byte bitset, or stopping at the preview flag) leaves bytes behind or runs off the end, so the frame-exact assertion in <see cref="CodecRoundTrip.Decode"/> catches it.</summary>
    [Fact]
    public void ChatCommandV1_19_1_DecodesNonEmptyLastSeenWindow()
    {
        byte[] signature = CommandSignature();
        Guid first = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Guid second = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
        byte[] seen = [0xDE, 0xAD, 0xBE, 0xEF];

        byte[] frame = Build(w =>
        {
            WriteV1CommandBody(w, signature);
            w.WriteVarInt(2);          // two acknowledged entries
            w.WriteUuid(first);
            w.WriteByteArray(seen);
            w.WriteUuid(second);
            w.WriteByteArray(seen);
            w.WriteBool(true);         // a last-received entry IS present
            w.WriteUuid(first);
            w.WriteByteArray(seen);
        });

        ServerboundSignedChatCommandPacket decoded =
            CodecRoundTrip.Decode(ChatCommandCodecs.SignedV1_19_1, frame);
        Assert.Equal(PinnedCommand, decoded.Command);
        Assert.Equal(signature, Assert.Single(decoded.ArgumentSignatures).Signature);
    }

    [Fact]
    public void ChatCommandV1_19_3_PinnedFrame()
    {
        byte[] signature = CommandSignature();
        ServerboundSignedChatCommandPacket packet = PinnedSignedCommand();

        byte[] expected = Build(w =>
        {
            w.WriteString(PinnedCommand, 256);      // writeUtf(command, 256)
            w.WriteLong(PinnedCommandTimestamp);    // writeInstant -> epoch millis
            w.WriteLong(PinnedCommandSalt);         // writeLong(salt)
            w.WriteVarInt(1);                       // one argument signature
            w.WriteString("message", 16);           // argument name, utf(16)
            w.WriteBytes(signature);                // fixed 256-byte MessageSignature, NO length prefix
            w.WriteVarInt(3);                       // last-seen offset
            w.WriteBytes([0x05, 0x00, 0x00]);       // fixed 20-bit ack bitset (3 bytes)
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_3, packet));

        ServerboundSignedChatCommandPacket decoded =
            CodecRoundTrip.Decode(ChatCommandCodecs.SignedV1_19_3, expected);
        Assert.Equal(packet.Command, decoded.Command);
        Assert.Equal(packet.Salt, decoded.Salt);
        Assert.Equal(signature, Assert.Single(decoded.ArgumentSignatures).Signature);
        Assert.Equal(3, decoded.LastSeen.Offset);
        Assert.Equal(new byte[] { 0x05, 0x00, 0x00 }, decoded.LastSeen.Acknowledged);
        Assert.Equal(0, decoded.LastSeen.Checksum);   // no checksum on the wire before 1.21.5

        // Differential vs 1.19.1: that era length-prefixes the signature, keeps the preview flag and frames the window as a list, so the same packet does not encode the same bytes.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_1, packet));
    }

    [Fact]
    public void ChatCommandUnsignedV1_20_5_PinnedFrame()
    {
        var packet = new ServerboundChatCommandPacket(PinnedCommand);

        // The unsigned frame contains only the UTF-8 command string.
        byte[] expected = Build(w => w.WriteString(PinnedCommand));

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.UnsignedV1_20_5, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(ChatCommandCodecs.UnsignedV1_20_5, expected));

        // The whole frame is the string: the pre-split era carried a timestamp, salt, signatures and an ack window after it, so the unsigned form is strictly shorter than the 1.19.3 chat_command.
        Assert.Equal(PinnedCommand.Length + 1, expected.Length);
        Assert.True(
            expected.Length < CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_3, PinnedSignedCommand()).Length);
    }

    [Fact]
    public void ChatCommandSignedV1_20_5_PinnedFrame()
    {
        byte[] signature = CommandSignature();
        ServerboundChatCommandSignedPacket packet = PinnedStandaloneCommand();

        byte[] expected = Build(w =>
        {
            w.WriteString(PinnedCommand);           // writeUtf(command) -> the 32767 default cap
            w.WriteLong(PinnedCommandTimestamp);    // writeInstant -> epoch millis
            w.WriteLong(PinnedCommandSalt);         // writeLong(salt)
            w.WriteVarInt(1);                       // one argument signature
            w.WriteString("message", 16);
            w.WriteBytes(signature);                // fixed 256-byte MessageSignature
            w.WriteVarInt(3);                       // last-seen offset
            w.WriteBytes([0x05, 0x00, 0x00]);       // fixed 20-bit ack bitset
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedStandaloneV1_20_5, packet));

        ServerboundChatCommandSignedPacket decoded =
            CodecRoundTrip.Decode(ChatCommandCodecs.SignedStandaloneV1_20_5, expected);
        Assert.Equal(packet.Command, decoded.Command);
        Assert.Equal(packet.TimestampMillis, decoded.TimestampMillis);
        Assert.Equal(signature, Assert.Single(decoded.ArgumentSignatures).Signature);
        Assert.Equal(3, decoded.LastSeen.Offset);

        // Differential vs 1.21.5: that era appends the last-seen checksum byte.
        byte[] withChecksum = CodecRoundTrip.Encode(ChatCommandCodecs.SignedStandaloneV1_21_5, packet);
        Assert.Equal(expected.Length + 1, withChecksum.Length);
        Assert.NotEqual(expected, withChecksum);
    }

    [Fact]
    public void ChatCommandSignedV1_21_5_PinnedFrame()
    {
        byte[] signature = CommandSignature();
        ServerboundChatCommandSignedPacket packet = PinnedStandaloneCommand();

        byte[] expected = Build(w =>
        {
            w.WriteString(PinnedCommand);
            w.WriteLong(PinnedCommandTimestamp);
            w.WriteLong(PinnedCommandSalt);
            w.WriteVarInt(1);
            w.WriteString("message", 16);
            w.WriteBytes(signature);
            w.WriteVarInt(3);                       // last-seen offset
            w.WriteBytes([0x05, 0x00, 0x00]);       // fixed 20-bit ack bitset
            w.WriteByte(0x2A);                      // last-seen checksum (added at 1.21.5)
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedStandaloneV1_21_5, packet));

        ServerboundChatCommandSignedPacket decoded =
            CodecRoundTrip.Decode(ChatCommandCodecs.SignedStandaloneV1_21_5, expected);
        Assert.Equal(0x2A, decoded.LastSeen.Checksum);
        Assert.Equal(signature, Assert.Single(decoded.ArgumentSignatures).Signature);
    }

    // chat_ack (1.19.3+ / v3): offset VarInt.
    [Fact]
    public void ChatAckV1_19_3_PinnedFrame()
    {
        var packet = new ServerboundChatAckPacket(42);

        byte[] expected = Build(w => w.WriteVarInt(42));

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCodecs.ChatAckV1_19_3, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(ChatCodecs.ChatAckV1_19_3, expected));
    }

    // Login start with a profile public key (1.19 / 1.19.1 signing eras):
    // 759 (v1): name UTF-8 capped at 16 characters; nullable profile key [expiry millis, key bytes, v1 sig];
    //           NO profile id.
    // 760 (v2): the same key block, then a nullable profile UUID. Each byte array is VarInt-prefixed.
    [Fact]
    public void LoginStartWithKeyV1_19_PinnedFrame()
    {
        byte[] keyDer = [0x30, 0x82, 0x01, 0x22];
        byte[] signature = [0xAA, 0xBB, 0xCC];
        var key = new ProfilePublicKeyData(0x0000018ABCDEF012L, keyDer, signature);
        var packet = new ServerboundHelloPacket("player", null) { ProfileKey = key };

        byte[] expected = Build(w =>
        {
            w.WriteString("player", 16);            // writeUtf(name, 16)
            w.WriteBool(true);                      // writeNullable: key present
            w.WriteLong(0x0000018ABCDEF012L);       // expiry epoch millis
            w.WriteVarInt(keyDer.Length);           // key byteArray length prefix
            w.WriteBytes(keyDer);
            w.WriteVarInt(signature.Length);        // v1 signature byteArray length prefix
            w.WriteBytes(signature);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(LoginCodecs.ClientHelloV1_19, packet));

        ServerboundHelloPacket decoded = CodecRoundTrip.Decode(LoginCodecs.ClientHelloV1_19, expected);
        Assert.Equal("player", decoded.Username);
        Assert.Null(decoded.ProfileId);
        Assert.NotNull(decoded.ProfileKey);
        Assert.Equal(key.ExpiresAtMillis, decoded.ProfileKey!.ExpiresAtMillis);
        Assert.Equal(key.KeyDer, decoded.ProfileKey.KeyDer);
        Assert.Equal(key.KeySignature, decoded.ProfileKey.KeySignature);

        // Offline fallback: no key writes a single absent flag after the name (unsigned path preserved).
        byte[] unsigned = CodecRoundTrip.Encode(LoginCodecs.ClientHelloV1_19, new ServerboundHelloPacket("player", null));
        Assert.Equal(Build(w => { w.WriteString("player", 16); w.WriteBool(false); }), unsigned);

        // Differential: the 1.19.1 (v2) codec appends a trailing absent-profile-id flag for the same packet.
        byte[] v2 = CodecRoundTrip.Encode(LoginCodecs.ClientHelloV1_19_1, packet);
        Assert.Equal(expected.Length + 1, v2.Length);
        Assert.NotEqual(expected, v2);
    }

    [Fact]
    public void LoginStartWithKeyV1_19_1_PinnedFrame()
    {
        Guid profileId = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
        byte[] keyDer = [0x30, 0x82, 0x01, 0x22];
        byte[] signature = [0xAA, 0xBB, 0xCC];
        var key = new ProfilePublicKeyData(0x0000018ABCDEF012L, keyDer, signature);
        var packet = new ServerboundHelloPacket("player", profileId) { ProfileKey = key };

        byte[] expected = Build(w =>
        {
            w.WriteString("player", 16);            // writeUtf(name, 16)
            w.WriteBool(true);                      // key present
            w.WriteLong(0x0000018ABCDEF012L);       // expiry epoch millis
            w.WriteVarInt(keyDer.Length);
            w.WriteBytes(keyDer);
            w.WriteVarInt(signature.Length);        // v2 signature bytes
            w.WriteBytes(signature);
            w.WriteBool(true);                      // writeNullable(UUID): profile id present
            w.WriteUuid(profileId);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(LoginCodecs.ClientHelloV1_19_1, packet));

        ServerboundHelloPacket decoded = CodecRoundTrip.Decode(LoginCodecs.ClientHelloV1_19_1, expected);
        Assert.Equal(profileId, decoded.ProfileId);
        Assert.NotNull(decoded.ProfileKey);
        Assert.Equal(key.KeyDer, decoded.ProfileKey!.KeyDer);

        // Differential: the 1.19 (v1) codec has no profile-id field, so it omits the trailing bool+uuid.
        byte[] v1 = CodecRoundTrip.Encode(LoginCodecs.ClientHelloV1_19, packet);
        Assert.NotEqual(expected, v1);
        Assert.Equal(expected.Length - 17, v1.Length); // dropped bool(1) + uuid(16)
    }

    // Login encryption response on the 759/760 signing eras:
    // Length-prefixed key bytes; then a branch discriminator. True selects a plain length-prefixed nonce;
    // false selects a salt long and a length-prefixed signature. The discriminator is UNCONDITIONAL on these two protocols: a login without a profile key still gets the leading discriminator boolean, always true. Omitting it causes a 1.19 or 1.19.2 decoder to lose alignment while reading the packet.
    [Fact]
    public void KeyResponseV1_19_PlainBranch_PinnedFrame()
    {
        var packet = new ServerboundKeyPacket([0x11, 0x22], [0x33]);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(2);
            w.WriteBytes([0x11, 0x22]);   // keybytes (shared secret)
            w.WriteBool(true);            // Either: left (no profile key was presented at hello)
            w.WriteVarInt(1);
            w.WriteBytes([0x33]);         // plain (RSA-encrypted) verify token
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(LoginCodecs.KeyV1_19, packet));

        ServerboundKeyPacket decoded = CodecRoundTrip.Decode(LoginCodecs.KeyV1_19, expected);
        Assert.Equal(new byte[] { 0x11, 0x22 }, decoded.SharedSecret);
        Assert.Equal(new byte[] { 0x33 }, decoded.VerifyToken);
        Assert.Null(decoded.SignedChallenge);

        // Differential: every other protocol's codec has no discriminator, so the identical packet is one byte shorter. This byte distinguishes the signed challenge form on 759 and 760.
        byte[] plain = CodecRoundTrip.Encode(LoginCodecs.Key, packet);
        Assert.NotEqual(expected, plain);
        Assert.Equal(expected.Length - 1, plain.Length);
    }

    [Fact]
    public void KeyResponseV1_19_SignedChallengeBranch_PinnedFrame()
    {
        var challenge = new SignedChallengeData(0x0000018ABCDEF012L, [0xAA, 0xBB, 0xCC]);
        var packet = new ServerboundKeyPacket([0x11, 0x22], []) { SignedChallenge = challenge };

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(2);
            w.WriteBytes([0x11, 0x22]);             // keybytes (shared secret)
            w.WriteBool(false);                     // Either: right (a profile key was presented)
            w.WriteLong(0x0000018ABCDEF012L);       // salt
            w.WriteVarInt(3);
            w.WriteBytes([0xAA, 0xBB, 0xCC]);       // signature
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(LoginCodecs.KeyV1_19, packet));

        ServerboundKeyPacket decoded = CodecRoundTrip.Decode(LoginCodecs.KeyV1_19, expected);
        Assert.Equal(new byte[] { 0x11, 0x22 }, decoded.SharedSecret);
        Assert.NotNull(decoded.SignedChallenge);
        Assert.Equal(challenge.Salt, decoded.SignedChallenge!.Salt);
        Assert.Equal(challenge.Signature, decoded.SignedChallenge.Signature);

        // Differential against the plain branch: same shared secret, different discriminator and shape.
        byte[] plainBranch = CodecRoundTrip.Encode(LoginCodecs.KeyV1_19, new ServerboundKeyPacket([0x11, 0x22], [0x33]));
        Assert.NotEqual(expected, plainBranch);
    }

    // player_command / leave bed. One wire form on every protocol from 1.8 to 26.2. The player-command frame contains id, action, and data VarInts. The 1.8 form has the same three VarInts. STOP_SLEEPING is the third enum constant in both eras, so its ordinal is 2:
    // 1.8   {START_SNEAKING, STOP_SNEAKING, STOP_SLEEPING, START_SPRINTING, STOP_SPRINTING, ...}
    // 1.9+  {PRESS_SHIFT_KEY, RELEASE_SHIFT_KEY, STOP_SLEEPING, START_SPRINTING, STOP_SPRINTING, ...}
    [Fact]
    public void PlayerCommandLeaveBed_PinnedFrame()
    {
        var packet = new ServerboundPlayerCommandPacket(EntityId: 42, Action: 2, Data: 0);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(42);   // entity id
            w.WriteVarInt(2);    // writeEnum(action) -> STOP_SLEEPING = 2
            w.WriteVarInt(0);    // data (jump boost / horse inventory), unused for leave bed
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerCommand, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(EntityServerboundCodecs.PlayerCommand, expected));

        // Differential against the neighbouring actions this surface already sends, so a wrong ordinal cannot pass: sneak is 0/1 and sprint is 3/4, none of which encode as the leave-bed frame.
        foreach (int other in new[] { 0, 1, 3, 4 })
            Assert.NotEqual(
                expected,
                CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerCommand, packet with { Action = other }));

    }

    /// <summary>The leave-bed frame is byte-identical on every era band, resolved through the real registration table rather than by reaching for the codec directly: 1.8, the 1.12 recipe-book era, 1.16.2, the 1.21.2 recipe-book split and 26.2 all encode the same three VarInts, which is why the action needs no era guard.</summary>
    [Theory]
    [InlineData("V1_8")]
    [InlineData("V1_12")]
    [InlineData("V1_16_2")]
    [InlineData("V1_21_2")]
    [InlineData("V26_2")]
    public void PlayerCommandLeaveBed_SameFrameOnEveryWireLayout(string key)
    {
        var version = new GameVersion(GameEdition.Java, "test", CodecKeyProtocols.Of(key));
        var builder = new ProtocolDescriptorBuilder(version, new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Serverbound, 0, "minecraft:player_command");
        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound)
            .TryGetInbound(0, out BoundPacketCodec entry));
        Assert.True(entry.IsImplemented, $"minecraft:player_command resolved to a marker under key {key}");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        entry.Encode(ref writer, new ServerboundPlayerCommandPacket(42, 2, 0), PacketCodecContext.Registryless);
        Assert.Equal(new byte[] { 0x2A, 0x02, 0x00 }, buffer.WrittenSpan.ToArray());
    }
}
