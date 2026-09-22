using System.Text;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Binding-level pins for the connection-flow packets: the play-phase <c>disconnect</c>, the play-phase cookie/transfer common packets, <c>client_command</c>, <c>player_loaded</c>, <c>client_tick_end</c>, and the <c>container_ack</c> alias.</summary>
/// <remarks>Every assertion resolves through <see cref="BoundCodec"/>, which asks the registrar what it ACTUALLY binds at a protocol number. That is the only thing that catches this defect family: each of these packets already had a correct wire shape somewhere in the assembly, or a trivial one, and was nevertheless registered as a marker, so the frame was recognised and thrown away. A test that names a codec directly proves nothing about whether a live session ever reaches it.</remarks>
public class ConnectionObligationBindingTests
{
    /// <summary>The supported protocol numbers. Listed rather than read from the version catalog because this assembly deliberately does not reference the dataset package; the sibling conformance suite walks the real catalog and would fail first if a version were added without landing here.</summary>
    private static readonly int[] All =
    [
        47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404,
        477, 480, 485, 490, 498, 573, 575, 578,
        735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763,
        764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777,
    ];

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => All;

    /// <summary>Every supported protocol number, in order.</summary>
    public static TheoryData<int> AllProtocols => Subset(static _ => true);

    /// <summary>Protocols carrying the 1.20.5+ cookie/transfer common packets.</summary>
    public static TheoryData<int> CookieProtocols => Subset(static p => p >= 766);

    /// <summary>Protocols carrying <c>client_tick_end</c> (1.21.2+).</summary>
    public static TheoryData<int> TickEndProtocols => Subset(static p => p >= 768);

    /// <summary>Protocols carrying <c>player_loaded</c> (1.21.4+).</summary>
    public static TheoryData<int> PlayerLoadedProtocols => Subset(static p => p >= 769);

    /// <summary>Protocols spelling the window transaction <c>container_ack</c> (1.9 to 1.16.4).</summary>
    public static TheoryData<int> ContainerAckProtocols => Subset(static p => p is >= 107 and <= 754);

    private static TheoryData<int> Subset(Func<int, bool> predicate)
    {
        var data = new TheoryData<int>();
        foreach (int protocol in All.Where(predicate))
            data.Add(protocol);

        return data;
    }

    // disconnect

    /// <summary>The kick reason under test carries a click event and a hover event, so the wire actually differs between the three eras. A plain-text reason encodes the same under a legacy and a modern interaction codec, which is exactly why an era misbinding survives ordinary round-trip tests.</summary>
    private static Component KickReason { get; } = new(
        new TextContent("Banned. Appeal here."),
        new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid/appeal"),
            HoverEvent = new HoverShowText(Component.Text("opens the appeal form")),
        });

    private static bool Contains(byte[] frame, string marker) =>
        frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker).AsSpan()) >= 0;

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void PlayDisconnect_IsBound_OnEveryProtocol(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:disconnect");
        byte[] frame = bound.Encode(new ClientboundDisconnectPacket(KickReason));

        Assert.NotEmpty(frame);

        // The reason survives the live entry point and re-encodes byte-exactly.
        var decoded = Assert.IsType<ClientboundDisconnectPacket>(bound.DecodeFrame(frame));
        Assert.Equal("Banned. Appeal here.", Assert.IsType<TextContent>(decoded.Reason.Content).Text);
        Assert.NotNull(decoded.Reason.Style.ClickEvent);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The transport boundary. 47-764 write a length-prefixed JSON string (first byte is the VarInt length, and the body starts with an ASCII <c>{</c>); 765+ write network NBT, whose first byte is the TAG_Compound id 0x0A.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void PlayDisconnect_TransportBoundary_IsProtocol765(int protocol)
    {
        byte[] frame = BoundCodec
            .At(protocol, PacketFlow.Clientbound, "minecraft:disconnect")
            .Encode(new ClientboundDisconnectPacket(KickReason));

        if (protocol < 765)
            Assert.Equal((byte)'{', frame[SkipVarInt(frame)]);

        else
            Assert.Equal(0x0A, frame[0]);

    }

    /// <summary>The interaction boundary, independent of the transport one. Below 770 the reason spells <c>clickEvent</c>/<c>hoverEvent</c> and nests the hover body under <c>contents</c>; from 770 it spells <c>click_event</c>/<c>hover_event</c> and inlines the body. The marker strings are raw UTF-8 in both the JSON and the NBT transport, so one check covers all three eras.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void PlayDisconnect_InteractionBoundary_IsProtocol770(int protocol)
    {
        byte[] frame = BoundCodec
            .At(protocol, PacketFlow.Clientbound, "minecraft:disconnect")
            .Encode(new ClientboundDisconnectPacket(KickReason));

        if (protocol < 770)
        {
            Assert.True(Contains(frame, "clickEvent"), $"disconnect @ {protocol} must use the legacy clickEvent key.");
            Assert.True(Contains(frame, "hoverEvent"), $"disconnect @ {protocol} must use the legacy hoverEvent key.");
            Assert.False(Contains(frame, "click_event"), $"disconnect @ {protocol} must not use the modern click_event key.");
        }
        else
        {
            Assert.True(Contains(frame, "click_event"), $"disconnect @ {protocol} must use the modern click_event key.");
            Assert.True(Contains(frame, "hover_event"), $"disconnect @ {protocol} must use the modern hover_event key.");
            Assert.False(Contains(frame, "clickEvent"), $"disconnect @ {protocol} must not use the legacy clickEvent key.");
        }
    }

    /// <summary>The configuration-phase disconnect is vanilla's SAME common packet class, so its wire has to move at the same two boundaries.</summary>
    [Theory]
    [MemberData(nameof(CookieProtocols))]
    public void ConfigDisconnect_MatchesPlayDisconnect_ByteForByte(int protocol)
    {
        byte[] play = BoundCodec
            .At(protocol, PacketFlow.Clientbound, "minecraft:disconnect")
            .Encode(new ClientboundDisconnectPacket(KickReason));
        byte[] config = ConfigBound(protocol, PacketFlow.Clientbound, "minecraft:disconnect")
            .Encode(new ClientboundConfigDisconnectPacket(KickReason));

        Assert.Equal(play, config);
    }

    [Theory]
    [InlineData(764, false, false)]
    [InlineData(765, true, false)]
    [InlineData(769, true, false)]
    [InlineData(770, true, true)]
    [InlineData(776, true, true)]
    public void ConfigDisconnect_UsesWireLayoutOfProtocol(int protocol, bool nbtTransport, bool modernInteractions)
    {
        byte[] frame = ConfigBound(protocol, PacketFlow.Clientbound, "minecraft:disconnect")
            .Encode(new ClientboundConfigDisconnectPacket(KickReason));

        if (nbtTransport)
            Assert.Equal(0x0A, frame[0]);

        else
            Assert.Equal((byte)'{', frame[SkipVarInt(frame)]);

        Assert.Equal(modernInteractions, Contains(frame, "click_event"));
        Assert.Equal(!modernInteractions, Contains(frame, "clickEvent"));
    }

    // client_command / player_loaded / client_tick_end

    /// <summary>The respawn request, on every protocol. The body is one VarInt: 0 = perform respawn. A client that cannot send this request is stuck on the death screen for the rest of the session.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void ClientCommand_IsBound_OnEveryProtocol_AndRespawnIsOrdinalZero(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:client_command");

        Assert.Equal([0x00], bound.Encode(new ServerboundClientCommandPacket(ClientCommandAction.PerformRespawn)));
        Assert.Equal([0x01], bound.Encode(new ServerboundClientCommandPacket(ClientCommandAction.RequestStats)));

        var decoded = Assert.IsType<ServerboundClientCommandPacket>(bound.DecodeFrame([0x00]));
        Assert.Equal(ClientCommandAction.PerformRespawn, decoded.Action);
    }

    [Theory]
    [MemberData(nameof(PlayerLoadedProtocols))]
    public void PlayerLoaded_IsBound_AndEmpty(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:player_loaded");
        Assert.Empty(bound.Encode(new ServerboundPlayerLoadedPacket()));
        Assert.IsType<ServerboundPlayerLoadedPacket>(bound.DecodeFrame([]));
    }

    [Theory]
    [MemberData(nameof(TickEndProtocols))]
    public void ClientTickEnd_IsBound_AndEmpty(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:client_tick_end");
        Assert.Empty(bound.Encode(new ServerboundClientTickEndPacket()));
        Assert.IsType<ServerboundClientTickEndPacket>(bound.DecodeFrame([]));
    }

    // cookies and transfer

    [Theory]
    [MemberData(nameof(CookieProtocols))]
    public void PlayCookieAndTransfer_AreBound_AndMatchTheConfigurationWire(int protocol)
    {
        var key = Identifier.Minecraft("velocity_forwarding");
        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];

        byte[] request = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:cookie_request")
            .Encode(new ClientboundCookieRequestPacket(key));
        Assert.Equal(
            ConfigBound(protocol, PacketFlow.Clientbound, "minecraft:cookie_request")
                .Encode(new ClientboundConfigCookieRequestPacket(key)),
            request);

        byte[] response = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:cookie_response")
            .Encode(new ServerboundCookieResponsePacket(key, payload));
        Assert.Equal(
            ConfigBound(protocol, PacketFlow.Serverbound, "minecraft:cookie_response")
                .Encode(new ServerboundConfigCookieResponsePacket(key, payload)),
            response);

        byte[] store = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:store_cookie")
            .Encode(new ClientboundStoreCookiePacket(key, payload));
        Assert.Equal(
            ConfigBound(protocol, PacketFlow.Clientbound, "minecraft:store_cookie")
                .Encode(new ClientboundConfigStoreCookiePacket(key, payload)),
            store);

        byte[] transfer = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:transfer")
            .Encode(new ClientboundTransferPacket("play.example.invalid", 25565));
        Assert.Equal(
            ConfigBound(protocol, PacketFlow.Clientbound, "minecraft:transfer")
                .Encode(new ClientboundConfigTransferPacket("play.example.invalid", 25565)),
            transfer);
    }

    [Theory]
    [MemberData(nameof(CookieProtocols))]
    public void PlayCookieResponse_EncodesAnAbsentPayloadAsAFalseFlag(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:cookie_response");
        byte[] frame = bound.Encode(new ServerboundCookieResponsePacket(Identifier.Minecraft("k"), null));

        Assert.Equal(0x00, frame[^1]);
        var decoded = Assert.IsType<ServerboundCookieResponsePacket>(bound.DecodeFrame(frame));
        Assert.Null(decoded.Payload);
    }

    // container_ack

    /// <summary>The 1.9-1.16.4 datasets spell the window transaction <c>container_ack</c>; the 1.8 dataset spells the same three fields <c>transaction</c>. Without the alias the whole band decoded neither direction, so a rejected container click was invisible and could not be answered.</summary>
    [Theory]
    [MemberData(nameof(ContainerAckProtocols))]
    public void ContainerAck_ResolvesTheTransactionWire_InBothDirections(int protocol)
    {
        byte[] expected = [0x03, 0x00, 0x2A, 0x00];

        BoundPacketCodec inbound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_ack");
        Assert.Equal(expected, inbound.Encode(new ClientboundTransactionPacket(3, 42, Accepted: false)));
        var decodedIn = Assert.IsType<ClientboundTransactionPacket>(inbound.DecodeFrame(expected));
        Assert.Equal(3, decodedIn.ContainerId);
        Assert.Equal((short)42, decodedIn.ActionNumber);
        Assert.False(decodedIn.Accepted);

        BoundPacketCodec outbound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:container_ack");
        Assert.Equal([0x03, 0x00, 0x2A, 0x01], outbound.Encode(new ServerboundTransactionPacket(3, 42, Accepted: true)));
    }

    /// <summary>The 1.8 name keeps resolving the same wire, so the alias adds a name rather than moving one.</summary>
    [Fact]
    public void Transaction_StillResolves_AtProtocol47()
    {
        byte[] frame = BoundCodec.At(47, PacketFlow.Clientbound, "minecraft:transaction")
            .Encode(new ClientboundTransactionPacket(3, 42, Accepted: false));
        Assert.Equal([0x03, 0x00, 0x2A, 0x00], frame);
    }

    // helpers

    /// <summary>The bound CONFIGURATION-phase codec for an identifier at a protocol number.</summary>
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

    /// <summary>The index just past a leading VarInt (the JSON string's length prefix).</summary>
    private static int SkipVarInt(byte[] frame)
    {
        int i = 0;
        while ((frame[i] & 0x80) != 0)
            i++;

        return i + 1;
    }
}
