using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Session;

/// <summary>
/// Byte-exact era pins for the play-phase client-information announce, resolved through the binding table rather than by naming a codec, so a misbound era fails here.
/// <para>Every expected frame below is hand-built field by field from the wire contract, never by running the encoder. Every value is non-empty and distinct so a dropped or reordered field changes the bytes. The assertions cover each layout transition from 1.8 through 26.2.</para>
/// </summary>
public sealed class ClientInformationCodecTests
{
    private const string Identifier = "minecraft:client_information";

    /// <summary>Distinct, non-default values so every field is observable in the bytes.</summary>
    private static ServerboundPlayClientInformationPacket Sample() =>
        new(
            Language: "fr_fr",
            ViewDistance: 12,
            ChatVisibility: 1,       // SYSTEM
            ChatColors: false,
            ModelCustomisation: 0x5A,
            MainHand: 0,             // LEFT, the non-default, so a dropped field shows
            TextFilteringEnabled: true,
            AllowsListing: false,
            ParticleStatus: 2);      // MINIMAL

    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>1.8: locale, view distance, BYTE visibility, colors, skin parts. No main hand.</summary>
    private static byte[] WireV1_8() => Build(w =>
    {
        w.WriteString("fr_fr");
        w.WriteSByte(12);
        w.WriteByte(1);
        w.WriteBool(false);
        w.WriteByte(0x5A);
    });

    /// <summary>1.9: visibility widens to a VarInt and main hand is appended.</summary>
    private static byte[] WireV1_9() => Build(w =>
    {
        w.WriteString("fr_fr");
        w.WriteSByte(12);
        w.WriteVarInt(1);
        w.WriteBool(false);
        w.WriteByte(0x5A);
        w.WriteVarInt(0);
    });

    private static byte[] WireV1_17() => Build(w =>
    {
        w.WriteString("fr_fr");
        w.WriteSByte(12);
        w.WriteVarInt(1);
        w.WriteBool(false);
        w.WriteByte(0x5A);
        w.WriteVarInt(0);
        w.WriteBool(true);
    });

    private static byte[] WireV1_18() => Build(w =>
    {
        w.WriteString("fr_fr");
        w.WriteSByte(12);
        w.WriteVarInt(1);
        w.WriteBool(false);
        w.WriteByte(0x5A);
        w.WriteVarInt(0);
        w.WriteBool(true);
        w.WriteBool(false);
    });

    private static byte[] WireV1_21_2() => Build(w =>
    {
        w.WriteString("fr_fr");
        w.WriteSByte(12);
        w.WriteVarInt(1);
        w.WriteBool(false);
        w.WriteByte(0x5A);
        w.WriteVarInt(0);
        w.WriteBool(true);
        w.WriteBool(false);
        w.WriteVarInt(2);
    });

    /// <summary>The era boundaries, each tested at the first protocol of the era and at the last protocol before the next delta, so an off-by-one in the binding table cannot hide.</summary>
    [Theory]
    [InlineData(47)]    // 1.8
    [InlineData(107)]   // 1.9
    [InlineData(340)]   // 1.12.2
    [InlineData(498)]   // 1.14.4
    [InlineData(754)]   // 1.16.5, last before text filtering
    [InlineData(755)]   // 1.17
    [InlineData(756)]   // 1.17.1, last before allows-listing
    [InlineData(757)]   // 1.18
    [InlineData(767)]   // 1.21.1, last before particle status
    [InlineData(768)]   // 1.21.2
    [InlineData(772)]
    [InlineData(776)]
    public void PlayClientInformation_IsImplemented_NotAMarker(int protocol)
    {
        // BoundCodec.At asserts IsImplemented, which is precisely what was false everywhere before.
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier);
        Assert.True(bound.IsImplemented);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(106)]
    public void ClientInformation_OmitsMainHand(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier);
        byte[] expected = WireV1_8();

        Assert.Equal(expected, bound.Encode(Sample()));

        // Frame-decode the real bytes: a round trip would agree with itself under a wrong framing.
        var decoded = Assert.IsType<ServerboundPlayClientInformationPacket>(bound.DecodeFrame(expected));
        Assert.Equal("fr_fr", decoded.Language);
        Assert.Equal(12, decoded.ViewDistance);
        Assert.Equal(1, decoded.ChatVisibility);
        Assert.False(decoded.ChatColors);
        Assert.Equal(0x5A, decoded.ModelCustomisation);

        // 1.8 carries no main hand, so the decoder defaults it to the canonical right hand.
        Assert.Equal(1, decoded.MainHand);

        // The 1.9 frame is one byte longer; feeding it here must not silently succeed.
        Assert.NotEqual(expected, WireV1_9());
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(498)]
    [InlineData(754)]
    public void ClientInformation_UsesMainHandAndVariableVisibility(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier);
        byte[] expected = WireV1_9();

        Assert.Equal(expected, bound.Encode(Sample()));

        var decoded = Assert.IsType<ServerboundPlayClientInformationPacket>(bound.DecodeFrame(expected));
        Assert.Equal(0, decoded.MainHand);
        Assert.False(decoded.TextFilteringEnabled);
        Assert.False(decoded.AllowsListing);
        Assert.Equal(0, decoded.ParticleStatus);

        // The 1.8 codec would stop one field short of the main hand.
        Assert.NotEqual(expected, WireV1_8());
    }

    [Theory]
    [InlineData(755)]
    [InlineData(756)]
    public void ClientInformation_AddsTextFiltering(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier);
        byte[] expected = WireV1_17();

        Assert.Equal(expected, bound.Encode(Sample()));

        var decoded = Assert.IsType<ServerboundPlayClientInformationPacket>(bound.DecodeFrame(expected));
        Assert.True(decoded.TextFilteringEnabled);
        Assert.False(decoded.AllowsListing);
        Assert.NotEqual(expected, WireV1_9());
        Assert.NotEqual(expected, WireV1_18());
    }

    [Theory]
    [InlineData(757)]
    [InlineData(763)]
    [InlineData(767)]
    public void ClientInformation_AddsListingPermission(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier);
        byte[] expected = WireV1_18();

        Assert.Equal(expected, bound.Encode(Sample()));

        var decoded = Assert.IsType<ServerboundPlayClientInformationPacket>(bound.DecodeFrame(expected));
        Assert.True(decoded.TextFilteringEnabled);
        Assert.False(decoded.AllowsListing);
        Assert.Equal(0, decoded.ParticleStatus);

        // Sending the 1.21.2 shape to a 1.20.x server appends a byte the vanilla server rejects.
        Assert.NotEqual(expected, WireV1_21_2());
    }

    [Theory]
    [InlineData(768)]
    [InlineData(772)]
    [InlineData(776)]
    public void ClientInformation_AddsParticleStatus(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier);
        byte[] expected = WireV1_21_2();

        Assert.Equal(expected, bound.Encode(Sample()));

        var decoded = Assert.IsType<ServerboundPlayClientInformationPacket>(bound.DecodeFrame(expected));
        Assert.Equal(2, decoded.ParticleStatus);
        Assert.Equal(Sample(), decoded);
        Assert.NotEqual(expected, WireV1_18());
    }

    /// <summary>Every field exposed by the options record must appear in the resulting packet.</summary>
    [Fact]
    public void Options_MapEveryFieldOntoThePacket()
    {
        var options = new ClientInformationOptions
        {
            Locale = "de_de",
            ViewDistance = 16,
            ChatVisibility = ChatVisibility.Hidden,
            ChatColors = false,
            DisplayedSkinParts = SkinParts.Hat | SkinParts.Cape,
            MainHand = MainHand.Left,
            TextFilteringEnabled = true,
            AllowsListing = false,
            ParticleStatus = ParticleStatus.Decreased,
        };

        ServerboundPlayClientInformationPacket play = options.ToPlayPacket();
        Assert.Equal("de_de", play.Language);
        Assert.Equal(16, play.ViewDistance);
        Assert.Equal(2, play.ChatVisibility);
        Assert.False(play.ChatColors);
        Assert.Equal(0x41, play.ModelCustomisation);   // Hat (0x40) | Cape (0x01)
        Assert.Equal(0, play.MainHand);
        Assert.True(play.TextFilteringEnabled);
        Assert.False(play.AllowsListing);
        Assert.Equal(1, play.ParticleStatus);

        // The configuration-phase projection must agree field for field; only the phase differs.
        ServerboundClientInformationPacket config = options.ToConfigurationPacket();
        Assert.Equal(play.Language, config.Language);
        Assert.Equal(play.ViewDistance, config.ViewDistance);
        Assert.Equal(play.ChatVisibility, config.ChatVisibility);
        Assert.Equal(play.ChatColors, config.ChatColors);
        Assert.Equal(play.ModelCustomisation, config.ModelCustomisation);
        Assert.Equal(play.MainHand, config.MainHand);
        Assert.Equal(play.TextFilteringEnabled, config.TextFilteringEnabled);
        Assert.Equal(play.AllowsListing, config.AllowsListing);
        Assert.Equal(play.ParticleStatus, config.ParticleStatus);
    }

    /// <summary>The defaults must reproduce exactly what the code hardcoded before, so nothing regresses.</summary>
    [Fact]
    public void Defaults_MatchThePreviousHardcodedAnnounce()
    {
        ServerboundPlayClientInformationPacket packet = new ClientInformationOptions().ToPlayPacket();

        Assert.Equal("en_us", packet.Language);
        Assert.Equal(8, packet.ViewDistance);
        Assert.Equal(0, packet.ChatVisibility);
        Assert.True(packet.ChatColors);
        Assert.Equal(0x7F, packet.ModelCustomisation);
        Assert.Equal(1, packet.MainHand);
        Assert.False(packet.TextFilteringEnabled);
        Assert.True(packet.AllowsListing);
        Assert.Equal(0, packet.ParticleStatus);
    }

    /// <summary>View distance is a signed byte on every version, so an out-of-range option must clamp rather than wrap into a negative value the server would reject.</summary>
    [Fact]
    public void ViewDistance_Clamps_RatherThanWrapping()
    {
        Assert.Equal(127, new ClientInformationOptions { ViewDistance = 400 }.ToPlayPacket().ViewDistance);
        Assert.Equal(2, new ClientInformationOptions { ViewDistance = -5 }.ToPlayPacket().ViewDistance);
    }
}
