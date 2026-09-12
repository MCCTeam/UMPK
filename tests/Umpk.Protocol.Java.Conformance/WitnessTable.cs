using System.Globalization;
using System.Text;
using Umpk.Data.Java;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Renders the witness pin: one line per implemented binding of a protocol, carrying what the bound codec made of that band's witness payload and which neighbouring eras could not make the same of it.</summary>
/// <remarks>The first five fields are exactly the codec-identity pin's, so the two files line up and the added columns can be stripped back to it; <see cref="WitnessPinTests.TheWitnessPin_ProjectsOntoTheCodecIdentityPin"/> is what holds that true rather than the eye.</remarks>
internal static class WitnessTable
{
    private static readonly string[] HeaderLines =
    [
        "# The witness pin: what each implemented binding of this protocol DECODED from a payload chosen",
        "# so the neighbouring eras cannot decode it the same way.",
        "#",
        "# The first five fields are the codec-identity pin's. Then:",
        "#   src:     corpus (real bytes from a committed capture inside this band), synth (an authored",
        "#            value minted through this band's own encoder), or none (no witness authored yet).",
        "#   w:       bytes the bound decoder consumed, on a payload that discriminates rather than on",
        "#            the codec-identity pin's all-zero probe.",
        "#   ok <..>  a digest of the decoded VALUES, clipped, keyed to the whole by h=<hash> when it is.",
        "#   reject:  the nearest differently-bound band on each side, named by its first protocol, and",
        "#            what it did instead: a fault, a different consumed count, or the first field it",
        "#            disagreed about.",
        "#   same:    that band read this payload identically, and the pair is separated by THAT band's",
        "#            own witness instead. Separation belongs to the pair; only one side can carry it",
        "#            where one era accepts everything the other can encode.",
        "#   twin:    neither side's payload separates the pair, and it is declared in IntentionalTwins.",
        "#",
        "# A witness describes a BAND, so every protocol in one renders the same three columns. Payloads",
        "# are authored (WitnessCatalog); this file is rendered. Regenerate with UMPK_UPDATE_WITNESS_PINS=1.",
    ];

    /// <summary>Renders one protocol's frozen witness table.</summary>
    internal static string Render(int protocol)
    {
        IReadOnlyDictionary<ProtocolTimeline.PacketKey, ResolvedWitness> witnesses = Witnesses.For(protocol);
        List<(ProtocolPhase Phase, PacketFlow Flow, int WireId, string Line, string? Clause)> lines = [];
        int implemented = 0;
        int corpus = 0;
        int synthesized = 0;

        foreach ((ProtocolPhase phase, PacketFlow flow, int wireId, BoundPacketCodec entry) in Walk(protocol))
        {
            if (!entry.IsImplemented)
                continue;

            implemented++;
            var key = new ProtocolTimeline.PacketKey(phase, flow, entry.Type.Id.ToString());
            string prefix = $"{phase} {flow} 0x{wireId:X2} {entry.Type.Id} {entry.CodecIdentity}";
            if (!witnesses.TryGetValue(key, out ResolvedWitness? witness))
            {
                lines.Add((phase, flow, wireId, $"{prefix} src:none", null));
                continue;
            }

            if (witness.Source == WitnessSource.Corpus)
                corpus++;

            else
                synthesized++;

            lines.Add((
                phase,
                flow,
                wireId,
                $"{prefix} src:{Source(witness)} w:{witness.Consumed.ToString(CultureInfo.InvariantCulture)} " +
                $"ok {WitnessDigest.Render(witness.Canonical)}",
                Clause(witness)));
        }

        lines.Sort(static (a, b) =>
        {
            int c = a.Phase.CompareTo(b.Phase);
            if (c != 0)
                return c;

            c = a.Flow.CompareTo(b.Flow);
            return c != 0 ? c : a.WireId.CompareTo(b.WireId);
        });

        var sb = new StringBuilder();
        sb.Append("# protocol ").Append(protocol.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (string header in HeaderLines)
            sb.Append(header).Append('\n');

        sb.Append("# witnessed ").Append((corpus + synthesized).ToString(CultureInfo.InvariantCulture))
            .Append(" of ").Append(implemented.ToString(CultureInfo.InvariantCulture))
            .Append(" implemented bindings (corpus ").Append(corpus.ToString(CultureInfo.InvariantCulture))
            .Append(", synth ").Append(synthesized.ToString(CultureInfo.InvariantCulture)).Append(")\n");
        sb.Append("witnesses:\n");
        foreach ((_, _, _, string line, string? clause) in lines)
        {
            sb.Append(line).Append('\n');
            if (clause is not null)
                sb.Append("    ").Append(clause).Append('\n');

        }

        return sb.ToString();
    }

    /// <summary>The packet and codec fields of one protocol's implemented bindings, in pin order.</summary>
    internal static IEnumerable<(ProtocolPhase Phase, PacketFlow Flow, int WireId, BoundPacketCodec Entry)> Walk(
        int protocol)
    {
        if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version))
            yield break;

        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!version!.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType _) in registry.Packets)
                    if (registry.TryGetInbound(wireId, out BoundPacketCodec entry))
                        yield return (phase, flow, wireId, entry);

            }

    }

    private static string Source(ResolvedWitness witness) =>
        witness.Source == WitnessSource.Corpus
            ? $"corpus@{witness.MintedAt.ToString(CultureInfo.InvariantCulture)}"
            : "synth";

    private static string Clause(ResolvedWitness witness)
    {
        if (witness.Neighbours.Count == 0)
            return "reject:none (this band has no differently-bound neighbour)";

        return string.Join(
            ' ',
            witness.Neighbours.Select(static n =>
            {
                string protocol = n.Protocol.ToString(CultureInfo.InvariantCulture);
                return n.Rejected ? $"reject:P{protocol}({n.Detail})"
                    : n.Mutual ? $"same:P{protocol}"
                    : $"twin:P{protocol}";
            }));
    }
}
