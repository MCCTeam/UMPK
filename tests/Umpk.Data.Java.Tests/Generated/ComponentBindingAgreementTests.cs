using System.Buffers;
using System.Text;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Data.Java.Tests.Generated;

/// <summary>The dataset is the single source of truth for the component click/hover interaction era, and this is what holds the binding tables to it. For every supported protocol it resolves the tab-list codec the registrar binds, pushes a component carrying a click event and a hover event through it, and asserts the era visible on the wire equals the era <see cref="Umpk.Protocol.Java.ProtocolFeatures.ComponentEra"/> declares for that version.</summary>
/// <remarks>Codecs cannot read this flag directly - they are shared statics that bake their era in at construction - so the dataset and binding table must state the boundary separately. This test compares those two statements for every supported version.</remarks>
public sealed class ComponentBindingAgreementTests
{
    private static Component Probe { get; } = new(
        new TextContent("era"),
        new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid/era"),
            HoverEvent = new HoverShowEntity("minecraft:pig", new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"), Component.Text("Pig")),
        });

    public static TheoryData<int> AllProtocols
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (JavaVersion version in JavaVersions.All)
            {
                data.Add(version.Version.Protocol);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void BoundTabListCodecUsesTheWireLayoutTheDatasetDeclares(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));

        // 1.8 carries the same two components under a different identity; from 1.9 it is tab_list.
        string identifier = protocol < 107 ? "minecraft:player_list_header_footer" : "minecraft:tab_list";

        var builder = new ProtocolDescriptorBuilder(version.Version, version.Features);
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).TryGetInbound(0, out BoundPacketCodec bound));
        Assert.True(bound.IsImplemented, $"tab list is a marker at protocol {protocol}; this test needs a bound codec.");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, new ClientboundTabListPacket(Probe, Probe), PacketCodecContext.Registryless);
        byte[] frame = buffer.WrittenSpan.ToArray();

        // The style field names are the era, verbatim, in both the JSON and the network-NBT transport.
        bool modernOnWire = Contains(frame, "click_event");
        bool legacyOnWire = Contains(frame, "clickEvent");
        Assert.True(modernOnWire ^ legacyOnWire, $"protocol {protocol}: frame must carry exactly one of the two style spellings.");

        ComponentWireEra onWire = modernOnWire ? ComponentWireEra.Modern : ComponentWireEra.Legacy;
        Assert.Equal(version.Features.ComponentEra, onWire);
    }

    /// <summary>The declared boundary itself: legacy through 769, modern from 770. Stated once, directly, so a dataset edit that moves it has to change this line and face the evidence in the XML doc on <see cref="Umpk.Protocol.Java.ProtocolFeatures.ComponentInteractionEra"/>.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void DeclaredWireLayoutBoundaryIs770(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        Assert.Equal(
            protocol >= 770 ? ComponentWireEra.Modern : ComponentWireEra.Legacy,
            version.Features.ComponentEra);
    }

    private static bool Contains(byte[] frame, string marker) =>
        frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker).AsSpan()) >= 0;
}
