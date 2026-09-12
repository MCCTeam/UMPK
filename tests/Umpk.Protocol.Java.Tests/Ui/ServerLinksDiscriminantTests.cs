using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>A server-links entry opens with the discriminant of an known-link-type-or-component choice. <see langword="true"/> selects the known-link-type branch. The packet is registered in both the configuration and the play protocol, so the two phases must emit the same byte.</summary>
public sealed class ServerLinksDiscriminantTests
{
    private static readonly ServerLinkEntry KnownLink = new(KnownTypeId: 0, Label: null, "https://bugs.example");

    private static readonly ServerLinkEntry CustomLink =
        new(KnownTypeId: null, Label: Component.Text("Wiki"), "https://wiki.example");

    /// <summary>A known link type is the left branch, so its discriminant byte is 1.</summary>
    [Fact]
    public void AKnownLinkType_WritesTheLeftDiscriminant()
    {
        byte[] play = CodecRoundTrip.Encode(
            UiMiscCodecs.ServerLinksV1_21_5, new ClientboundServerLinksPacket([KnownLink]));
        byte[] configuration = CodecRoundTrip.Encode(
            ConfigurationCodecs.ServerLinksV1_21_5, new ClientboundConfigServerLinksPacket([KnownLink]));

        // The list count VarInt, then the entry's discriminant.
        Assert.Equal(0x01, play[0]);
        Assert.Equal(0x01, play[1]);
        Assert.Equal(play, configuration);
    }

    /// <summary>A custom label is the right branch, so its discriminant byte is 0.</summary>
    [Fact]
    public void ACustomLabel_WritesTheRightDiscriminant()
    {
        byte[] play = CodecRoundTrip.Encode(
            UiMiscCodecs.ServerLinksV1_21_5, new ClientboundServerLinksPacket([CustomLink]));
        byte[] configuration = CodecRoundTrip.Encode(
            ConfigurationCodecs.ServerLinksV1_21_5, new ClientboundConfigServerLinksPacket([CustomLink]));

        Assert.Equal(0x00, play[1]);
        Assert.Equal(play, configuration);
    }

    /// <summary>The cross-phase clause: the configuration codec must decode the bytes the play codec wrote, and read the same entries out of them. A round trip through either codec alone agrees with itself whichever way its discriminant runs.</summary>
    [Fact]
    public void TheConfigurationCodec_ReadsWhatThePlayCodecWrote()
    {
        byte[] frame = CodecRoundTrip.Encode(
            UiMiscCodecs.ServerLinksV1_21_5, new ClientboundServerLinksPacket([KnownLink, CustomLink]));

        ClientboundConfigServerLinksPacket decoded =
            CodecRoundTrip.Decode(ConfigurationCodecs.ServerLinksV1_21_5, frame);

        Assert.Equal(2, decoded.Links.Count);
        Assert.Equal(0, decoded.Links[0].KnownTypeId);
        Assert.Null(decoded.Links[0].Label);
        Assert.Null(decoded.Links[1].KnownTypeId);
        Assert.NotNull(decoded.Links[1].Label);
    }
}
