using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Send-path coverage for the <c>client_command</c> respawn request on every supported protocol. The packet must have a bound codec and remain sendable on all 49 versions.</summary>
public sealed class RespawnSendTests
{
    /// <summary>All 49 supported protocols, 1.8 through 26.2, kept as an independent literal table so the test cannot inherit an omission from the catalog it verifies.</summary>
    private static readonly int[] All =
    [
        47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404, 477, 480, 485, 490, 498, 573,
        575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767,
        768, 769, 770, 771, 772, 773, 774, 775, 776,
    ];

    public static TheoryData<int> AllProtocols
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (int protocol in All)
                data.Add(protocol);

            return data;
        }
    }

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => All;

    private static SessionActions Actions(RecordingSink recorder, JavaVersion version, EventBus? events = null)
    {
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
            Events = events,
        };
        return new SessionActions(recorder, services);
    }

    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public async Task Respawn_Sends_TheClientCommandRecord(int protocol)
    {
        var recorder = new RecordingSink();
        var events = new EventBus(NullLogger.Instance, TimeSpan.Zero, 256);

        // RespawnAsync now waits for the clientbound respawn (or the confirmation window), so the send path is exercised by publishing the acknowledgement it is waiting for rather than by racing the window: the subscription is armed and the packet is sent (synchronously, on this fake sink) before RespawnAsync's first genuine await, so it is already listening by the time this returns.
        Task<RespawnOutcome> pending = Actions(recorder, Version(protocol), events).RespawnAsync();
        _ = events.PublishAsync(new Respawned());
        RespawnOutcome outcome = await pending;

        Assert.Equal(RespawnOutcome.Confirmed, outcome);
        var sent = Assert.IsType<ServerboundClientCommandPacket>(Assert.Single(recorder.Packets));
        Assert.Equal(ClientCommandAction.PerformRespawn, sent.Action);
        Assert.Equal(ProtocolPhase.Play, sent.Type.Phase);
        Assert.Equal(PacketFlow.Serverbound, sent.Type.Flow);
        Assert.Equal(Identifier.Minecraft("client_command"), sent.Type.Id);

        // No raw frames: the send now goes through the bound codec, so a version whose registry lacks the packet would throw rather than write bytes nobody validated.
        Assert.Empty(recorder.Frames);
    }

    /// <summary>The record has to encode to the ordinal the server reads. Resolution through the bound descriptor this fails if the packet is ever left a marker again on any of these protocols.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void Respawn_EncodesToOrdinalZero_OnTheShippedDescriptor(int protocol)
    {
        JavaVersion version = Version(protocol);
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        Assert.True(
            registry.TryGetOutbound(
                PlayPackets.Serverbound.ClientCommand, out int wireId, out BoundPacketCodec bound),
            $"client_command has no outbound binding at protocol {protocol}");
        Assert.True(wireId >= 0);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(
            ref writer,
            new ServerboundClientCommandPacket(ClientCommandAction.PerformRespawn),
            new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty));

        Assert.Equal<byte[]>([0x00], buffer.WrittenSpan.ToArray());
    }

    /// <summary>Why the old "no wire id" guard could never fire, kept as a standing warning about this shape. A marker is registered with a real wire id, and <see cref="WireIndex"/> matches on packet IDENTITY, so it happily resolves an id for a packet that has no codec at all. Demonstrated on serverbound <c>custom_payload</c>, which is still a marker on every protocol: a non-negative result here proves the lookup says nothing about whether the packet is implemented.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void AMarkerStillResolvesAWireId_SoAWireIdCheckIsNotAnImplementedCheck(int protocol)
    {
        var wire = new WireIndex(Version(protocol));

        Assert.True(wire.ServerboundPlay(Identifier.Minecraft("custom_payload")) >= 0);

        PhaseRegistry registry = Version(protocol).Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        int markerId = wire.ServerboundPlay(Identifier.Minecraft("custom_payload"));
        Assert.True(registry.TryGetInbound(markerId, out BoundPacketCodec marker));
        Assert.False(marker.IsImplemented);
    }

    /// <summary>The replacement gate. <c>CanSendPlay</c> consults the OUTBOUND table, which only holds implemented packets, so it answers the question a version-optional send actually needs.</summary>
    [Theory]
    [InlineData(47, false, false)]
    [InlineData(767, false, false)]
    [InlineData(768, true, false)]
    [InlineData(769, true, true)]
    [InlineData(776, true, true)]
    public void CanSendPlay_ReportsTheVersionGates_ForTickEndAndPlayerLoaded(
        int protocol, bool tickEnd, bool playerLoaded)
    {
        var wire = new WireIndex(Version(protocol));

        Assert.Equal(tickEnd, wire.CanSendPlay(PlayPackets.Serverbound.ClientTickEnd));
        Assert.Equal(playerLoaded, wire.CanSendPlay(PlayPackets.Serverbound.PlayerLoaded));

        // client_command is on every version, so the same gate says yes everywhere.
        Assert.True(wire.CanSendPlay(PlayPackets.Serverbound.ClientCommand));
    }

    /// <summary><c>client_command</c> is registered and implemented on every supported protocol. <c>CanSendPlay</c> therefore answers true everywhere, and <c>RespawnAsync</c> reports a concrete outcome instead of silently doing nothing.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void ClientCommand_IsSendableOnEveryProtocol(int protocol)
    {
        var wire = new WireIndex(Version(protocol));

        Assert.True(wire.CanSendPlay(PlayPackets.Serverbound.ClientCommand));
    }

    [Fact]
    public async Task RequestStatistics_Sends_TheRequestStatsAction()
    {
        var recorder = new RecordingSink();

        await Actions(recorder, Version(776)).RequestStatisticsAsync();

        var sent = Assert.IsType<ServerboundClientCommandPacket>(Assert.Single(recorder.Packets));
        Assert.Equal(ClientCommandAction.RequestStats, sent.Action);
    }
}
