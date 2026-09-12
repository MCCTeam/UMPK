using System.Reflection;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>The read loop resolves one inbound frame once and answers every lifecycle question off that resolution. These are the three things that go wrong if the resolution is trusted further than it is valid: a binding that has no resolved form silently loses its gates, and a cached phase route outlives the phase or the descriptor it was built for.</summary>
public class FrameResolutionTests
{
    private static readonly ProtocolFeatures NoFeatures = new();

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private static JavaConnectionOptions Options() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
    };

    /// <summary>A binding that resolves nothing keeps the predicate path. It is the case a compiler cannot report: inheriting both defaults leaves the type building, and an empty resolution taken as authoritative would arm no gate and recognise no delimiter, which reads as a working session until a phase transition never happens.</summary>
    [Fact]
    public async Task BindingThatResolvesNothing_StillBundlesAndArmsEveryGate()
    {
        // The premise: FakeCodecBinding declares neither default, so this test is about the defaults.
        MethodInfo[] declared = typeof(FakeCodecBinding).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(declared, m => m.Name.EndsWith("TryResolve", StringComparison.Ordinal));
        Assert.DoesNotContain(
            declared,
            m => m.Name.EndsWith("TryDecode", StringComparison.Ordinal) && m.GetParameters().Length == 3);

        await BundlesAtomically();
        await ArmsGate(compressionWireId: 5);
        await ArmsGate(encryptionWireId: 5);
        await ArmsTerminalGateAndResumesOnSetPhase();

        static async Task BundlesAtomically()
        {
            var pair = DuplexPipePair.Create();
            await using var conn = new JavaConnection(pair.Left, Options());
            conn.BindCodec(new FakeCodecBinding([0, 2, 3], bundleDelimiterWireId: 0), PacketFlow.Clientbound);
            conn.Start();

            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [0]);
            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [2, 0xB2]);
            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [3, 0xC3]);
            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [0]);

            InboundItem item = await conn.ReceiveAsync(Ct());
            Assert.NotNull(item.Bundle);
            Assert.Equal(2, item.Bundle!.Packets.Count);
        }

        static async Task ArmsGate(int? compressionWireId = null, int? encryptionWireId = null)
        {
            var pair = DuplexPipePair.Create();
            await using var conn = new JavaConnection(pair.Left, Options());
            conn.BindCodec(
                new FakeCodecBinding([5, 6], compressionWireId: compressionWireId, encryptionWireId: encryptionWireId),
                PacketFlow.Clientbound);
            conn.SetPhase(ProtocolPhase.Login);
            conn.Start();

            long observed = 0;
            conn.PacketObserved += _ => Interlocked.Increment(ref observed);

            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5]);
            InboundItem gateFrame = await conn.ReceiveAsync(Ct());
            Assert.Equal(5, gateFrame.Frame.WireId);

            // The next frame's bytes are already on the pipe. The reader is parked at the transform boundary, so nothing may read them until the consumer enables the transform.
            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [6]);
            await Task.Delay(150);
            Assert.Equal(1, Interlocked.Read(ref observed));
        }

        static async Task ArmsTerminalGateAndResumesOnSetPhase()
        {
            var pair = DuplexPipePair.Create();
            await using var conn = new JavaConnection(pair.Left, Options());
            conn.BindCodec(
                new FakeCodecBinding([5, 6], terminalWireId: 5, nextPhase: ProtocolPhase.Play),
                PacketFlow.Clientbound);
            conn.SetPhase(ProtocolPhase.Configuration);
            conn.Start();

            long observed = 0;
            conn.PacketObserved += _ => Interlocked.Increment(ref observed);

            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [5]);
            InboundItem terminal = await conn.ReceiveAsync(Ct());
            Assert.Equal(5, terminal.Frame.WireId);

            await FramingTests.WriteRawFrameAsync(pair.Right.Output, [6]);
            await Task.Delay(150);
            Assert.Equal(1, Interlocked.Read(ref observed));

            conn.SetPhase(ProtocolPhase.Play);
            InboundItem next = await conn.ReceiveAsync(Ct());
            Assert.Equal(6, next.Frame.WireId);
        }
    }

    /// <summary>A phase route cached from the previous frame must not survive the phase it was built for. Protocol 776 puts <c>keep_alive</c> (a long) at configuration clientbound 0x04 and <c>block_changed_ack</c> (a VarInt) at play clientbound 0x04, so a stale route does not merely mis-name the packet: it reads eight bytes where one arrived.</summary>
    [Fact]
    public async Task SetPhaseWhileRunning_DecodesTheNextFrameAgainstTheNewPhaseTable()
    {
        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", 776), NoFeatures);
        PacketRegistrar.Register(builder, ProtocolPhase.Configuration, PacketFlow.Clientbound, 0x04, "minecraft:keep_alive");
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, 0x04, "minecraft:block_changed_ack");

        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new DescriptorFrameCodecBinding(builder.Build()), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Configuration);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [0x04, 0, 0, 0, 0, 0, 0, 0, 7]);
        InboundItem inConfiguration = await conn.ReceiveAsync(Ct());
        Assert.Equal(7L, Assert.IsType<ClientboundConfigKeepAlivePacket>(inConfiguration.Packet).Id);

        conn.SetPhase(ProtocolPhase.Play);

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [0x04, 7]);
        InboundItem inPlay = await conn.ReceiveAsync(Ct());
        Assert.Equal(7, Assert.IsType<ClientboundBlockChangedAckPacket>(inPlay.Packet).Sequence);
    }

    /// <summary>Swapping the binding is the other way the table under a wire id changes, and it happens with no phase transition to notice: a proxy or a test rebinds a live connection from one version's descriptor to another's. Protocol 47 reads <c>update_time</c> at play clientbound 0x03 as two longs; the 776 descriptor's entry at the same id is a one-byte VarInt.</summary>
    [Fact]
    public async Task BindCodecToAnotherDescriptor_ResolvesTheNextFrameAgainstIt()
    {
        var legacy = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", 47), NoFeatures);
        PacketRegistrar.Register(legacy, ProtocolPhase.Play, PacketFlow.Clientbound, 0x03, "minecraft:update_time");

        var modern = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", 776), NoFeatures);
        PacketRegistrar.Register(modern, ProtocolPhase.Play, PacketFlow.Clientbound, 0x03, "minecraft:block_changed_ack");

        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, Options());
        conn.BindCodec(new DescriptorFrameCodecBinding(legacy.Build()), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        await FramingTests.WriteRawFrameAsync(
            pair.Right.Output, [0x03, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 2]);
        InboundItem on47 = await conn.ReceiveAsync(Ct());
        Assert.IsType<ClientboundSetTimePacket>(on47.Packet);

        conn.BindCodec(new DescriptorFrameCodecBinding(modern.Build()), PacketFlow.Clientbound);

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [0x03, 7]);
        InboundItem on776 = await conn.ReceiveAsync(Ct());
        Assert.Equal(7, Assert.IsType<ClientboundBlockChangedAckPacket>(on776.Packet).Sequence);
    }
}
