using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public sealed class PreRegistrationGateTests
{
    private const int GateWireId = 5;
    private const int NextWireId = 6;

    private static readonly ProtocolFeatures NoFeatures = new();

    [Theory]
    [InlineData(FrameRole.Terminal, false)]
    [InlineData(FrameRole.Terminal, true)]
    [InlineData(FrameRole.CompressionPoint, false)]
    [InlineData(FrameRole.CompressionPoint, true)]
    [InlineData(FrameRole.EncryptionPoint, false)]
    [InlineData(FrameRole.EncryptionPoint, true)]
    public async Task MarkedGate_PausesReader_InEitherRegistrationOrder(FrameRole role, bool registerFirst)
    {
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", 776), NoFeatures);

        if (registerFirst)
        {
            RegisterEntry(builder, role);
            MarkGate(builder, role);
        }
        else
        {
            MarkGate(builder, role);
            RegisterEntry(builder, role);
        }

        var pair = DuplexPipePair.Create();
        await using var connection = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        connection.BindCodec(new DescriptorFrameCodecBinding(builder.Build()), PacketFlow.Clientbound);
        connection.SetPhase(ProtocolPhase.Login);
        connection.Start();

        long observed = 0;
        connection.PacketObserved += _ => Interlocked.Increment(ref observed);

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [GateWireId]);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [NextWireId]);

        InboundItem gate = await connection.ReceiveAsync(stopping.Token);
        Assert.Equal(GateWireId, gate.Frame.WireId);

        await Task.Delay(150);
        Assert.Equal(1, Interlocked.Read(ref observed));

        if (role == FrameRole.Terminal)
        {
            Assert.Equal(ProtocolPhase.Login, connection.Phase);
            connection.SetPhase(ProtocolPhase.Play);
            InboundItem next = await connection.ReceiveAsync(stopping.Token);
            Assert.Equal(NextWireId, next.Frame.WireId);
        }
    }

    private static void RegisterEntry(ProtocolDescriptorBuilder builder, FrameRole role)
    {
        if (role == FrameRole.Terminal)
        {
            builder.Register(GateWireId, TestPacket.Definition, TestPacket.Codec);
            return;
        }

        builder.RegisterMarker(
            GateWireId,
            new MarkerPacketType(ProtocolPhase.Login, PacketFlow.Clientbound, TestPacket.Definition.Id));
    }

    private static void MarkGate(ProtocolDescriptorBuilder builder, FrameRole role)
    {
        switch (role)
        {
            case FrameRole.Terminal:
                builder.MarkTerminal(ProtocolPhase.Login, PacketFlow.Clientbound, GateWireId, ProtocolPhase.Play);
                break;
            case FrameRole.CompressionPoint:
                builder.MarkCompressionEnablePoint(ProtocolPhase.Login, PacketFlow.Clientbound, GateWireId);
                break;
            case FrameRole.EncryptionPoint:
                builder.MarkEncryptionEnablePoint(ProtocolPhase.Login, PacketFlow.Clientbound, GateWireId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(role), role, null);
        }
    }

    private sealed record TestPacket : IPacket
    {
        public static readonly PacketType<TestPacket> Definition = new(
            ProtocolPhase.Login,
            PacketFlow.Clientbound,
            Identifier.Minecraft("pre_registration_gate_test"));

        public static readonly PacketCodec<TestPacket> Codec = PacketCodec<TestPacket>.Of(
            static (ref PacketWriter _, TestPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new TestPacket());

        public PacketType Type => Definition;
    }
}
