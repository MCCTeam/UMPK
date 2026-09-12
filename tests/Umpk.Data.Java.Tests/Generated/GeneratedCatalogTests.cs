using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Data.Java.Tests.Generated;

/// <summary>Tests over the generated <c>Umpk.Data.Java</c> catalog: every sampled (version, packet) resolves a codec or a marker, and the catalog obeys the normative lazy no-cctor trimming shape.</summary>
public class GeneratedCatalogTests
{
    public static IEnumerable<object[]> SampledVersions =>
        [
            [JavaVersions.V1_8],
            [JavaVersions.V1_20_2],   // protocols 764-767
            [JavaVersions.V1_20_4],
            [JavaVersions.V1_20_6],
            [JavaVersions.V1_21_1],
            [JavaVersions.V1_21_5],
            [JavaVersions.V26_2],
        ];

    // The sampled versions plus the 764-767 and 768-775 versions: every registered packet in each of these descriptors must resolve a codec or a marker, and every descriptor must build.
    public static IEnumerable<object[]> AllVersions =>
    [
        [JavaVersions.V1_8],
        [JavaVersions.V1_9], [JavaVersions.V1_9_1], [JavaVersions.V1_9_2], [JavaVersions.V1_9_4],
        [JavaVersions.V1_10], [JavaVersions.V1_11], [JavaVersions.V1_11_2],
        [JavaVersions.V1_12], [JavaVersions.V1_12_1], [JavaVersions.V1_12_2],
        [JavaVersions.V1_13], [JavaVersions.V1_13_1], [JavaVersions.V1_13_2],
        [JavaVersions.V1_20_2], [JavaVersions.V1_20_3], [JavaVersions.V1_20_5], [JavaVersions.V1_21],
        [JavaVersions.V1_21_2], [JavaVersions.V1_21_4], [JavaVersions.V1_21_5],
        [JavaVersions.V1_21_6], [JavaVersions.V1_21_7], [JavaVersions.V1_21_9], [JavaVersions.V1_21_11],
        [JavaVersions.V26_1], [JavaVersions.V26_2],
    ];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void EveryDescriptor_Builds_AndEveryPacketResolves(JavaVersion version)
    {
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
        {
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!version.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                {
                    continue;
                }

                foreach ((int wireId, PacketType type) in registry.Packets)
                {
                    Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec entry));
                    Assert.Equal(type, entry.Type);
                }
            }
        }
    }

    [Fact]
    public void Catalog_Resolves_And_HasDistinctProtocols()
    {
        // All exposes exactly one entry per protocol number. This is the N:1 name->protocol dedup invariant (1.21.2/1.21.3 both map to 768): the catalog must not list a protocol twice even though both names get their own ergonomic V* property.
        int distinctProtocols = JavaVersions.All.Select(v => v.Version.Protocol).Distinct().Count();
        Assert.Equal(distinctProtocols, JavaVersions.All.Count);

        // 49 distinct protocols: 47/770/776, the pre-flattening range 107-340 (1.9-1.12.2), the 1.13.x protocols 393/401/404, the flattening era 477/480/485/490/498 (1.14.x) + 573/575/578 (1.15.x), netty-modern 735-758 (1.16-1.18.2), and 759-763, 764-767, 768-775.
        Assert.Equal(49, JavaVersions.All.Count);

        // Every supported protocol resolves by number.
        foreach (int protocol in new[] { 47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776 })
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out _), $"protocol {protocol} must resolve");
        }

        // Names resolve including N:1 aliases (1.20.3/1.20.4 share 765, 1.20.5/1.20.6 share 766, 1.21/1.21.1 share 767, 1.21.2/1.21.3 share 768, 1.21.7/1.21.8 share 772, 1.21.9/1.21.10 share 773); alias lookup goes through the generated name table, not JavaVersion.HasName.
        foreach (string name in new[]
                 {
                     "1.8",
                     "1.9", "1.9.1", "1.9.2", "1.9.4", "1.10", "1.11", "1.11.2", "1.12", "1.12.1", "1.12.2",
                     "1.13", "1.13.1", "1.13.2",
                     "1.20.2", "1.20.3", "1.20.4", "1.20.5", "1.20.6", "1.21", "1.21.1",
                     "1.21.2", "1.21.3", "1.21.4", "1.21.5", "1.21.6", "1.21.7", "1.21.8",
                     "1.21.9", "1.21.10", "1.21.11", "26.1", "26.2",
                 })
        {
            Assert.True(JavaVersions.TryGetByName(name, out _), name);
        }

        Assert.False(JavaVersions.TryGetByProtocol(999, out _));
    }

    [Theory]
    [MemberData(nameof(SampledVersions))]
    public void EveryRegisteredPacket_ResolvesCodecOrMarker(JavaVersion version)
    {
        // Walk every (phase, flow) registry; every wire id must resolve an entry (codec or marker).
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
        {
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!version.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                {
                    continue;
                }

                foreach ((int wireId, PacketType type) in registry.Packets)
                {
                    Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec entry));
                    Assert.Equal(type, entry.Type);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SampledVersions))]
    public void MarkerCount_IsTrackedAndReported(JavaVersion version)
    {
        (int implemented, int markers, _) = CountEntries(version);
        // The exemplars + login/config reach-play path are implemented; the rest are markers. (The former Assert.Equal(total, implemented + markers) was a tautology: CountEntries defines total as implemented + markers. Exact per-protocol split is pinned in RegistrationMarkerCountInvariantTests, so it is not duplicated here.)
        Assert.True(implemented > 0, "at least the exemplars must be implemented");
        Assert.True(markers > 0, "registration intentionally leaves the bulk of the surface as markers");
    }

    [Fact]
    public void JavaVersions_HasNoStaticConstructor_LazyShape()
    {
        // The normative trimming shape: no static constructor and no static field initializers, so touching one V* property never roots the whole catalog. A type with field initializers or a static ctor would expose a non-null TypeInitializer.
        Assert.Null(typeof(JavaVersions).TypeInitializer);
    }

    [Fact]
    public void TouchingOneVersion_DoesNotThrowOrRequireOthers()
    {
        // Behavioral rooting check: constructing V1_21_5 alone succeeds without All being touched.
        JavaVersion only = JavaVersions.V1_21_5;
        Assert.Equal(770, only.Version.Protocol);
    }

    private static (int Implemented, int Markers, int Total) CountEntries(JavaVersion version)
    {
        int implemented = 0;
        int markers = 0;
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
        {
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!version.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                {
                    continue;
                }

                foreach ((int wireId, PacketType _) in registry.Packets)
                {
                    registry.TryGetInbound(wireId, out BoundPacketCodec entry);
                    if (entry.IsImplemented)
                    {
                        implemented++;
                    }
                    else
                    {
                        markers++;
                    }
                }
            }
        }

        return (implemented, markers, implemented + markers);
    }
}
