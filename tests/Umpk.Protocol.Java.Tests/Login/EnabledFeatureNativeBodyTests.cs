using System.Buffers;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

public sealed class EnabledFeatureNativeBodyTests
{
    // Original unmodified client packet writers/readers at 1.19.3, 1.20.1 and 1.20.2. The adjacent 762 source has the same wire form; native class origins, jar/library hashes and decoded sets are retained under runs/axe-work-20261022-v1/native-feature-writer/.
    public static IEnumerable<object[]> NativeBodies()
    {
        yield return [761, "00", new string[] { }];
        yield return [761, "01116d696e6563726166743a76616e696c6c61", new string[] { "minecraft:vanilla" }];
        yield return [761, "03116d696e6563726166743a76616e696c6c61156d696e6563726166743a7570646174655f315f3230166578616d706c653a6675747572655f66656174757265", new string[] { "minecraft:vanilla", "minecraft:update_1_20", "example:future_feature" }];
        yield return [762, "00", new string[] { }];
        yield return [762, "01116d696e6563726166743a76616e696c6c61", new string[] { "minecraft:vanilla" }];
        yield return [762, "03116d696e6563726166743a76616e696c6c61156d696e6563726166743a7570646174655f315f3230166578616d706c653a6675747572655f66656174757265", new string[] { "minecraft:vanilla", "minecraft:update_1_20", "example:future_feature" }];
        yield return [763, "00", new string[] { }];
        yield return [763, "01116d696e6563726166743a76616e696c6c61", new string[] { "minecraft:vanilla" }];
        yield return [763, "03116d696e6563726166743a76616e696c6c61156d696e6563726166743a7570646174655f315f3230166578616d706c653a6675747572655f66656174757265", new string[] { "minecraft:vanilla", "minecraft:update_1_20", "example:future_feature" }];
        yield return [764, "00", new string[] { }];
        yield return [764, "01116d696e6563726166743a76616e696c6c61", new string[] { "minecraft:vanilla" }];
        yield return [764, "03116d696e6563726166743a76616e696c6c61156d696e6563726166743a7570646174655f315f3230166578616d706c653a6675747572655f66656174757265", new string[] { "minecraft:vanilla", "minecraft:update_1_20", "example:future_feature" }];
    }

    [Theory]
    [MemberData(nameof(NativeBodies))]
    public void OriginalWriterBodyReachesTheRealPhaseBinding(int protocol, string hex, string[] names)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        ProtocolPhase phase = protocol < 764 ? ProtocolPhase.Play : ProtocolPhase.Configuration;
        Assert.True(version.Protocol.TryGetRegistry(phase, PacketFlow.Clientbound, out var registry));
        var entry = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("update_enabled_features"));
        Assert.True(registry.TryGetInbound(entry.WireId, out var bound));
        Assert.True(bound.IsImplemented);
        byte[] body = Convert.FromHexString(hex);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(bound.Decode(body, context));
        Assert.Equal(names.Select(Identifier.Parse), packet.Features);
        Assert.Equal(phase, packet.Type.Phase);
        Assert.Equal(PacketFlow.Clientbound, packet.Type.Flow);
        Assert.Equal(Identifier.Minecraft("update_enabled_features"), packet.Type.Id);
        Assert.Equal(ProtocolPhase.Configuration, new ClientboundConfigUpdateEnabledFeaturesPacket([]).Type.Phase);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        bound.Encode(ref writer, packet, context);
        Assert.Equal(body, output.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData(760, ProtocolPhase.Play)]
    [InlineData(764, ProtocolPhase.Play)]
    [InlineData(763, ProtocolPhase.Configuration)]
    public void ThePacketDoesNotLeakAcrossItsNativePhaseBoundary(int protocol, ProtocolPhase phase)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        if (version.Protocol.TryGetRegistry(phase, PacketFlow.Clientbound, out var registry))
            Assert.DoesNotContain(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("update_enabled_features"));
    }
}
