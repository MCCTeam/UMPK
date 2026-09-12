using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

/// <summary>Non-live diagnostic: reports, per protocol, whether the key serverbound Play packets the high-level client must send (keep-alive answer, teleport confirm, chat, movement, dig) have an IMPLEMENTED codec or are verbatim markers. A marker cannot be encoded from a typed packet, so on those versions the high-level client cannot answer keep-alive and the server times it out. This maps the send-capable boundary that the live matrix classification is derived from.</summary>
public sealed class ServerboundCapabilityProbe
{
    private readonly ITestOutputHelper _out;

    public ServerboundCapabilityProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Report_Serverbound_Play_SendCapability_Per_Protocol()
    {
        string[] names = ["keep_alive", "accept_teleportation", "chat", "move_player_pos", "player_action"];
        foreach ((string version, int protocol) in LiveMatrix.Representatives)
        {
            if (!Umpk.Data.Java.JavaVersions.TryGetByProtocol(protocol, out JavaVersion? v) || v is null)
                continue;

            PhaseRegistry sb = v.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            var flags = new List<string>();
            foreach (string n in names)
                flags.Add($"{n}={Impl(sb, Identifier.Minecraft(n))}");

            bool keepAlive = IsImplemented(sb, Identifier.Minecraft("keep_alive"));
            _out.WriteLine($"{version} ({protocol}) sendCapable={keepAlive} | {string.Join(" ", flags)}");
        }
    }

    private static string Impl(PhaseRegistry registry, Identifier id) => IsImplemented(registry, id) ? "impl" : "marker";

    private static bool IsImplemented(PhaseRegistry registry, Identifier id)
    {
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id && registry.TryGetOutbound(type, out _, out BoundPacketCodec codec))
                return codec.IsImplemented;

        return false;
    }
}
