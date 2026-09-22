using System.Text;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>Cross-era framing checks for several packet families. Every assertion resolves through <see cref="BoundCodec"/>, which asks the registrar what it ACTUALLY binds at a protocol number.</summary>
/// <remarks>
/// Three things this suite deliberately does NOT rely on:
/// <list type="bullet">
/// <item>a plain round trip. Encode and decode through the same wrong codec agree with each other, which
/// can hide an era mismatch. Each item therefore carries a frame LENGTH or a field-position assertion against a hand-built literal frame.</item>
/// <item>the registration fixture. It records identifier and marker/codec status, not WHICH codec, so a
/// green pin says nothing about any of this. Only two of the items below move the fixture at all.</item>
/// <item>empty or default values. An absent optional and an empty list encode identically under a right
/// and a wrong framing; every value here is non-empty and era-distinguishing.</item>
/// </list>
/// </remarks>
public sealed class MisboundCodecFramingTests
{
    /// <summary>Every supported protocol number, in wire order.</summary>
    private static readonly int[] AllProtocols =
    [
        47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404,
        477, 480, 485, 490, 498, 573, 575, 578,
        735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763,
        764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777,
    ];

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => AllProtocols;

    private static TheoryData<int> Range(int min, int max)
    {
        var data = new TheoryData<int>();
        foreach (int protocol in AllProtocols.Where(p => p >= min && p <= max))
            data.Add(protocol);

        return data;
    }

    // Serverbound resource-pack response. The uuid the modern codec writes arrives at 765. Obligation flavour: a server running require-resource-pack kicks a client that never answers, and an answer in the wrong shape is not much better than none.

    /// <summary>Protocols whose resource-pack response is a bare action ordinal under the modern record.</summary>
    public static TheoryData<int> BareResponseProtocols => Range(477, 764);

    /// <summary>Protocols whose resource-pack response carries the pack uuid ahead of the action.</summary>
    public static TheoryData<int> UuidResponseProtocols => Range(765, 777);

    [Theory]
    [MemberData(nameof(BareResponseProtocols))]
    public void ServerResourcePack_Is_ExactlyTheActionOrdinal_On477To764(int protocol)
    {
        BoundPacketCodec bound = Sb(protocol, "resource_pack");

        // The frame LENGTH is the whole point: the uuid-carrying codec produces 17 bytes here and a round trip through it would still have agreed with itself.
        byte[] frame = bound.Encode(new ServerboundResourcePackPacket(SampleUuid, ResourcePackAction.Accepted));
        Assert.Equal([0x03], frame);

        var decoded = Assert.IsType<ServerboundResourcePackPacket>(bound.DecodeFrame([0x01]));
        Assert.Equal(ResourcePackAction.Declined, decoded.Action);
        Assert.Equal(Guid.Empty, decoded.Id);
    }

    [Theory]
    [MemberData(nameof(UuidResponseProtocols))]
    public void ServerResourcePack_CarriesTheUuid_From765(int protocol)
    {
        BoundPacketCodec bound = Sb(protocol, "resource_pack");
        byte[] frame = bound.Encode(new ServerboundResourcePackPacket(SampleUuid, ResourcePackAction.Accepted));

        Assert.Equal(17, frame.Length);
        Assert.Equal(Uuid(SampleUuid), frame[..16]);
        Assert.Equal(0x03, frame[16]);
    }

    [Fact]
    public void ServerResourcePack_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        byte[] uuidForm = Cat(Uuid(SampleUuid), [0x03]);
        byte[] bareForm = [0x03];

        AssertRejects(Sb(764, "resource_pack"), uuidForm, "1.20.2 reads one VarInt and nothing else");
        AssertRejects(Sb(477, "resource_pack"), uuidForm, "1.14 reads one VarInt and nothing else");
        AssertRejects(Sb(765, "resource_pack"), bareForm, "1.20.3 needs 16 uuid bytes before the action");
        AssertRejects(Sb(776, "resource_pack"), bareForm, "26.2 needs 16 uuid bytes before the action");
    }

    /// <summary>The configuration-phase copy is the SAME vanilla common packet from 1.20.2 on, so the two phases have to move at the same protocol. This is the cross-check that caught the play-phase copy: the configuration one was already correct and the play one was not.</summary>
    [Theory]
    [MemberData(nameof(UuidResponseProtocols))]
    public void ServerResourcePack_PlayAndConfigurationAgree_ByteForByte(int protocol)
    {
        byte[] play = Sb(protocol, "resource_pack")
            .Encode(new ServerboundResourcePackPacket(SampleUuid, ResourcePackAction.Downloaded));
        byte[] config = ConfigBound(protocol, PacketFlow.Serverbound, "minecraft:resource_pack")
            .Encode(new ServerboundConfigResourcePackPacket(SampleUuid, (int)ResourcePackAction.Downloaded));

        Assert.Equal(play, config);
    }

    [Fact]
    public void ServerResourcePack_PlayAndConfigurationAgree_OnTheUuidLessWireLayout()
    {
        byte[] play = Sb(764, "resource_pack")
            .Encode(new ServerboundResourcePackPacket(SampleUuid, ResourcePackAction.Accepted));
        byte[] config = ConfigBound(764, PacketFlow.Serverbound, "minecraft:resource_pack")
            .Encode(new ServerboundConfigResourcePackPacket(SampleUuid, (int)ResourcePackAction.Accepted));

        Assert.Equal(play, config);
    }

    // Clientbound resource-pack requests must decode on protocols 477-764 so the client can answer.

    /// <summary>Protocols whose pack request is url + hash only.</summary>
    public static TheoryData<int> TwoStringRequestProtocols => Range(477, 754);

    /// <summary>Protocols whose pack request adds a required flag and an optional JSON prompt.</summary>
    public static TheoryData<int> PromptRequestProtocols => Range(755, 764);

    private const string PackUrl = "https://example.invalid/pack.zip";
    private const string PackHash = "0123456789abcdef0123456789abcdef01234567";

    private static byte[] TwoStringRequest => Cat(Str(PackUrl), Str(PackHash));

    private static byte[] PromptRequest =>
        Cat(Str(PackUrl), Str(PackHash), [0x01], [0x01], Str("{\"text\":\"Install the pack\"}"));

    [Theory]
    [MemberData(nameof(TwoStringRequestProtocols))]
    public void ClientResourcePack_IsUrlAndHash_On477To754(int protocol)
    {
        BoundPacketCodec bound = Cb(protocol, "resource_pack");
        var decoded = Assert.IsType<ClientboundResourcePackPushPacket>(bound.DecodeFrame(TwoStringRequest));

        Assert.Equal(PackUrl, decoded.Url);
        Assert.Equal(PackHash, decoded.Hash);
        Assert.False(decoded.Required);
        Assert.Null(decoded.Prompt);
        Assert.Equal(Guid.Empty, decoded.Id);
        Assert.Equal(TwoStringRequest, bound.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(PromptRequestProtocols))]
    public void ClientResourcePack_AddsRequiredAndJsonPrompt_On755To764(int protocol)
    {
        BoundPacketCodec bound = Cb(protocol, "resource_pack");
        var decoded = Assert.IsType<ClientboundResourcePackPushPacket>(bound.DecodeFrame(PromptRequest));

        Assert.Equal(PackUrl, decoded.Url);
        Assert.True(decoded.Required);
        Component prompt = Assert.IsType<Component>(decoded.Prompt);
        Assert.Equal("Install the pack", Assert.IsType<TextContent>(prompt.Content).Text);
        Assert.Equal(Guid.Empty, decoded.Id);

        // The prompt is a JSON STRING on this band, not network NBT. Proved structurally: the bytes after the two strings and the two present flags are a VarInt length that accounts for the whole rest of the frame, which network NBT (a tag byte then a big-endian length) cannot satisfy.
        byte[] frame = bound.Encode(decoded);
        int promptStart = TwoStringRequest.Length + 2;
        int width = VarIntWidth(frame, promptStart);
        int length = VarIntAt(frame, promptStart);
        Assert.Equal(frame.Length - promptStart - width, length);
        Assert.Contains("Install the pack", Encoding.UTF8.GetString(frame, promptStart + width, length), StringComparison.Ordinal);
    }

    [Fact]
    public void ClientResourcePack_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        AssertRejects(Cb(754, "resource_pack"), PromptRequest, "1.16.5 ends after the hash");
        AssertRejects(Cb(477, "resource_pack"), PromptRequest, "1.14 ends after the hash");
        AssertRejects(Cb(755, "resource_pack"), TwoStringRequest, "1.17 needs the required flag");
        AssertRejects(Cb(764, "resource_pack"), TwoStringRequest, "1.20.2 needs the required flag");
    }

    /// <summary>47-404 keep the legacy record: the band covered here starts at 477.</summary>
    [Theory]
    [MemberData(nameof(LegacyRequestProtocols))]
    public void ClientResourcePack_KeepsTheLegacyRecord_On47To404(int protocol)
    {
        var decoded = Assert.IsType<ClientboundLegacyResourcePackPacket>(
            Cb(protocol, "resource_pack").DecodeFrame(TwoStringRequest));
        Assert.Equal(PackUrl, decoded.Url);
    }

    /// <summary>Protocols carrying the pre-1.14 two-string pack request under the legacy record.</summary>
    public static TheoryData<int> LegacyRequestProtocols => Range(47, 404);

    /// <summary>The 765+ split carries the pack uuid, and it lives under a different identifier. Pinned so the 755-764 member above can never be mistaken for the modern push wire.</summary>
    [Theory]
    [MemberData(nameof(UuidResponseProtocols))]
    public void ResourcePackPush_CarriesTheUuid_From765(int protocol)
    {
        byte[] frame = Cb(protocol, "resource_pack_push")
            .Encode(new ClientboundResourcePackPushPacket(SampleUuid, PackUrl, PackHash, Required: true, Prompt: null));

        Assert.Equal(Uuid(SampleUuid), frame[..16]);
        Assert.Equal(Cat(Uuid(SampleUuid), Str(PackUrl), Str(PackHash), [0x01], [0x00]), frame);
    }

    // Cooldown. Per-ITEM through 767, per-GROUP from 768.

    /// <summary>Protocols whose cooldown names an item by registry id.</summary>
    public static TheoryData<int> ItemCooldownProtocols => Range(107, 767);

    /// <summary>Protocols whose cooldown names a cooldown group by identifier.</summary>
    public static TheoryData<int> GroupCooldownProtocols => Range(768, 777);

    private static byte[] ItemCooldownFrame => Cat(VarInt(280), VarInt(40));

    private static byte[] GroupCooldownFrame => Cat(Str("minecraft:hammer"), VarInt(40));

    [Theory]
    [MemberData(nameof(ItemCooldownProtocols))]
    public void Cooldown_IsAVarIntItemId_Through767(int protocol)
    {
        BoundPacketCodec bound = Cb(protocol, "cooldown");
        var decoded = Assert.IsType<ClientboundCooldownPacket>(bound.DecodeFrame(ItemCooldownFrame));

        Assert.Equal(280, decoded.ItemId);
        Assert.Equal(40, decoded.Ticks);
        Assert.Equal(3, ItemCooldownFrame.Length);
        Assert.Equal(ItemCooldownFrame, bound.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(GroupCooldownProtocols))]
    public void Cooldown_IsAGroupIdentifier_From768(int protocol)
    {
        BoundPacketCodec bound = Cb(protocol, "cooldown");
        var decoded = Assert.IsType<ClientboundCooldownPacket>(bound.DecodeFrame(GroupCooldownFrame));

        Assert.Null(decoded.ItemId);
        Assert.Equal(Identifier.Minecraft("hammer"), decoded.CooldownGroup);
        Assert.Equal(GroupCooldownFrame, bound.Encode(decoded));
    }

    [Fact]
    public void Cooldown_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        AssertRejects(Cb(768, "cooldown"), ItemCooldownFrame, "1.21.2 reads the item id's bytes as a UTF length");
        AssertRejects(Cb(767, "cooldown"), GroupCooldownFrame, "1.21 has no identifier to read");
        AssertRejects(Cb(477, "cooldown"), GroupCooldownFrame, "1.14 has no identifier to read");
    }

    // Serverbound player_input. The steer-vehicle body runs to 767.

    /// <summary>Protocols whose player_input is the steer-vehicle body.</summary>
    public static TheoryData<int> SteerInputProtocols => Range(107, 767);

    /// <summary>Protocols whose player_input is the seven-flag byte.</summary>
    public static TheoryData<int> FlagInputProtocols => Range(768, 777);

    private static byte[] SteerInputFrame => Cat(F32(0.5f), F32(-0.25f), [0x03]);

    [Theory]
    [MemberData(nameof(SteerInputProtocols))]
    public void PlayerInput_IsTwoFloatsAndAFlagByte_Through767(int protocol)
    {
        BoundPacketCodec bound = Sb(protocol, "player_input");
        var decoded = Assert.IsType<ServerboundSteerVehiclePacket>(bound.DecodeFrame(SteerInputFrame));

        Assert.Equal(0.5f, decoded.Strafe);
        Assert.Equal(-0.25f, decoded.Forward);
        Assert.Equal(0x03, decoded.Flags);
        Assert.Equal(9, SteerInputFrame.Length);
        Assert.Equal(SteerInputFrame, bound.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(FlagInputProtocols))]
    public void PlayerInput_IsASingleFlagsByte_From768(int protocol)
    {
        BoundPacketCodec bound = Sb(protocol, "player_input");
        byte[] frame = bound.Encode(new ServerboundPlayerInputPacket(
            Forward: true, Backward: false, Left: false, Right: false, Jump: true, Shift: false, Sprint: true));

        Assert.Equal([0x51], frame);
    }

    [Fact]
    public void PlayerInput_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        AssertRejects(Sb(768, "player_input"), SteerInputFrame, "1.21.2 reads one byte and stops");
        AssertRejects(Sb(767, "player_input"), [0x51], "1.21 needs two floats before the flags");
        AssertRejects(Sb(477, "player_input"), [0x51], "1.14 needs two floats before the flags");
    }

    // Serverbound move_vehicle. The on-ground bool is a 1.21.4 addition.

    /// <summary>Protocols whose vehicle move ends after the pitch float.</summary>
    public static TheoryData<int> NoOnGroundProtocols => Range(107, 768);

    /// <summary>Protocols whose vehicle move carries the trailing on-ground bool.</summary>
    public static TheoryData<int> OnGroundProtocols => Range(769, 777);

    private static byte[] VehicleBody => Cat(F64(1.5), F64(64.0), F64(-2.5), F32(90f), F32(-10f));

    [Theory]
    [MemberData(nameof(NoOnGroundProtocols))]
    public void MoveVehicle_EndsAfterThePitch_Through768(int protocol)
    {
        BoundPacketCodec bound = Sb(protocol, "move_vehicle");
        var decoded = Assert.IsType<ServerboundMoveVehiclePacket>(bound.DecodeFrame(VehicleBody));

        Assert.Equal(32, VehicleBody.Length);
        Assert.Equal(-2.5, decoded.Z);
        Assert.False(decoded.OnGround);
        Assert.Equal(VehicleBody, bound.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(OnGroundProtocols))]
    public void MoveVehicle_CarriesOnGround_From769(int protocol)
    {
        BoundPacketCodec bound = Sb(protocol, "move_vehicle");
        byte[] frame = Cat(VehicleBody, [0x01]);
        var decoded = Assert.IsType<ServerboundMoveVehiclePacket>(bound.DecodeFrame(frame));

        Assert.Equal(33, frame.Length);
        Assert.True(decoded.OnGround);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void MoveVehicle_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        AssertRejects(Sb(768, "move_vehicle"), Cat(VehicleBody, [0x01]), "1.21.3 ends after the pitch float");
        AssertRejects(Sb(477, "move_vehicle"), Cat(VehicleBody, [0x01]), "1.14 ends after the pitch float");
        AssertRejects(Sb(769, "move_vehicle"), VehicleBody, "1.21.4 needs the on-ground bool");
    }

    // set_objective. The optional number format and the NBT display name both arrive at 765;
    // the objective name's 16-char cap goes away at 757.

    /// <summary>Protocols whose set_objective is the JSON body with no number format.</summary>
    public static TheoryData<int> JsonObjectiveProtocols => Range(393, 764);

    /// <summary>Protocols whose set_objective is the NBT body with the optional number format.</summary>
    public static TheoryData<int> NbtObjectiveProtocols => Range(765, 777);

    // A STYLED display name on purpose: a bare text component serialises to a naked JSON string and to a network-NBT TAG_String, which look alike enough to blur the boundary. With a style the JSON form is an object ('{') and the NBT form is a TAG_Compound (0x0A). No click or hover event, so the component's interaction dialect (a separate 770 boundary) cannot leak into this assertion.
    private static ClientboundSetObjectivePacket Objective { get; } = new(
        "kills",
        ScoreboardObjectiveMode.Add,
        new Component(new TextContent("Kills"), new Style { Color = TextColor.Red }),
        ObjectiveRenderType.Hearts,
        null);

    // The objective name and the mode byte are era-independent, so the display name always starts here.
    private static readonly int DisplayStart = Str("kills").Length + 1;

    [Theory]
    [MemberData(nameof(JsonObjectiveProtocols))]
    public void SetObjective_IsJsonAndHasNoNumberFormat_Through764(int protocol)
    {
        byte[] frame = Cb(protocol, "set_objective").Encode(Objective);

        // The display name is a length-prefixed JSON string.
        Assert.Equal((byte)'{', frame[DisplayStart + VarIntWidth(frame, DisplayStart)]);

        // The LAST byte is the render-type ordinal, not a number-format present flag. That is the assertion a round trip cannot make: the 1.21.5 codec appends a 0x00 here and would have round-tripped it happily.
        Assert.Equal((byte)ObjectiveRenderType.Hearts, frame[^1]);
    }

    [Theory]
    [MemberData(nameof(NbtObjectiveProtocols))]
    public void SetObjective_IsNbtAndCarriesTheNumberFormatFlag_From765(int protocol)
    {
        byte[] frame = Cb(protocol, "set_objective").Encode(Objective);

        Assert.Equal(0x0A, frame[DisplayStart]);           // TAG_Compound, network NBT
        Assert.Equal(0x00, frame[^1]);                     // number format absent
        Assert.Equal((byte)ObjectiveRenderType.Hearts, frame[^2]);
    }

    [Fact]
    public void SetObjective_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        byte[] json = Cb(764, "set_objective").Encode(Objective);
        byte[] nbt = Cb(765, "set_objective").Encode(Objective);

        Assert.NotEqual(json, nbt);
        AssertRejects(Cb(765, "set_objective"), json, "1.20.3 reads the display name as network NBT");
        AssertRejects(Cb(764, "set_objective"), nbt, "1.20.2 reads the display name as a JSON string");
        AssertRejects(Cb(477, "set_objective"), nbt, "1.14 reads the display name as a JSON string");
    }

    /// <summary>The second boundary on this packet, and the reason 757-764 needs its own member: vanilla drops the objective name's 16-char cap at 1.18, so a longer name is legal from 757 and a fault below it.</summary>
    [Fact]
    public void SetObjective_ObjectiveNameCap_MovesAt757()
    {
        var longName = new ClientboundSetObjectivePacket(
            new string('o', 20), ScoreboardObjectiveMode.Add, Component.Text("Kills"), ObjectiveRenderType.Integer, null);

        Assert.Throws<ProtocolViolationException>(() => Cb(756, "set_objective").Encode(longName));
        Assert.NotEmpty(Cb(757, "set_objective").Encode(longName));
        Assert.NotEmpty(Cb(764, "set_objective").Encode(longName));
    }

    // set_display_objective. The slot widens from a byte to a VarInt at 764, where vanilla replaced the raw int with the DisplaySlot enum.

    /// <summary>Protocols whose display slot is a byte.</summary>
    public static TheoryData<int> ByteSlotProtocols => Range(107, 763);

    /// <summary>Protocols whose display slot is a VarInt.</summary>
    public static TheoryData<int> VarIntSlotProtocols => Range(764, 777);

    [Theory]
    [MemberData(nameof(ByteSlotProtocols))]
    public void SetDisplayObjective_SlotIsOneByte_Through763(int protocol)
    {
        // Slot 200 is what separates the two framings: one byte under the era form, two under a VarInt. Vanilla never sends a slot above 18, which is exactly why this misbinding was quiet.
        byte[] frame = Cb(protocol, "set_display_objective")
            .Encode(new ClientboundSetDisplayObjectivePacket(200, "kills"));

        Assert.Equal(Cat([0xC8], Str("kills")), frame);
    }

    [Theory]
    [MemberData(nameof(VarIntSlotProtocols))]
    public void SetDisplayObjective_SlotIsAVarInt_From764(int protocol)
    {
        byte[] frame = Cb(protocol, "set_display_objective")
            .Encode(new ClientboundSetDisplayObjectivePacket(200, "kills"));

        Assert.Equal(Cat(VarInt(200), Str("kills")), frame);
    }

    [Fact]
    public void SetDisplayObjective_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        byte[] byteSlot = Cat([0xC8], Str("kills"));
        byte[] varIntSlot = Cat(VarInt(200), Str("kills"));

        AssertRejects(Cb(764, "set_display_objective"), byteSlot, "1.20.2 reads the slot as a VarInt");
        AssertRejects(Cb(763, "set_display_objective"), varIntSlot, "1.20.1 reads the slot as a byte");
        AssertRejects(Cb(477, "set_display_objective"), varIntSlot, "1.14 reads the slot as a byte");
    }

    [Fact]
    public void SetDisplayObjective_ObjectiveNameCap_MovesAt757()
    {
        var longName = new ClientboundSetDisplayObjectivePacket(1, new string('o', 20));

        Assert.Throws<ProtocolViolationException>(() => Cb(756, "set_display_objective").Encode(longName));
        Assert.NotEmpty(Cb(757, "set_display_objective").Encode(longName));
        Assert.NotEmpty(Cb(763, "set_display_objective").Encode(longName));
    }

    // The legacy join packet's dimension. 47-578 name the dimension with a signed int and carry no resource key, and the synthesized spawn info said "overworld" no matter what the int was.

    [Theory]
    [InlineData(-1, "minecraft:the_nether")]
    [InlineData(0, "minecraft:overworld")]
    [InlineData(1, "minecraft:the_end")]
    public void LegacyLogin_DerivesTheDimensionKey_On47(int dimension, string expected)
    {
        // int playerId; byte gameType; byte dimension; byte difficulty; byte maxPlayers; utf levelType;
        // bool reducedDebugInfo.
        byte[] frame = Cat(
            I32(42), [0x01], [(byte)(sbyte)dimension], [0x02], [0x14], Str("default"), [0x00]);

        var decoded = Assert.IsType<ClientboundLoginPacket>(Cb(47, "login").DecodeFrame(frame));
        Assert.Equal(expected, decoded.SpawnInfo.Dimension);
        Assert.Equal(dimension, (int?)decoded.Legacy?.Dimension);
    }

    [Theory]
    [InlineData(-1, "minecraft:the_nether")]
    [InlineData(0, "minecraft:overworld")]
    [InlineData(1, "minecraft:the_end")]
    public void LegacyLogin_DerivesTheDimensionKey_On393(int dimension, string expected)
    {
        // int playerId; byte gameType; int dimension; byte difficulty; byte maxPlayers; utf levelType;
        // bool reducedDebugInfo.
        byte[] frame = Cat(I32(42), [0x01], I32(dimension), [0x02], [0x14], Str("default"), [0x00]);

        var decoded = Assert.IsType<ClientboundLoginPacket>(Cb(393, "login").DecodeFrame(frame));
        Assert.Equal(expected, decoded.SpawnInfo.Dimension);
    }

    [Theory]
    [InlineData(-1, "minecraft:the_nether")]
    [InlineData(0, "minecraft:overworld")]
    [InlineData(1, "minecraft:the_end")]
    public void LegacyLogin_DerivesTheDimensionKey_On477(int dimension, string expected)
    {
        // int playerId; byte gameType; int dimension; byte maxPlayers; utf levelType; varint chunkRadius;
        // bool reducedDebugInfo.
        byte[] frame = Cat(I32(42), [0x01], I32(dimension), [0x14], Str("default"), VarInt(10), [0x00]);

        var decoded = Assert.IsType<ClientboundLoginPacket>(Cb(477, "login").DecodeFrame(frame));
        Assert.Equal(expected, decoded.SpawnInfo.Dimension);
    }

    [Theory]
    [InlineData(-1, "minecraft:the_nether")]
    [InlineData(1, "minecraft:the_end")]
    public void LegacyLogin_DerivesTheDimensionKey_On573(int dimension, string expected)
    {
        // 1.15 adds the long seed after the dimension and a trailing show-death-screen bool.
        byte[] frame = Cat(
            I32(42), [0x01], I32(dimension), I64(1234), [0x14], Str("default"), VarInt(10), [0x00], [0x01]);

        var decoded = Assert.IsType<ClientboundLoginPacket>(Cb(573, "login").DecodeFrame(frame));
        Assert.Equal(expected, decoded.SpawnInfo.Dimension);
    }

    // map_item_data, the packet half. 477-776 was ONE binding on the 1.21.5 codec, which reads no tracking-position bool and an OPTIONAL icon list. 1.14-1.16.5 has both bools and a bare VarInt-counted list, and the icon display name is JSON through 764. The MapData model and the applier wiring live elsewhere; this is the wire.

    /// <summary>Protocols whose map packet carries the tracking-position bool and a bare icon list.</summary>
    public static TheoryData<int> TrackingMapProtocols => Range(477, 754);

    /// <summary>Protocols whose map packet has no tracking-position bool and a JSON icon name.</summary>
    public static TheoryData<int> JsonNameMapProtocols => Range(755, 764);

    /// <summary>Protocols whose map packet carries a network-NBT icon name.</summary>
    public static TheoryData<int> NbtNameMapProtocols => Range(765, 777);

    // A NAMED icon: an unnamed one encodes a single absent-flag byte under every era and would not tell the component transports apart. Styled for the same reason the objective display name is.
    private static Component IconName { get; } =
        new(new TextContent("Home"), new Style { Color = TextColor.Red });

    private static ClientboundMapItemDataPacket MapPacket(bool? trackingPosition) => new(
        MapId: 3,
        Scale: 2,
        Locked: false,
        Icons: [new MapIcon(9, 10, -20, 12, IconName)],
        Patch: new MapPatch(0, 0, 0, 0, []),
        TrackingPosition: trackingPosition);

    [Theory]
    [MemberData(nameof(TrackingMapProtocols))]
    public void MapItemData_CarriesBothBoolsAndABareIconList_On477To754(int protocol)
    {
        BoundPacketCodec bound = Cb(protocol, "map_item_data");
        byte[] frame = bound.Encode(MapPacket(trackingPosition: true));

        Assert.Equal(3, frame[0]);      // map id
        Assert.Equal(2, frame[1]);      // scale
        Assert.Equal(1, frame[2]);      // tracking position (true)
        Assert.Equal(0, frame[3]);      // locked (false)
        Assert.Equal(1, frame[4]);      // icon COUNT, not a present flag
        Assert.Equal(9, frame[5]);      // icon type
        Assert.Equal(10, frame[6]);
        Assert.Equal(0xEC, frame[7]);   // z = -20
        Assert.Equal(12, frame[8]);     // rotation
        Assert.Equal(1, frame[9]);      // display name present

        var decoded = Assert.IsType<ClientboundMapItemDataPacket>(bound.DecodeFrame(frame));
        Assert.True(decoded.TrackingPosition);
        Assert.False(decoded.Locked);
        Assert.Equal(9, Assert.Single(Assert.IsType<MapIcon[]>(decoded.Icons, exactMatch: false)).Type);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(JsonNameMapProtocols))]
    public void MapItemData_DropsTrackingAndWrapsTheIconList_On755To764(int protocol)
    {
        BoundPacketCodec bound = Cb(protocol, "map_item_data");
        byte[] frame = bound.Encode(MapPacket(trackingPosition: null));

        Assert.Equal(3, frame[0]);      // map id
        Assert.Equal(2, frame[1]);      // scale
        Assert.Equal(0, frame[2]);      // locked (false) - the tracking bool is gone
        Assert.Equal(1, frame[3]);      // icon list PRESENT flag
        Assert.Equal(1, frame[4]);      // icon count
        Assert.Equal(1, frame[9]);      // display name present

        // The name is a length-prefixed JSON object on this band, not network NBT.
        Assert.Equal((byte)'{', frame[10 + VarIntWidth(frame, 10)]);

        var decoded = Assert.IsType<ClientboundMapItemDataPacket>(bound.DecodeFrame(frame));
        Assert.Null(decoded.TrackingPosition);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(NbtNameMapProtocols))]
    public void MapItemData_IconNameIsNetworkNbt_From765(int protocol)
    {
        BoundPacketCodec bound = Cb(protocol, "map_item_data");
        byte[] frame = bound.Encode(MapPacket(trackingPosition: null));

        Assert.Equal(0, frame[2]);      // locked
        Assert.Equal(1, frame[3]);      // icon list present
        Assert.Equal(1, frame[9]);      // display name present
        Assert.Equal(0x0A, frame[10]);  // TAG_Compound

        var decoded = Assert.IsType<ClientboundMapItemDataPacket>(bound.DecodeFrame(frame));
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The two 1.14/1.17 head layouts are the same WIDTH when the icon list is present (tracking+locked against locked+present-flag), which is exactly why the misbinding survived every round trip: only the meaning of two bytes changes. The absence of the icon list is what separates them on the wire, so that is what this checks.</summary>
    [Fact]
    public void MapItemData_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        byte[] emptyIconList = Cb(754, "map_item_data").Encode(MapPacket(trackingPosition: true) with { Icons = [] });
        byte[] absentIconList = Cb(755, "map_item_data").Encode(MapPacket(trackingPosition: null) with { Icons = null });
        byte[] jsonName = Cb(764, "map_item_data").Encode(MapPacket(trackingPosition: null));
        byte[] nbtName = Cb(765, "map_item_data").Encode(MapPacket(trackingPosition: null));

        AssertRejects(Cb(755, "map_item_data"), emptyIconList, "1.17 has no tracking-position bool to read");
        AssertRejects(Cb(770, "map_item_data"), emptyIconList, "1.21.5 has no tracking-position bool to read");
        AssertRejects(Cb(477, "map_item_data"), absentIconList, "1.14 has no present flag and must read a count");
        AssertRejects(Cb(754, "map_item_data"), absentIconList, "1.16.5 has no present flag and must read a count");
        AssertRejects(Cb(764, "map_item_data"), nbtName, "1.20.2 reads the icon name as a JSON string");
        AssertRejects(Cb(765, "map_item_data"), jsonName, "1.20.3 reads the icon name as network NBT");
    }

    // edit_book. 393-754 was a 17-protocol marker because only the 1.17 page-list form was modeled. Four wire generations, three of them the item-stack shape.

    /// <summary>Protocols whose edit_book is the present-id stack + bool + trailing VarInt.</summary>
    public static TheoryData<int> PresentIdBookProtocols => Range(404, 754);

    // A NON-EMPTY book: the empty stack is a single 0x00 byte under the present-id form and a -1 short under the short-id form, which is exactly the pair an empty-stack round trip cannot tell apart.
    private static ItemStack Book { get; } =
        new(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1);

    [Fact]
    public void EditBook_On393_IsTheStackAndTheSigningBoolAndNothingElse()
    {
        BoundPacketCodec bound = Sb(393, "edit_book");
        byte[] frame = bound.Encode(new ServerboundLegacyEditBookPacket(Book, Signing: true, HandOrSlot: null));

        // Short-id stack: a big-endian short id, a count byte, then a bare TAG_End for the absent NBT.
        Assert.Equal(5, frame.Length);
        Assert.Equal(0x01, frame[^1]); // the signing bool is the last byte: no hand follows

        var decoded = Assert.IsType<ServerboundLegacyEditBookPacket>(bound.DecodeFrame(frame));
        Assert.True(decoded.Signing);
        Assert.Null(decoded.HandOrSlot);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void EditBook_On401_AppendsTheHandVarInt_OnTheSameShortIdStack()
    {
        BoundPacketCodec bound = Sb(401, "edit_book");
        byte[] frame = bound.Encode(new ServerboundLegacyEditBookPacket(Book, Signing: true, HandOrSlot: 1));

        Assert.Equal(6, frame.Length);
        Assert.Equal(0x01, frame[^2]);  // signing
        Assert.Equal(0x01, frame[^1]);  // hand

        var decoded = Assert.IsType<ServerboundLegacyEditBookPacket>(bound.DecodeFrame(frame));
        Assert.Equal(1, decoded.HandOrSlot);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(PresentIdBookProtocols))]
    public void EditBook_On404To754_IsThePresentIdStackPlusBoolAndVarInt(int protocol)
    {
        BoundPacketCodec bound = Sb(protocol, "edit_book");
        byte[] frame = bound.Encode(new ServerboundLegacyEditBookPacket(Book, Signing: false, HandOrSlot: 3));

        // Present-id stack: a present bool, a VarInt id, a count byte, then the NBT present marker.
        Assert.Equal(0x01, frame[0]);
        Assert.Equal(6, frame.Length);
        Assert.Equal(0x00, frame[^2]);  // signing = false
        Assert.Equal(0x03, frame[^1]);  // hand (through 1.16.3) or slot (1.16.4+)

        var decoded = Assert.IsType<ServerboundLegacyEditBookPacket>(bound.DecodeFrame(frame));
        Assert.Equal(3, decoded.HandOrSlot);
        Assert.False(decoded.Signing);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    [Fact]
    public void EditBook_WireLayoutsDoNotAcceptEachOthersFrames()
    {
        byte[] shortIdNoHand = Sb(393, "edit_book").Encode(new ServerboundLegacyEditBookPacket(Book, true, null));
        byte[] shortIdHand = Sb(401, "edit_book").Encode(new ServerboundLegacyEditBookPacket(Book, true, 1));
        byte[] presentId = Sb(404, "edit_book").Encode(new ServerboundLegacyEditBookPacket(Book, true, 1));
        byte[] pageList = Sb(755, "edit_book").Encode(new ServerboundEditBookPacket(4, ["page"], "Title"));

        Assert.NotEqual(shortIdHand, presentId);
        AssertRejects(Sb(393, "edit_book"), shortIdHand, "1.13 has no hand to read");
        AssertRejects(Sb(401, "edit_book"), shortIdNoHand, "1.13.1 needs the hand VarInt");
        AssertRejects(Sb(404, "edit_book"), shortIdHand, "1.13.2 reads a present flag, not a short id");
        AssertRejects(Sb(754, "edit_book"), pageList, "1.16.5 reads a stack, not a slot and page list");
        AssertRejects(Sb(755, "edit_book"), presentId, "1.17 reads a slot and a page list, not a stack");
    }

    // Literal-frame helpers. Hand-rolled on purpose: reusing the production writer would let a wrong codec agree with itself.

    private static readonly Guid SampleUuid = new("12345678-9abc-def0-1122-334455667788");

    private static byte[] VarInt(int value)
    {
        var bytes = new List<byte>();
        uint v = (uint)value;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            bytes.Add(v != 0 ? (byte)(b | 0x80) : b);
        }
        while (v != 0);
        return [.. bytes];
    }

    private static byte[] Str(string s)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(s);
        return [.. VarInt(utf8.Length), .. utf8];
    }

    private static byte[] I32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] I64(long v)
    {
        var b = new byte[8];
        for (int i = 0; i < 8; i++)
            b[i] = (byte)(v >> (56 - (8 * i)));

        return b;
    }

    private static byte[] F32(float v)
    {
        byte[] b = BitConverter.GetBytes(v);
        Array.Reverse(b);
        return b;
    }

    private static byte[] F64(double v)
    {
        byte[] b = BitConverter.GetBytes(v);
        Array.Reverse(b);
        return b;
    }

    private static byte[] Uuid(Guid g)
    {
        Span<byte> b = stackalloc byte[16];
        g.TryWriteBytes(b, bigEndian: true, out _);
        return b.ToArray();
    }

    private static byte[] Cat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (byte[] p in parts)
            all.AddRange(p);

        return [.. all];
    }

    /// <summary>The byte width of the VarInt starting at <paramref name="offset"/>.</summary>
    private static int VarIntWidth(byte[] frame, int offset)
    {
        int width = 1;
        while ((frame[offset + width - 1] & 0x80) != 0)
            width++;

        return width;
    }

    /// <summary>The value of the VarInt starting at <paramref name="offset"/>.</summary>
    private static int VarIntAt(byte[] frame, int offset)
    {
        int value = 0;
        int shift = 0;
        for (int i = offset; i < frame.Length; i++)
        {
            value |= (frame[i] & 0x7F) << shift;
            if ((frame[i] & 0x80) == 0)
                break;

            shift += 7;
        }

        return value;
    }

    private static BoundPacketCodec Cb(int protocol, string id) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:" + id);

    private static BoundPacketCodec Sb(int protocol, string id) =>
        BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:" + id);

    private static BoundPacketCodec ConfigBound(int protocol, PacketFlow flow, string identifier)
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Configuration, flow, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(ProtocolPhase.Configuration, flow).TryGetInbound(0, out BoundPacketCodec bound));
        Assert.True(bound.IsImplemented, $"{identifier} is a configuration marker at protocol {protocol}");
        return bound;
    }

    /// <summary>Asserts that a frame does NOT survive a codec: it either faults or re-encodes differently.</summary>
    private static void AssertRejects(BoundPacketCodec bound, byte[] frame, string because)
    {
        object decoded;
        try
        {
            decoded = bound.DecodeFrame(frame);
        }
        catch (Exception)
        {
            return; // faulted, which is the honest outcome
        }

        Assert.False(frame.SequenceEqual(bound.Encode(decoded)), because);
    }
}
