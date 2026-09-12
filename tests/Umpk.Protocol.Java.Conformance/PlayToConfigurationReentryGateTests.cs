using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Protocol 764-767 descriptor gates: the Play-to-Configuration re-entry pair (<c>minecraft:start_configuration</c> clientbound / <c>minecraft:configuration_acknowledged</c> serverbound) must be real empty-payload codecs marked terminal into the configuration phase on every configuration-phase version, including 770/776.</summary>
public sealed class PlayToConfigurationReentryGateTests
{
    public static TheoryData<string> GateVersions => new() { "V1_20_2", "V1_20_3", "V1_20_5", "V1_21", "V1_21_5", "V26_2" };

    [Theory]
    [MemberData(nameof(GateVersions))]
    public void PlayToConfiguration_Gates_AreTerminal(string versionName)
    {
        JavaVersion version = versionName switch
        {
            "V1_20_2" => JavaVersions.V1_20_2,
            "V1_20_3" => JavaVersions.V1_20_3,
            "V1_20_5" => JavaVersions.V1_20_5,
            "V1_21" => JavaVersions.V1_21,
            "V1_21_5" => JavaVersions.V1_21_5,
            _ => JavaVersions.V26_2,
        };

        AssertTerminal(version, PacketFlow.Clientbound, PlayPackets.Clientbound.StartConfiguration.Id);
        AssertTerminal(version, PacketFlow.Serverbound, PlayPackets.Serverbound.ConfigurationAcknowledged.Id);
    }

    private static void AssertTerminal(JavaVersion version, PacketFlow flow, Identifier id)
    {
        Assert.True(version.Protocol.TryGetRegistry(ProtocolPhase.Play, flow, out PhaseRegistry registry));
        int wireId = -1;
        foreach ((int candidate, PacketType type) in registry.Packets)
            if (type.Id == id)
            {
                wireId = candidate;
                break;
            }

        Assert.True(wireId >= 0, $"{id} is not registered in {flow} play .");
        registry.TryGetInbound(wireId, out BoundPacketCodec entry);
        Assert.True(entry.IsImplemented, $"{id} must be a real codec, not a marker.");
        Assert.True(version.Protocol.TryGetTerminalTransition(ProtocolPhase.Play, flow, wireId, out ProtocolPhase next));
        Assert.Equal(ProtocolPhase.Configuration, next);
    }
}
