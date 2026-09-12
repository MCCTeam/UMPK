using Umpk.Data.Java;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Catalog-wide coverage for the connection-flow obligations: the packets a session cannot do without, walked over the SHIPPED version catalog rather than a hand-listed set of protocols. A version added later fails here rather than shipping with the same registered-but-never-implemented hole.</summary>
/// <remarks>The rule this encodes: for a packet in this list, a protocol that REGISTERS the identifier must also bind a codec for it. A marker means the frame is recognised and discarded, which for these packets is indistinguishable from the server having said nothing.</remarks>
public sealed class ServerboundObligationCoverageTests
{
    public static TheoryData<int> Protocols
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (int protocol in JavaVersions.All.Select(v => v.Version.Protocol).Distinct().Order())
                data.Add(protocol);

            return data;
        }
    }

    /// <summary>(phase, flow, identifier) tuples that must never be markers where the dataset registers them.</summary>
    private static readonly (ProtocolPhase Phase, PacketFlow Flow, string Id)[] Required =
    [
        // The kick reason, in both phases. A marker here throws away the only explanation a consumer ever gets for a server-initiated disconnect.
        (ProtocolPhase.Play, PacketFlow.Clientbound, "disconnect"),
        (ProtocolPhase.Configuration, PacketFlow.Clientbound, "disconnect"),

        // The respawn request. Vanilla has no other path off the death screen.
        (ProtocolPhase.Play, PacketFlow.Serverbound, "client_command"),

        // The level-loaded and end-of-tick announcements the 1.21 servers gate movement and movement bookkeeping on.
        (ProtocolPhase.Play, PacketFlow.Serverbound, "player_loaded"),
        (ProtocolPhase.Play, PacketFlow.Serverbound, "client_tick_end"),

        // The cookie/transfer common packets, in every phase that carries them. An unanswered cookie request stalls the proxies that use it; an ignored transfer silently drops the handoff.
        (ProtocolPhase.Play, PacketFlow.Clientbound, "cookie_request"),
        (ProtocolPhase.Play, PacketFlow.Serverbound, "cookie_response"),
        (ProtocolPhase.Play, PacketFlow.Clientbound, "store_cookie"),
        (ProtocolPhase.Play, PacketFlow.Clientbound, "transfer"),
        (ProtocolPhase.Configuration, PacketFlow.Clientbound, "cookie_request"),
        (ProtocolPhase.Configuration, PacketFlow.Serverbound, "cookie_response"),
        (ProtocolPhase.Configuration, PacketFlow.Clientbound, "store_cookie"),
        (ProtocolPhase.Configuration, PacketFlow.Clientbound, "transfer"),
        (ProtocolPhase.Login, PacketFlow.Clientbound, "cookie_request"),
        (ProtocolPhase.Login, PacketFlow.Serverbound, "cookie_response"),

        // The pre-1.17 window transaction, in both directions. Without the echo vanilla's server drops every container click after the first rejected one. The identity is minecraft:transaction on every protocol that has it: the 1.9-1.16.4 dataset name container_ack is an alias onto the same timeline, so the descriptor reports the canonical name.
        (ProtocolPhase.Play, PacketFlow.Clientbound, "transaction"),
        (ProtocolPhase.Play, PacketFlow.Serverbound, "transaction"),
    ];

    [Theory]
    [MemberData(nameof(Protocols))]
    public void ConnectionObligations_AreNeverMarkers(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;

        foreach ((ProtocolPhase phase, PacketFlow flow, string name) in Required)
        {
            if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                continue;

            var id = Identifier.Minecraft(name);
            foreach ((int wireId, PacketType type) in registry.Packets)
            {
                if (type.Id != id)
                    continue;

                Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
                Assert.True(
                    bound.IsImplemented,
                    $"minecraft:{name} ({phase}/{flow}) is a marker at protocol {protocol}: the frame would be recognised and discarded.");
            }
        }
    }

    /// <summary>The two obligations that exist on the whole supported range must resolve on every protocol, not merely be non-markers where they happen to be registered.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void DisconnectAndClientCommand_ExistOnEveryProtocol(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;

        Assert.True(
            IsImplemented(descriptor, ProtocolPhase.Play, PacketFlow.Clientbound, "disconnect"),
            $"play disconnect must be implemented at protocol {protocol}");
        Assert.True(
            IsImplemented(descriptor, ProtocolPhase.Play, PacketFlow.Serverbound, "client_command"),
            $"client_command must be implemented at protocol {protocol}");
    }

    private static bool IsImplemented(ProtocolDescriptor descriptor, ProtocolPhase phase, PacketFlow flow, string name)
    {
        if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
            return false;

        var id = Identifier.Minecraft(name);
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id && registry.TryGetInbound(wireId, out BoundPacketCodec bound) && bound.IsImplemented)
                return true;

        return false;
    }
}
