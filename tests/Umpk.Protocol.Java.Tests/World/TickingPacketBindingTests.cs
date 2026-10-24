using Umpk.Data.Java;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public sealed class TickingPacketBindingTests
{
    public static IEnumerable<object[]> ModernPackets =>
        from protocol in JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Order()
        where protocol >= 765
        from identifier in new[] { "ticking_state", "ticking_step" }
        select new object[] { protocol, identifier };

    [Theory]
    [MemberData(nameof(ModernPackets))]
    public void EveryNativeTickControlPacketResolvesAnImplementedCodec(int protocol, string identifier)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        PhaseRegistry registry = version!.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        int wire = Assert.Single(registry.Packets, row => row.Type.Id == Identifier.Minecraft(identifier)).WireId;
        Assert.True(registry.TryGetInbound(wire, out BoundPacketCodec binding));
        Assert.True(binding.IsImplemented, $"{identifier} remains a marker at protocol {protocol}.");
    }

    [Theory]
    [InlineData("ticking_state")]
    [InlineData("ticking_step")]
    public void TickControlDoesNotBindBeforeOnePointTwentyPointThree(string identifier) =>
        Assert.False(BoundCodec.EntryAt(764, PacketFlow.Clientbound, "minecraft:" + identifier).IsImplemented);
}
