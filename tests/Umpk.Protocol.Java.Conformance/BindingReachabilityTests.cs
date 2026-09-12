using System.Text;
using Umpk.Data.Java;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The mirror of the marker gate. A marker is a wire id with no codec; this is the opposite shape, a codec with no wire id: a timeline (or an alias onto one) that no supported protocol ever resolves, so the codec behind it is written, tested in isolation, and unreachable on every real session.</summary>
/// <remarks>
/// <para>This covers alias keys as well as canonical ones. The descriptor renders the canonical packet name, so <see cref="BoundPacketCodec.DatasetIdentifier"/> records the identifier that actually resolved the binding. An alias is reachable exactly when some protocol has a binding that names it.</para>
/// </remarks>
public sealed class BindingReachabilityTests
{
    [Fact]
    public void EveryDeclaredBinding_ResolvesOnAtLeastOneSupportedProtocol()
    {
        int[] protocols = [.. JavaVersions.All.Select(v => v.Version.Protocol).Distinct().Order()];
        var descriptors = new List<ProtocolDescriptor>(protocols.Length);
        foreach (int protocol in protocols)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
            descriptors.Add(version.Protocol);
        }

        var unreachable = new StringBuilder();
        foreach (KeyValuePair<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id), TimelineBinding> pair in
            PacketBindings.Table
                .OrderBy(kv => kv.Key.Phase).ThenBy(kv => kv.Key.Flow)
                .ThenBy(kv => kv.Key.Id.ToString(), StringComparer.Ordinal))
        {
            (ProtocolPhase phase, PacketFlow flow, Identifier id) = pair.Key;
            if (!descriptors.Any(d => ResolvesImplemented(d, phase, flow, id)))
                unreachable
                    .Append(phase).Append('/').Append(flow).Append(' ').Append(id)
                    .Append(pair.Value.IsAlias ? " (alias)" : string.Empty)
                    .Append(" is declared in the binding table but no supported protocol resolves an implemented codec for it.\n");

        }

        Assert.True(
            unreachable.Length == 0,
            "Declared bindings that nothing can reach (a codec with no wire id is as inert as a wire id\n"
            + "with no codec, and it looks like coverage while it sits there):\n\n" + unreachable);
    }

    /// <summary>Whether any packet in the descriptor resolved an implemented codec under exactly this identifier. The match is on <see cref="BoundPacketCodec.DatasetIdentifier"/>, the key that actually resolved the binding, not on the packet's canonical identity: matching on the canonical identity would report every alias as reachable the moment its target timeline resolved anywhere, which is the blind spot this check exists to close.</summary>
    private static bool ResolvesImplemented(
        ProtocolDescriptor descriptor,
        ProtocolPhase phase,
        PacketFlow flow,
        Identifier id)
    {
        if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
            return false;

        foreach ((int wireId, PacketType _) in registry.Packets)
            if (registry.TryGetInbound(wireId, out BoundPacketCodec entry)
                && entry.IsImplemented
                && entry.DatasetIdentifier == id)
                return true;

        return false;
    }
}
