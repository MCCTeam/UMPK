using System.Buffers;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.Session;

/// <summary>Pins literal frames and incompatible layouts for resource-pack negotiation.</summary>
public sealed class ResourcePackWireShapeTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void ResourcePack_Clientbound_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(Str("https://example.invalid/pack.zip"), Str("0123456789abcdef0123456789abcdef01234567"));
        var p = (ClientboundLegacyResourcePackPacket)Clientbound(protocol, "resource_pack").DecodeFrame(frame);
        Assert.Equal("https://example.invalid/pack.zip", p.Url);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", p.Hash);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(110)]
    public void ResourcePack_HashEchoFrame_Decodes(int protocol)
    {
        byte[] frame = Cat(Str("0123456789abcdef0123456789abcdef01234567"), VarInt(3));
        var p = (ServerboundLegacyResourcePackPacket)Serverbound(protocol, "resource_pack").DecodeFrame(frame);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", p.Hash);
        Assert.Equal(ResourcePackAction.Accepted, p.Action);
        Assert.Equal(frame, Serverbound(protocol, "resource_pack").Encode(p));
    }

    [Theory]
    [InlineData(210)]
    [InlineData(340)]
    [InlineData(404)]
    public void ResourcePack_ResultOnlyFrame_Decodes(int protocol)
    {
        byte[] frame = VarInt(0);
        var p = (ServerboundLegacyResourcePackPacket)Serverbound(protocol, "resource_pack").DecodeFrame(frame);
        Assert.Null(p.Hash);
        Assert.Equal(ResourcePackAction.SuccessfullyLoaded, p.Action);
        Assert.Equal(frame, Serverbound(protocol, "resource_pack").Encode(p));
    }

    [Fact]
    public void ServerboundResourcePack_AlternateWireShapes_AreRejected()
    {
        byte[] withHash = Cat(Str("0123456789abcdef0123456789abcdef01234567"), VarInt(3));
        byte[] resultOnly = VarInt(3);

        Rejects(Serverbound(210, "resource_pack"), withHash, "1.10 dropped the echoed hash");
        Rejects(Serverbound(107, "resource_pack"), resultOnly, "1.9 requires the echoed hash");
    }
}
