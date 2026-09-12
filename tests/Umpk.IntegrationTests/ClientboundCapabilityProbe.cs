using System.Text;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

/// <summary>Non-live diagnostic: for the mid eras (protocols 477-763, MC 1.14-1.20.1) reports every CLIENTBOUND Play packet and whether its codec is IMPLEMENTED (decodable into a typed packet) or a verbatim marker. The marker list is the receive-side work list for the mid-era receive-parity effort. Run with <c>--filter Report_Clientbound_Play_ReceiveCapability_Mid_Era</c>.</summary>
public sealed class ClientboundCapabilityProbe
{
    private readonly ITestOutputHelper _out;

    public ClientboundCapabilityProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Report_Clientbound_Play_ReceiveCapability_Mid_WireLayout()
    {
        // The receive-side features of interest for the mid-era parity work.
        string[] focus =
        [
            "level_chunk_with_light", "level_chunk", "chat", "system_chat", "player_chat", "disguised_chat",
            "update_mob_effect", "remove_mob_effect", "add_entity", "add_mob", "set_entity_data",
            "block_update", "set_time", "player_position",
        ];

        foreach ((string version, int protocol) in LiveMatrix.Representatives)
        {
            if (protocol < 477 || protocol > 763)
                continue;

            if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion? v) || v is null)
                continue;

            PhaseRegistry cb = v.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            int implemented = 0;
            int markers = 0;
            var markerNames = new List<string>();
            foreach ((int wireId, PacketType type) in cb.Packets)
            {
                bool impl = cb.TryGetInbound(wireId, out BoundPacketCodec codec) && codec.IsImplemented;
                if (impl)
                    implemented++;

                else
                {
                    markers++;
                    markerNames.Add(type.Id.Path);
                }
            }

            var sb = new StringBuilder();
            sb.Append(version).Append(" (").Append(protocol).Append(") clientbound: ")
                .Append(implemented).Append(" impl / ").Append(markers).Append(" marker | focus: ");
            foreach (string n in focus)
                sb.Append(n).Append('=').Append(Impl(cb, Identifier.Minecraft(n))).Append(' ');

            _out.WriteLine(sb.ToString());
            _out.WriteLine($"  MARKERS[{version}]: {string.Join(", ", markerNames)}");
        }
    }

    private static string Impl(PhaseRegistry registry, Identifier id)
    {
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id && registry.TryGetInbound(wireId, out BoundPacketCodec codec))
                return codec.IsImplemented ? "impl" : "marker";

        return "absent";
    }
}
