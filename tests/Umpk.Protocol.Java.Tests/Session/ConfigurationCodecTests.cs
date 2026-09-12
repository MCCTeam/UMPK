using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Session;

/// <summary>Configuration-phase registry payloads and play-to-configuration handshake markers.</summary>
public sealed class ConfigurationCodecTests
{
    [Fact]
    public void RegistryBlob_RoundTripsPreservingMemberOrder()
    {
        var dimensionType = new NbtCompound();
        dimensionType.PutString("type", "minecraft:dimension_type");
        var root = new NbtCompound();
        root.Put("minecraft:dimension_type", dimensionType);
        root.PutInt("zzz_last", 1);
        root.PutInt("aaa_first", 2);

        var packet = new ClientboundConfigRegistryBlobPacket(root);
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.RegistryBlob, packet);
        byte[] first = CodecRoundTrip.Encode(ConfigurationCodecs.RegistryBlob, packet);
        byte[] second = CodecRoundTrip.Encode(ConfigurationCodecs.RegistryBlob, decoded);
        Assert.Equal(first, second);
    }

    [Fact]
    public void StartAndAcknowledgementMarkers_HaveEmptyPayloads()
    {
        var start = new ClientboundStartConfigurationPacket();
        var acknowledgement = new ServerboundConfigurationAcknowledgedPacket();
        Assert.Empty(CodecRoundTrip.Encode(ConfigurationCodecs.StartConfiguration, start));
        Assert.Empty(CodecRoundTrip.Encode(ConfigurationCodecs.ConfigurationAcknowledged, acknowledgement));
        Assert.NotNull(CodecRoundTrip.Cycle(ConfigurationCodecs.StartConfiguration, start));
    }
}
