using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Marker-vs-implemented registration invariant for protocols 47, 770, and 776. The test walks every phase/flow registry and splits entries on <see cref="BoundPacketCodec.IsImplemented"/> so either direction of drift is visible in CI.</summary>
public sealed class RegistrationMarkerCountInvariantTests
{
    private readonly ITestOutputHelper _output;

    public RegistrationMarkerCountInvariantTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void GateMarkerCounts_MatchRecountedInvariant()
    {
        (int i47, int m47) = Count(JavaVersions.V1_8);
        (int i770, int m770) = Count(JavaVersions.V1_21_5);
        (int i776, int m776) = Count(JavaVersions.V26_2);
        _output.WriteLine($"47:  implemented={i47} markers={m47} total={i47 + m47}");
        _output.WriteLine($"770: implemented={i770} markers={m770} total={i770 + m770}");
        _output.WriteLine($"776: implemented={i776} markers={m776} total={i776 + m776}");

        // Totals stay stable at 111, 236, and 256; the implemented/marker split is the invariant.
        Assert.Equal((103, 8), (i47, m47));
        Assert.Equal((205, 31), (i770, m770));
        Assert.Equal((213, 43), (i776, m776));
    }

    private static (int Implemented, int Markers) Count(JavaVersion version)
    {
        int implemented = 0;
        int markers = 0;
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!version.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType _) in registry.Packets)
                {
                    registry.TryGetInbound(wireId, out BoundPacketCodec entry);
                    if (entry.IsImplemented)
                        implemented++;

                    else
                        markers++;

                }
            }

        return (implemented, markers);
    }
}
