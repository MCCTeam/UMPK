using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.PacketApplication.Session;

/// <summary>Resource-pack requests that require an immediate protocol-compatible response.</summary>
public sealed class ResourcePackRequestApplierTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(110)]
    [InlineData(210)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task Request_IsAnsweredWithMatchingHash(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        var request = new ClientboundLegacyResourcePackPacket(
            "https://example.invalid/pack.zip",
            "0123456789abcdef0123456789abcdef01234567");

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, "resource_pack", request);

        object sent = Assert.Single(harness.Recorder.Packets);
        var response = Assert.IsType<ServerboundLegacyResourcePackPacket>(sent);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", response.Hash);
        Assert.Equal(ResourcePackAction.Declined, response.Action);
        Assert.True(BoundPacketApplierHarness.Version(protocol).Protocol.TryGetRegistry(
            ProtocolPhase.Play,
            PacketFlow.Serverbound,
            out PhaseRegistry registry));
        Assert.True(registry.TryGetOutbound(response.Type, out _, out BoundPacketCodec bound));
        Assert.True(bound.IsImplemented);
    }

    [Fact]
    public async Task ConfiguredAcceptanceWithoutDownload_ReportsAccepted()
    {
        var policies = new ClientPolicies
        {
            ResourcePack = ResourcePackPolicy.Configure(new ResourcePackDownloadOptions { Accept = true }),
        };
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(774), policies: policies);
        var id = Guid.NewGuid();

        await harness.ApplyAsync(new ClientboundResourcePackPushPacket(
            id, "https://example.invalid/pack.zip", string.Empty, Required: false, Prompt: null));

        var response = Assert.IsType<ServerboundResourcePackPacket>(Assert.Single(harness.Recorder.Packets));
        Assert.Equal(id, response.Id);
        Assert.Equal(ResourcePackAction.Accepted, response.Action);
    }

    [Fact]
    public void CachingCannotBeEnabledWithoutDownloading()
    {
        Assert.Throws<ArgumentException>(() => ResourcePackPolicy.Configure(
            new ResourcePackDownloadOptions
            {
                Accept = true,
                Cache = true,
                CacheDirectory = Path.GetTempPath(),
            }));
    }
}
