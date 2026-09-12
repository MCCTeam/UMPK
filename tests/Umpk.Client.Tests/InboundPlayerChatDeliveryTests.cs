using System.Buffers;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// End-to-end delivery pins for INBOUND player chat: a wire frame goes in, a <see cref="ChatMessageReceived"/> must come out.
/// <para>These drive the complete chain: the real <see cref="JavaVersion"/>, its real <see cref="ProtocolDescriptor"/>, the wire id and codec the registrar actually bound for that protocol, and the real applier chain behind <see cref="ApplierHarness"/>. A codec-level test cannot see a missing registrar binding, and an applier-level test that constructs the packet by hand cannot either. The frame has to enter as bytes and the assertion has to be on the published event.</para>
/// <para>The sender here is not on the tab list, so the session's chat-verifier resolver finds no announced profile key for them: the normal state for an offline-mode session, and for any peer whose player-info entry has not arrived. An unverifiable message must still be delivered, labelled <see cref="ChatVerification.Unverified"/>, never dropped. The verified path, where the roster does carry the peer's key, is <c>PeerChatVerificationTests</c>.</para>
/// </summary>
public sealed class InboundPlayerChatDeliveryTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");

    private const string Content = "hello from the modern band";

    /// <summary>The modern player-chat band from 1.21.5 through 26.2.</summary>
    public static TheoryData<int> ModernVersions => [770, 776];

    [Theory]
    [MemberData(nameof(ModernVersions))]
    public async Task ModernPlayerChat_Frame_Reaches_TheConsumer_WithNoVerifierConfigured(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        object packet = DecodeClientbound(version!, UiPackets.Clientbound.PlayerChat, BuildModernPlayerChatFrame());
        await harness.ApplyAsync(packet);

        Assert.NotNull(seen);
        Assert.Equal(ChatCategory.Player, seen!.Category);

        // From 1.19 the server sends the bare body and the CLIENT composes the line, so the rendered message has to name the sender. Publishing the body alone is what made a 1.19+ session show "hello from the modern band" where a 1.16.5 session on the same server showed "<Notch> hello from the modern band".
        Assert.Equal($"<Notch> {Content}", seen.Message.ToPlainText());
        Assert.Equal(Content, seen.Body.ToPlainText());
        Assert.Equal(Sender, seen.SenderId);
        Assert.Equal("Notch", seen.SenderName!.ToPlainText());
        Assert.False(seen.IsOverlay);

        // Signed, but this session has no verifier for the sender: delivered and labelled, not dropped.
        Assert.Equal(ChatVerification.Unverified, seen.Verification);
    }

    /// <summary>An unsigned player_chat (as an offline-mode server sends) is delivered too, distinguishable from a signed-but-uncheckable one.</summary>
    [Fact]
    public async Task UnsignedPlayerChat_Frame_Reaches_TheConsumer_AsInsecure()
    {
        JavaVersion version = JavaVersions.V26_2;
        var harness = new ApplierHarness(version);
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        object packet = DecodeClientbound(
            version, UiPackets.Clientbound.PlayerChat, BuildModernPlayerChatFrame(signed: false));
        await harness.ApplyAsync(packet);

        Assert.NotNull(seen);
        Assert.Equal(ChatCategory.Player, seen!.Category);
        Assert.Equal($"<Notch> {Content}", seen.Message.ToPlainText());
        Assert.Equal(Content, seen.Body.ToPlainText());
        Assert.Equal(ChatVerification.Insecure, seen.Verification);
    }

    /// <summary>The pre-signing path must stay green: 1.12.2 legacy chat is a JSON component plus a position byte, and it was never affected by the marker (it has always had a real codec). This is the independent control for the signed-chat bands.</summary>
    [Fact]
    public async Task LegacyChat_Frame_StillReaches_TheConsumer_OnPre1_19()
    {
        JavaVersion version = JavaVersions.V1_12_2;
        var harness = new ApplierHarness(version);
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteString(
            ComponentJson.ToJsonString(Component.Text("hello from 1.12.2"), ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object),
            262144);
        w.WriteByte(0); // chat position

        object packet = DecodeClientbound(
            version, PlayPackets.Clientbound.LegacyChat, buffer.WrittenSpan.ToArray());
        await harness.ApplyAsync(packet);

        Assert.NotNull(seen);
        Assert.Equal(ChatCategory.Legacy, seen!.Category);
        Assert.Equal("hello from 1.12.2", seen.Message.ToPlainText());
        Assert.Equal(ChatVerification.NotApplicable, seen.Verification);
    }

    /// <summary>Resolves the wire id and codec the registrar actually bound for a packet on this version, then decodes the frame through it exactly as the live dispatcher would (frame-exact: a trailing byte faults). Nothing here names a codec, so a marker or a misbound era fails on the spot.</summary>
    private static object DecodeClientbound(JavaVersion version, PacketType type, byte[] frame)
    {
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        Assert.True(
            registry.TryGetOutbound(type, out int wireId, out BoundPacketCodec _),
            $"{type.Id} is not registered at protocol {version.Protocol.Version.Protocol}.");
        Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
        Assert.True(
            bound.IsImplemented,
            $"{type.Id} is a marker at protocol {version.Protocol.Version.Protocol}: " +
            "the frame decodes to nothing and no applier can ever see it.");

        return bound.Decode(frame, PacketCodecContext.Registryless);
    }

    /// <summary>A 770+ player_chat frame, built field by field from the packet layout and the chat types it composes, never by running the encoder.</summary>
    private static byte[] BuildModernPlayerChatFrame(bool signed = true)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        w.WriteVarInt(11);          // globalIndex (1.21.5+)
        w.WriteUuid(Sender);        // sender
        w.WriteVarInt(4);           // index
        w.WriteBool(signed);        // nullable MessageSignature
        if (signed)
        {
            var sig = new byte[256];
            Array.Fill(sig, (byte)0x5C);
            w.WriteBytes(sig);
        }

        // packed behavior: content, instant, salt, packed last-seen.
        w.WriteString(Content, 256);
        w.WriteLong(1_700_000_000_000L);
        w.WriteLong(0x0123_4567_89AB_CDEFL);
        w.WriteVarInt(0);           // no last-seen entries

        w.WriteBool(false);         // no unsigned override, so the signed content is what displays
        w.WriteVarInt(0);           // FilterMask PASS_THROUGH

        // bound behavior: holder chat type, name component, optional target component.
        w.WriteVarInt(1);
        WriteModernComponent(ref w, Component.Text("Notch"));
        w.WriteBool(false);

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteModernComponent(ref PacketWriter w, Component c) =>
        w.WriteComponent(c, ComponentWireEra.Modern, NbtWireFormat.JavaRootTagOrString);
}
