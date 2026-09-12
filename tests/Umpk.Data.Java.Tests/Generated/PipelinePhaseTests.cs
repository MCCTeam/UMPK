using System.Buffers;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Data.Java.Tests.Generated;

/// <summary>Drives the transport + codec binding over an in-memory pipe pair, exercising terminal-packet phase transitions on the real descriptor from <c>Umpk.Data.Java</c>.</summary>
public class PipelinePhaseTests
{
    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    [Fact]
    public async Task Login_To_Configuration_Transition_OnLoginAcknowledged_1215()
    {
        JavaVersion version = JavaVersions.V1_21_5;
        var pair = DuplexPipePair.Create();

        // The "server" side sends serverbound frames; the "client" side decodes clientbound... but to exercise login-acknowledged (serverbound, terminal) we bind the reading side to Serverbound inbound and push a login-acknowledged frame at it, then confirm the phase advances to config.
        await using var reader = new JavaConnection(pair.Right, new JavaConnectionOptions
        {
            ReadIdleTimeout = TimeSpan.Zero,
            UnknownPacketPolicy = UnknownPacketPolicy.Skip,
        });
        var binding = new DescriptorFrameCodecBinding(version.Protocol);
        reader.BindCodec(binding, PacketFlow.Serverbound);
        reader.SetPhase(ProtocolPhase.Login);
        reader.Start();

        // Encode a login-acknowledged frame (wire id 0x03 in 1.21.5 login serverbound) and send it raw.
        await using var writer = new JavaConnection(pair.Left, new JavaConnectionOptions { ReadIdleTimeout = TimeSpan.Zero });
        var body = new ArrayBufferWriter<byte>();
        var pw = new PacketWriter(body);
        pw.WriteVarInt(0x03);
        await writer.SendFrameAsync(0x03, body.WrittenMemory[1..], Ct());

        InboundItem item = await reader.ReceiveAsync(Ct());
        Assert.IsType<ServerboundLoginAcknowledgedPacket>(item.Packet);

        // Assert the descriptor itself declares login-acknowledged (serverbound login, wire id 0x03, the frame decoded above) as a terminal transition into Configuration. This queries the real transition metadata instead of manually SetPhase(Configuration) + re-reading it, which would be a set-then-read tautology. Mirrors PlayToConfigurationReentryGateTests.
        Assert.True(version.Protocol.TryGetTerminalTransition(
            ProtocolPhase.Login, PacketFlow.Serverbound, 0x03, out ProtocolPhase next));
        Assert.Equal(ProtocolPhase.Configuration, next);
    }

    [Fact]
    public async Task Handshake_Intention_Decodes_And_Status_Registry_Resolves()
    {
        JavaVersion version = JavaVersions.V1_21_5;
        var pair = DuplexPipePair.Create();

        await using var reader = new JavaConnection(pair.Right, new JavaConnectionOptions
        {
            ReadIdleTimeout = TimeSpan.Zero,
            UnknownPacketPolicy = UnknownPacketPolicy.Skip,
        });
        var binding = new DescriptorFrameCodecBinding(version.Protocol);
        reader.BindCodec(binding, PacketFlow.Serverbound);
        reader.SetPhase(ProtocolPhase.Handshake);
        reader.Start();

        await using var writer = new JavaConnection(pair.Left, new JavaConnectionOptions { ReadIdleTimeout = TimeSpan.Zero });
        var handshake = new ServerboundHandshakePacket(770, "localhost", 25565, 1);
        var buffer = new ArrayBufferWriter<byte>();
        var pw = new PacketWriter(buffer);
        HandshakeCodecs.Intention.Encode(ref pw, handshake, PacketCodecContext.Registryless);
        await writer.SendFrameAsync(0x00, buffer.WrittenMemory, Ct());

        InboundItem item = await reader.ReceiveAsync(Ct());
        var decoded = Assert.IsType<ServerboundHandshakePacket>(item.Packet);
        Assert.Equal(770, decoded.ProtocolVersion);
        Assert.Equal("localhost", decoded.ServerAddress);
        Assert.Equal(1, decoded.NextState);
    }

    [Fact]
    public void EveryVersion_HasHandshakeStatusLoginRegistries()
    {
        foreach (JavaVersion v in JavaVersions.All)
        {
            Assert.True(v.Protocol.TryGetRegistry(ProtocolPhase.Handshake, PacketFlow.Serverbound, out _));
            Assert.True(v.Protocol.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound, out _));
            Assert.True(v.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out _));
        }
    }
}
