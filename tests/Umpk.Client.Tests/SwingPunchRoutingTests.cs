using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The swing send across the 26.3 removal of <c>minecraft:swing</c>. On 777 the session binds the handless <c>minecraft:punch</c>, so both hands send it (the packet carries no hand to choose with); older eras keep the hand-carrying swing. Routing reads the bound outbound table, never a protocol number.</summary>
public sealed class SwingPunchRoutingTests
{
    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    private static InteractionActions Actions(RecordingSink recorder, int protocol)
    {
        JavaVersion version = Version(protocol);
        var state = new ClientState(new ClientFeatures().Normalized());
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        return new InteractionActions(recorder, services, new SequenceTracker());
    }

    [Theory]
    [InlineData(Hand.Main)]
    [InlineData(Hand.Off)]
    public async Task SwingAsync_On777_SendsPunch_ForBothHands(Hand hand)
    {
        var recorder = new RecordingSink();

        await Actions(recorder, 777).SwingAsync(hand);

        Assert.IsType<ServerboundPunchPacket>(Assert.Single(recorder.Packets));
    }

    [Theory]
    [InlineData(Hand.Main, 0)]
    [InlineData(Hand.Off, 1)]
    public async Task SwingAsync_Before777_SendsSwing_WithHand(Hand hand, int expectedHand)
    {
        var recorder = new RecordingSink();

        await Actions(recorder, 776).SwingAsync(hand);

        var sent = Assert.IsType<ServerboundSwingPacket>(Assert.Single(recorder.Packets));
        Assert.Equal(expectedHand, sent.Hand);
    }

    [Fact]
    public async Task TeleportConfirm_On777_CarriesPositionAndRotation()
    {
        var harness = new ApplierHarness(JavaVersions.V26_3);
        var pos = new ClientboundPlayerPositionPacket(10, 65, -3, 90, 0, 0, TeleportId: 7, ModernValues: null);

        await harness.ApplyAsync(pos);

        var accept = Assert.IsType<ServerboundAcceptTeleportationPacket>(harness.Recorder.Packets[0]);
        Assert.Equal(7, accept.TeleportId);

        // The 777 confirm is id, three doubles, two floats.
        byte[] expected =
        [
            0x07,
            0x40, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x40, 0x50, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x42, 0xB4, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        Assert.Equal(expected, EncodeAt(777, "minecraft:accept_teleportation", accept));
    }

    private static byte[] EncodeAt(int protocol, string identifier, object packet)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Serverbound, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(
            descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound).TryGetInbound(0, out BoundPacketCodec bound));
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, packet, new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty));
        return buffer.WrittenSpan.ToArray();
    }
}
