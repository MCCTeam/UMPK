using System.Buffers;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>
/// Byte-anchored tests for configuration-phase dialog packets on protocols 771-776.
/// <para>The clear-dialog and custom-click-action packets use the SAME wire form in play and configuration. Show-dialog does NOT: play starts with a holder VarInt, where 0 means inline and id+1 means a registry reference, while configuration carries a bare tag with NO holder id. This is identical in 1.21.6, 1.21.9, 26.1, and 26.2.</para>
/// <para>Every payload below is non-empty: an empty dialog body would encode to bytes that both the holder form and the context-free form could plausibly accept, hiding a framing mismatch.</para>
/// </summary>
public sealed class ConfigurationDialogCodecTests
{
    // A dialog body with real members, so the leading byte of the tag is unambiguous and the holder-vs-bare difference is visible in the frame.
    private static NbtCompound DialogBody()
    {
        var body = new NbtCompound();
        body.PutString("type", "minecraft:notice");
        var title = new NbtCompound();
        title.PutString("text", "Server notice");
        body.Put("title", title);
        return body;
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    // The configuration show_dialog frame is the NBT tag alone. The first byte is the tag's type byte (0x0A, TAG_Compound), NOT a holder VarInt.
    [Fact]
    public void ConfigShowDialog_IsBareTag_RawByteFrame()
    {
        NbtCompound body = DialogBody();
        byte[] expected = Build(w => w.WriteNbt(body, NbtWireFormat.JavaUnnamedRoot));

        Assert.Equal(0x0A, expected[0]);
        Assert.Equal(
            expected,
            CodecRoundTrip.Encode(ConfigurationCodecs.ShowDialog, new ClientboundConfigShowDialogPacket(body)));

        ClientboundConfigShowDialogPacket decoded = CodecRoundTrip.Decode(ConfigurationCodecs.ShowDialog, expected);
        var decodedBody = Assert.IsType<NbtCompound>(decoded.Dialog);
        Assert.Equal("minecraft:notice", decodedBody.GetString("type"));
        Assert.Equal("Server notice", Assert.IsType<NbtCompound>(decodedBody["title"]).GetString("text"));
    }

    // The whole point of the separate configuration codec: the same dialog body produces a DIFFERENT frame in the two phases, and the play frame is exactly the configuration frame with the holder VarInt 0 in front. Reusing the play codec in configuration would eat the tag's type byte as the holder id, so this pins them apart.
    [Fact]
    public void ConfigShowDialog_DiffersFromPlayShowDialog_ByTheHolderPrefix()
    {
        NbtCompound body = DialogBody();

        byte[] config = CodecRoundTrip.Encode(ConfigurationCodecs.ShowDialog, new ClientboundConfigShowDialogPacket(body));
        byte[] play = CodecRoundTrip.Encode(UiMiscCodecs.ShowDialogV1_21_6, new ClientboundShowDialogPacket(null, body));

        Assert.NotEqual(config, play);
        Assert.Equal(config.Length + 1, play.Length);
        Assert.Equal(0x00, play[0]); // the holder VarInt: 0 means "inline body follows"
        Assert.Equal(config, play[1..]);

        // And the play codec, fed the configuration frame, does not reproduce the same packet: it reads the tag's 0x0A type byte as a holder id of 10, i.e. registry entry 9.
        var misread = CodecRoundTrip.Decode(UiMiscCodecs.ShowDialogV1_21_6, config[..1]);
        Assert.Equal(9, misread.RegistryId);
        Assert.Null(misread.InlineDialog);
    }

    // A registry reference cannot be expressed in configuration (there is no registry access there), so the configuration packet carries no registry id at all and always resolves to an inline body.
    [Fact]
    public void ConfigShowDialog_RoundTripsBody()
    {
        NbtCompound body = DialogBody();
        ClientboundConfigShowDialogPacket cycled =
            CodecRoundTrip.Cycle(ConfigurationCodecs.ShowDialog, new ClientboundConfigShowDialogPacket(body));
        Assert.Equal(body, Assert.IsType<NbtCompound>(cycled.Dialog));
    }

    [Fact]
    public void ConfigClearDialog_IsEmptyFrame()
    {
        Assert.Empty(CodecRoundTrip.Encode(ConfigurationCodecs.ClearDialog, new ClientboundConfigClearDialogPacket()));
        Assert.Equal(
            CodecRoundTrip.Encode(UiMiscCodecs.ClearDialogV1_21_6, new ClientboundClearDialogPacket()),
            CodecRoundTrip.Encode(ConfigurationCodecs.ClearDialog, new ClientboundConfigClearDialogPacket()));
        Assert.NotNull(CodecRoundTrip.Decode(ConfigurationCodecs.ClearDialog, []));
    }

    // The configuration custom_click_action must carry vanilla's lengthPrefixed(65536) exactly like the play one: both phases use the same observable wire form. This guard is restated for the configuration phase so the prefix cannot be dropped on one side only.
    [Fact]
    public void ConfigCustomClickAction_CarriesLengthPrefix_RawByteFrame()
    {
        var payload = new NbtCompound();
        payload.PutString("source", "reward_menu");
        payload.PutString("choice", "yes");

        byte[] body = NbtWriter.ToArray(payload, NbtWireFormat.JavaUnnamedRoot);
        byte[] expected = Build(w =>
        {
            w.WriteString("example:claim"); // action identifier
            w.WriteByteArray(body);         // lengthPrefixed(65536) around the unnamed-root tag
        });

        Assert.Equal(
            expected,
            CodecRoundTrip.Encode(
                ConfigurationCodecs.CustomClickAction,
                new ServerboundConfigCustomClickActionPacket(Identifier.Parse("example:claim"), payload)));

        ServerboundConfigCustomClickActionPacket decoded =
            CodecRoundTrip.Decode(ConfigurationCodecs.CustomClickAction, expected);
        Assert.Equal(Identifier.Parse("example:claim"), decoded.Id);
        Assert.Equal(["source", "choice"], Assert.IsType<NbtCompound>(decoded.Payload).Keys);
    }

    // Byte-for-byte identical to the play-phase frame for the same id and payload, which is the claim that lets the two share a body.
    [Fact]
    public void ConfigCustomClickAction_MatchesPlayFrame()
    {
        var payload = new NbtCompound();
        payload.PutString("source", "reward_menu");
        Identifier id = Identifier.Parse("example:claim");

        Assert.Equal(
            CodecRoundTrip.Encode(UiMiscCodecs.CustomClickActionV1_21_6, new ServerboundCustomClickActionPacket(id, payload)),
            CodecRoundTrip.Encode(ConfigurationCodecs.CustomClickAction, new ServerboundConfigCustomClickActionPacket(id, payload)));
    }

    // An absent payload is a length of 1 carrying a bare TAG_End, never a length of 0.
    [Fact]
    public void ConfigCustomClickAction_AbsentPayload_IsLengthOneTagEnd()
    {
        byte[] expected = Build(w =>
        {
            w.WriteString("example:claim");
            w.WriteByteArray([0x00]);
        });

        Assert.Equal(
            expected,
            CodecRoundTrip.Encode(
                ConfigurationCodecs.CustomClickAction,
                new ServerboundConfigCustomClickActionPacket(Identifier.Parse("example:claim"), null)));
        Assert.Null(CodecRoundTrip.Decode(ConfigurationCodecs.CustomClickAction, expected).Payload);
    }

    // A prefix-less frame must not be accepted by the configuration codec either.
    [Fact]
    public void ConfigCustomClickAction_PrefixlessFrame_IsNotAccepted()
    {
        var payload = new NbtCompound();
        payload.PutString("source", "reward_menu");

        byte[] prefixless = Build(w =>
        {
            w.WriteString("example:claim");
            w.WriteNbt(payload, NbtWireFormat.JavaUnnamedRoot);
        });

        var reader = new PacketReader(prefixless);
        try
        {
            ConfigurationCodecs.CustomClickAction.Decode(ref reader, PacketCodecContext.Registryless);
        }
        catch (ProtocolViolationException)
        {
            return;
        }
        catch (NbtFormatException)
        {
            return;
        }

        Assert.NotEqual(0, reader.Remaining);
    }

    // All three must resolve to implemented codecs (not markers) on every configuration-phase era key from 1.21.6 on, and must stay absent before it.
    [Theory]
    [InlineData(PacketFlow.Clientbound, "minecraft:show_dialog", "V1_21_6")]
    [InlineData(PacketFlow.Clientbound, "minecraft:show_dialog", "V1_21_7")]
    [InlineData(PacketFlow.Clientbound, "minecraft:show_dialog", "V1_21_9")]
    [InlineData(PacketFlow.Clientbound, "minecraft:show_dialog", "V1_21_11")]
    [InlineData(PacketFlow.Clientbound, "minecraft:show_dialog", "V26_1")]
    [InlineData(PacketFlow.Clientbound, "minecraft:show_dialog", "V26_2")]
    [InlineData(PacketFlow.Clientbound, "minecraft:clear_dialog", "V1_21_6")]
    [InlineData(PacketFlow.Clientbound, "minecraft:clear_dialog", "V26_2")]
    [InlineData(PacketFlow.Serverbound, "minecraft:custom_click_action", "V1_21_6")]
    [InlineData(PacketFlow.Serverbound, "minecraft:custom_click_action", "V26_2")]
    public void ConfigurationDialogPackets_AreImplemented(PacketFlow flow, string identifier, string codecKey)
    {
        var version = new GameVersion(GameEdition.Java, "test", CodecKeyProtocols.Of(codecKey));
        var builder = new ProtocolDescriptorBuilder(version, new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Configuration, flow, 0x11, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(ProtocolPhase.Configuration, flow).TryGetInbound(0x11, out BoundPacketCodec entry));
        Assert.True(entry.IsImplemented, $"{identifier} must not be a configuration-phase marker on {codecKey}.");
        Assert.Equal(ProtocolPhase.Configuration, entry.Type.Phase);
    }
}
