using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Chat component payload shapes used by system-chat and server-data packets.</summary>
public sealed class ChatComponentWireShapeTests
{
    [Fact]
    public void JsonComponent_WritesStringPayload()
    {
        var packet = new ClientboundSystemChatPacket(Component.Text("hello world"), Overlay: false);
        byte[] bytes = CodecRoundTrip.Encode(ChatDisplayCodecs.SystemChatV1_19, packet);
        Assert.True(bytes[1] is (byte)'{' or (byte)'"');
        var decoded = CodecRoundTrip.Cycle(ChatDisplayCodecs.SystemChatV1_19, packet);
        Assert.Equal(packet.Overlay, decoded.Overlay);
    }

    [Fact]
    public void NbtComponent_WritesTagPayload()
    {
        var packet = new ClientboundSystemChatPacket(Component.Text("hello world"), Overlay: true);
        byte[] bytes = CodecRoundTrip.Encode(ChatDisplayCodecs.SystemChatV1_20_3, packet);
        Assert.True(bytes[0] is 8 or 10);
        var decoded = CodecRoundTrip.Cycle(ChatDisplayCodecs.SystemChatV1_20_3, packet);
        Assert.True(decoded.Overlay);
    }

    [Fact]
    public void ServerData_SecureChatFieldIsRemovedAtBoundary()
    {
        var packet = new ClientboundServerDataPacket(Component.Text("motd"), IconBytes: null)
        { EnforcesSecureChat = true };
        byte[] withField = CodecRoundTrip.Encode(ChatDisplayCodecs.ServerDataV1_20_3, packet);
        byte[] withoutField = CodecRoundTrip.Encode(ChatDisplayCodecs.ServerDataV1_20_5, packet);
        Assert.Equal(withField.Length - 1, withoutField.Length);
        var decoded = CodecRoundTrip.Cycle(ChatDisplayCodecs.ServerDataV1_20_3, packet);
        Assert.True(decoded.EnforcesSecureChat);
    }
}
