using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

public sealed class CookiePayloadLimitTests
{
    private const int Limit = 5120;
    private static readonly Identifier Key = Identifier.Minecraft("session");

    [Fact]
    public void ExactLimit_RoundTripsInLoginAndConfiguration()
    {
        byte[] payload = new byte[Limit];

        Assert.Equal(payload, CodecRoundTrip.Cycle(
            LoginChannelCodecs.LoginCookieResponse,
            new ServerboundLoginCookieResponsePacket(Key, payload)).Payload);
        Assert.Equal(payload, CodecRoundTrip.Cycle(
            ConfigurationCodecs.StoreCookie,
            new ClientboundConfigStoreCookiePacket(Key, payload)).Payload);
    }

    [Fact]
    public void OversizePayload_IsRejectedWhileEncoding()
    {
        byte[] payload = new byte[Limit + 1];

        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Encode(
            LoginChannelCodecs.LoginCookieResponse,
            new ServerboundLoginCookieResponsePacket(Key, payload)));
        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Encode(
            ConfigurationCodecs.StoreCookie,
            new ClientboundConfigStoreCookiePacket(Key, payload)));
    }

    [Fact]
    public void OversizePayload_IsRejectedWhileDecoding()
    {
        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Decode(
            LoginChannelCodecs.LoginCookieResponse,
            OversizeFrame(nullable: true)));
        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Decode(
            ConfigurationCodecs.StoreCookie,
            OversizeFrame(nullable: false)));
    }

    private static byte[] OversizeFrame(bool nullable)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteString(Key.ToString());
        if (nullable)
            writer.WriteBool(true);
        writer.WriteVarInt(Limit + 1);
        return buffer.WrittenSpan.ToArray();
    }
}
