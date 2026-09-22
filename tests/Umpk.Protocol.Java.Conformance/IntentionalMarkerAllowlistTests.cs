using System.Text;
using Umpk.Data.Java;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The marker-consistency gate: every registered-but-unimplemented packet in the supported catalog must be declared in <see cref="IntentionalMarkers"/> with a reason, and every declaration must still describe a marker that exists. Both directions fail the build.</summary>
/// <remarks>
/// <para>An undeclared marker is a packet registered with a real wire id and no codec. It is recognised, relayed verbatim and never decoded, so the consumer sees an empty collection or an unchanged value, which is indistinguishable from the server having sent nothing. No exception exposes the omission.</para>
/// <para>The reverse direction matters just as much. If an entry names protocols that are no longer markers, its reason is stale. Therefore the comparison is set equality per identity, and implementing a marker requires removing its allowance.</para>
/// </remarks>
public sealed class IntentionalMarkerAllowlistTests
{
    /// <summary>Words that mean the author had nothing to say. A reason containing one is not a reason.</summary>
    private static readonly string[] PlaceholderWords =
        ["todo", "tbd", "fixme", "xxx", "placeholder", "unknown", "not sure", "n/a", "see above", "ditto"];

    /// <summary>How many of the 71 allowances the binding surface declares today. Twenty is where the first pass stopped: every allowance whose reason is a decision, plus nothing from the backlog yet.</summary>
    private const int DeclaredRatchet = 20;

    /// <summary>Every marker in the catalog is declared, and every declaration matches the catalog exactly. One test rather than one per protocol: the failure is a diff of two sets, and splitting it per protocol would report the same missing entry forty-nine times.</summary>
    [Fact]
    public void EveryMarker_IsDeclaredIntentional_AndEveryDeclarationStillDescribesOne()
    {
        Dictionary<(ProtocolPhase, PacketFlow, string), SortedSet<int>> actual = ActualMarkers();
        var declared = IntentionalMarkers.All.ToDictionary(
            a => (a.Phase, a.Flow, a.Identifier),
            a => new SortedSet<int>(a.Protocols));

        Assert.Equal(
            IntentionalMarkers.All.Count,
            declared.Count);

        var problems = new StringBuilder();
        foreach (KeyValuePair<(ProtocolPhase, PacketFlow, string), SortedSet<int>> pair in
            actual.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2).ThenBy(kv => kv.Key.Item3, StringComparer.Ordinal))
        {
            (ProtocolPhase phase, PacketFlow flow, string id) = pair.Key;
            SortedSet<int> protocols = pair.Value;
            if (!declared.TryGetValue(pair.Key, out SortedSet<int>? allowed))
            {
                problems
                    .Append("UNDECLARED marker ").Append(phase).Append('/').Append(flow).Append(' ')
                    .Append("minecraft:").Append(id).Append(" on ").Append(Render(protocols)).Append('\n');
                continue;
            }

            if (!allowed.SetEquals(protocols))
            {
                IEnumerable<int> gained = allowed.Except(protocols);
                IEnumerable<int> missing = protocols.Except(allowed);
                problems
                    .Append("STALE entry ").Append(phase).Append('/').Append(flow).Append(' ')
                    .Append("minecraft:").Append(id)
                    .Append(": declared ").Append(Render(allowed))
                    .Append(", actual ").Append(Render(protocols))
                    .Append(" (no longer a marker on ").Append(Render(gained))
                    .Append("; newly a marker on ").Append(Render(missing)).Append(")\n");
            }
        }

        foreach ((ProtocolPhase, PacketFlow, string) key in declared.Keys
            .Where(k => !actual.ContainsKey(k))
            .OrderBy(k => k.Item1).ThenBy(k => k.Item2).ThenBy(k => k.Item3, StringComparer.Ordinal))
        {
            (ProtocolPhase phase, PacketFlow flow, string id) = key;
            problems
                .Append("DEAD entry ").Append(phase).Append('/').Append(flow).Append(' ')
                .Append("minecraft:").Append(id)
                .Append(" is allowlisted but is not a marker anywhere; delete it.\n");
        }

        Assert.True(
            problems.Length == 0,
            "The intentional-marker allowlist does not match the catalog.\n"
            + "A new marker must be declared in IntentionalMarkers.cs with a reason; a marker that gained a\n"
            + "codec must leave the list. There is deliberately no regeneration switch for that file.\n\n"
            + problems);
    }

    /// <summary>A reason has to be a reason. Length, terminal punctuation, no placeholder words, and ASCII: cheap checks, but they are what stops the list becoming a column of identifiers with the word "intentional" beside each one.</summary>
    [Fact]
    public void EveryAllowance_CarriesAUsableReason()
    {
        var problems = new StringBuilder();
        foreach (MarkerAllowance a in IntentionalMarkers.All)
        {
            string where = $"{a.Phase}/{a.Flow} minecraft:{a.Identifier}";
            if (a.Protocols.Count == 0)
                problems.Append(where).Append(": declares no protocols.\n");

            if (a.Protocols.Distinct().Count() != a.Protocols.Count)
                problems.Append(where).Append(": declares a protocol twice.\n");

            AppendProseProblems(problems, where, a.Why);
        }

        Assert.True(problems.Length == 0, "Allowlist reasons are not usable:\n" + problems);
    }

    /// <summary>The same set equality read from the other end: the binding surface. A marker a binding file DECLARES must be a marker this file knows about, on exactly the protocols this file names, for the reason this file gives.</summary>
    /// <remarks>
    /// <para>The check above proves the allowlist describes the catalog. It cannot prove anything about the registration layer, because almost every marker gets there by omission: no timeline key, a <c>TryGetValue</c> miss, a marker entry, and not one line anywhere saying so. That is why "is this packet bound?" cannot be answered by grepping for silence: a forgotten registration line and a deliberate absence read identically in the file where the packet lives.</para>
    /// <para>An explicit declaration makes the omission reviewable, and this gate checks its protocol band and reason. A declaration with the wrong band or reason fails here. The reason vocabulary also draws the line for what MUST be declared: every reason but <see cref="MarkerReason.KnownGap"/> records a decision somebody made, and a decision belongs where it was made. <see cref="MarkerReason.KnownGap"/> is the backlog, and the backlog is a ratchet.</para>
    /// </remarks>
    [Fact]
    public void EveryDeclaredMarker_AgreesWithItsAllowance()
    {
        Dictionary<(ProtocolPhase, PacketFlow, string), DeclaredMarker> declared = DeclaredMarkers();
        Dictionary<(ProtocolPhase, PacketFlow, string), MarkerAllowance> allowed =
            IntentionalMarkers.All.ToDictionary(a => (a.Phase, a.Flow, a.Identifier));

        var problems = new StringBuilder();
        foreach (KeyValuePair<(ProtocolPhase, PacketFlow, string), DeclaredMarker> pair in declared
            .OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2).ThenBy(kv => kv.Key.Item3, StringComparer.Ordinal))
        {
            (ProtocolPhase phase, PacketFlow flow, string id) = pair.Key;
            string where = $"{phase}/{flow} minecraft:{id}";
            MarkerDeclaration[] distinct = [.. pair.Value.Declarations.Distinct()];
            if (distinct.Length > 1)
            {
                problems
                    .Append(where).Append(": declares ").Append(distinct.Length)
                    .Append(" different reasons across its band; an allowance carries one.\n");
                continue;
            }

            MarkerDeclaration declaration = distinct[0];
            AppendProseProblems(problems, where, declaration.Why);
            if (!allowed.TryGetValue(pair.Key, out MarkerAllowance? allowance))
            {
                problems
                    .Append(where).Append(" is declared a deliberate marker from ")
                    .Append(declaration.FromProtocol).Append(" but is not in this allowlist.\n");
                continue;
            }

            if (allowance.Reason != declaration.Reason)
                problems
                    .Append(where).Append(": declared ").Append(declaration.Reason)
                    .Append(", allowlisted ").Append(allowance.Reason).Append(".\n");

            if (!pair.Value.Protocols.SetEquals(allowance.Protocols))
                problems
                    .Append(where).Append(": the declaration governs ").Append(Render(pair.Value.Protocols))
                    .Append(", the allowlist says ").Append(Render(allowance.Protocols.Order())).Append(".\n");

        }

        foreach (MarkerAllowance a in IntentionalMarkers.All
            .Where(a => a.Reason != MarkerReason.KnownGap)
            .Where(a => !declared.ContainsKey((a.Phase, a.Flow, a.Identifier)))
            .OrderBy(a => a.Phase).ThenBy(a => a.Flow).ThenBy(a => a.Identifier, StringComparer.Ordinal))
            problems
                .Append(a.Phase).Append('/').Append(a.Flow).Append(" minecraft:").Append(a.Identifier)
                .Append(" is allowlisted as ").Append(a.Reason)
                .Append(", which is a decision, and no binding file declares it.\n");

        Assert.True(
            problems.Length == 0,
            "The binding surface and the intentional-marker allowlist disagree.\n"
            + "A declared marker is written where the packet is bound, with the reason and the band this\n"
            + "file gives it; a reason that is not KnownGap has to be declared there at all.\n\n"
            + problems);

        // A ratchet, not a pin: backfilling an absence raises this and nothing has to be edited, while deleting a declaration that is already written down fails. The number moves up only.
        Assert.True(
            declared.Count >= DeclaredRatchet,
            $"{declared.Count} declared markers, and {DeclaredRatchet} were already written down.");
    }

    /// <summary>The declared-deliberate reasons are the load-bearing ones, so they are pinned by count. A change that turns a KnownGap into a WrongCodecWouldBeWorse (or the reverse) has to say so here. This is the one place to look to see whether the honest-gap total is moving in the right direction.</summary>
    [Fact]
    public void ReasonMix_MatchesTheRecordedSplit()
    {
        Dictionary<MarkerReason, int> byReason = IntentionalMarkers.All
            .GroupBy(a => a.Reason)
            .ToDictionary(g => g.Key, g => g.Count());

        // These exact totals make every marker addition, removal, and reason change an explicit update.
        Assert.Equal(71, IntentionalMarkers.All.Count);
        Assert.Equal(1146, IntentionalMarkers.All.Sum(a => a.Protocols.Count));
        Assert.Equal(2, byReason[MarkerReason.HandledElsewhere]);
        Assert.Equal(4, byReason[MarkerReason.WrongCodecWouldBeWorse]);
        Assert.Equal(2, byReason[MarkerReason.DatasetArtifact]);
        Assert.Equal(12, byReason[MarkerReason.NoConsumerWorthWriting]);
        Assert.Equal(51, byReason[MarkerReason.KnownGap]);
    }

    private static Dictionary<(ProtocolPhase, PacketFlow, string), SortedSet<int>> ActualMarkers()
    {
        var result = new Dictionary<(ProtocolPhase, PacketFlow, string), SortedSet<int>>();
        foreach (int protocol in JavaVersions.All.Select(v => v.Version.Protocol).Distinct().Order())
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
            ProtocolDescriptor descriptor = version.Protocol;
            foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
                foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
                {
                    if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                        continue;

                    foreach ((int wireId, PacketType type) in registry.Packets)
                    {
                        if (!registry.TryGetInbound(wireId, out BoundPacketCodec entry) || entry.IsImplemented)
                            continue;

                        (ProtocolPhase phase, PacketFlow flow, string) key = (phase, flow, type.Id.Path);
                        if (!result.TryGetValue(key, out SortedSet<int>? set))
                        {
                            set = [];
                            result[key] = set;
                        }

                        set.Add(protocol);
                    }
                }

        }

        return result;
    }

    /// <summary>Every marker a binding file declared, with the protocols each declaration governs.</summary>
    private static Dictionary<(ProtocolPhase, PacketFlow, string), DeclaredMarker> DeclaredMarkers()
    {
        var result = new Dictionary<(ProtocolPhase, PacketFlow, string), DeclaredMarker>();
        foreach (int protocol in JavaVersions.All.Select(v => v.Version.Protocol).Distinct().Order())
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
            ProtocolDescriptor descriptor = version.Protocol;
            foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
                foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
                {
                    if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                        continue;

                    foreach ((int wireId, PacketType type) in registry.Packets)
                    {
                        if (!registry.TryGetInbound(wireId, out BoundPacketCodec entry)
                            || entry.Declaration is not { } declaration)
                            continue;

                        (ProtocolPhase phase, PacketFlow flow, string) key = (phase, flow, type.Id.Path);
                        if (!result.TryGetValue(key, out DeclaredMarker? marker))
                        {
                            marker = new DeclaredMarker([], []);
                            result[key] = marker;
                        }

                        marker.Protocols.Add(protocol);
                        if (!marker.Declarations.Contains(declaration))
                            marker.Declarations.Add(declaration);

                    }
                }

        }

        return result;
    }

    private static void AppendProseProblems(StringBuilder problems, string where, string why)
    {
        if (why.Length < 80)
            problems.Append(where).Append(": reason is too short to be one.\n");

        if (!why.EndsWith('.'))
            problems.Append(where).Append(": reason does not end in a full stop.\n");

        foreach (string word in PlaceholderWords)
            if (why.Contains(word, StringComparison.OrdinalIgnoreCase))
                problems.Append(where).Append(": reason contains the placeholder '").Append(word).Append("'.\n");

        foreach (char c in why)
            if (c > 0x7E || c < 0x20)
            {
                problems.Append(where).Append(": reason contains a non-ASCII character.\n");
                break;
            }

    }

    private static string Render(IEnumerable<int> protocols)
    {
        string joined = string.Join(", ", protocols);
        return joined.Length == 0 ? "(none)" : joined;
    }

    /// <summary>One identity's declared markers: the protocols they govern, and the declarations seen.</summary>
    private sealed record DeclaredMarker(SortedSet<int> Protocols, List<MarkerDeclaration> Declarations);
}
