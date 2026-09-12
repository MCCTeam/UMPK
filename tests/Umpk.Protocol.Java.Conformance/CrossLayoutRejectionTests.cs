using Umpk.Data.Java;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The assertion the witness pin exists for: every witness must be REJECTED by the nearest differently-bound band on each side, or the two bands must be declared twins.</summary>
/// <remarks>
/// <para>A pin freezes what is; this decides whether what is means anything. A witness whose neighbours happily decode it to the same values proves nothing about the binding it pins, and it is worse than no witness at all because the fixture makes it look like coverage. So the harness runs each band's payload through the neighbouring bands and requires a difference: a fault, a different consumed count, or a different decoded value. Only a genuine byte-for-byte twin is allowed to agree, and only by being written into <see cref="IntentionalTwins"/> with the vanilla reading behind it.</para>
/// <para>One row per protocol, per the counting convention; the rejection clauses are assertions inside a row. That keeps this suite's contribution to the pinned totals a function of the protocol catalog rather than of how many pairs a tranche happens to reach.</para>
/// </remarks>
public sealed class CrossLayoutRejectionTests
{
    public static IEnumerable<object[]> Protocols =>
        JavaVersions.All.Select(static v => new object[] { v.Version.Protocol }).Distinct();

    [Theory]
    [MemberData(nameof(Protocols))]
    public void EveryWitness_IsRejectedByItsNeighbouringBands(int protocol)
    {
        IReadOnlyDictionary<ProtocolTimeline.PacketKey, ResolvedWitness> witnesses = Witnesses.For(protocol);

        List<string> complaints = [];
        HashSet<(ProtocolPhase, PacketFlow, string, int, int)> observed = [];
        foreach (ResolvedWitness witness in witnesses.Values)
        {
            if (witness.Neighbours.Count == 0)
            {
                complaints.Add(
                    $"{witness.Key} band {ProtocolTimeline.RangeOf(witness.Band)} has no differently-bound " +
                    "neighbour, so its witness asserts nothing; it does not belong in the catalog.");
                continue;
            }

            foreach (NeighbourVerdict verdict in witness.Neighbours)
            {
                if (verdict.Separated)
                    continue;

                (ProtocolPhase, PacketFlow, string, int, int) pair = Pair(witness, verdict.Protocol);
                observed.Add(pair);
                if (!IntentionalTwins.Pairs.Contains(pair))
                    complaints.Add(
                        $"{witness.Key}: bands P{witness.Band.First} and P{verdict.Protocol} both read this " +
                        $"witness as {WitnessDigest.Render(witness.Canonical)}, so it does not distinguish " +
                        "them. Author a payload that does, or declare the pair in IntentionalTwins with the " +
                        "vanilla reading.");

            }
        }

        // The other direction of the exact set: a declaration that is no longer true must fail.
        foreach (TwinDeclaration twin in IntentionalTwins.All)
        {
            if (!Applies(twin, protocol) || observed.Contains(twin.Pair))
                continue;

            complaints.Add(
                $"{twin.Phase}/{twin.Flow} {twin.Identifier}: bands P{twin.Band} and P{twin.Twin} are " +
                "declared twins, and at this protocol the witness separates them. Remove the declaration.");
        }

        // Every catalogued packet that is actually bound here must have produced a witness. Without this a mistyped identifier, or a packet whose bands stopped resolving, would silently contribute nothing and the suite would still be green.
        int bound = 0;
        foreach (WitnessedPacket declared in Witnesses.Catalog)
        {
            BoundPacketCodec? codec = Witnesses.Bind(protocol, declared.Key);
            if (codec is null || !codec.IsImplemented)
                continue;

            bound++;
            if (!witnesses.ContainsKey(declared.Key))
                complaints.Add(
                    $"{declared.Key} is bound at protocol {protocol} and the catalog declares it, but no " +
                    "witness resolved for it: no capture inside its band carries the packet and it has no " +
                    "authored value.");

        }

        Assert.True(complaints.Count == 0, string.Join('\n', complaints.Take(24)));

        // The tranche is a number rather than a habit: a packet that left the catalog would take its rejection clauses with it and nothing else would notice.
        Assert.Equal(WitnessCatalog.TrancheOne, Witnesses.Catalog.Count);

        // The debt is counted too. A deferred packet that quietly became witnessed, or a witnessed one that quietly slid into the deferred list, both move this number.
        Assert.Equal(10, WitnessCatalog.Deferred.Count);
        Assert.Empty(WitnessCatalog.Deferred.Select(static d => d.Key).Intersect(Witnesses.Catalog.Select(static c => c.Key)));

        // And every supported protocol carries a real part of it. A protocol that carried none would pin an empty witness column and pass.
        Assert.True(
            bound >= 5,
            $"protocol {protocol} binds only {bound} of the {WitnessCatalog.TrancheOne} catalogued packets.");
        Assert.Equal(bound, witnesses.Count);
    }

    private static bool Applies(TwinDeclaration twin, int protocol)
    {
        BoundPacketCodec? bound = Witnesses.Bind(protocol, new ProtocolTimeline.PacketKey(twin.Phase, twin.Flow, twin.Identifier));
        if (bound is null || !bound.IsImplemented)
            return false;

        IReadOnlyDictionary<ProtocolTimeline.PacketKey, ResolvedWitness> witnesses = Witnesses.For(protocol);
        return witnesses.TryGetValue(new ProtocolTimeline.PacketKey(twin.Phase, twin.Flow, twin.Identifier), out ResolvedWitness? witness)
            && (witness.Band.First == twin.Band || witness.Band.First == twin.Twin);
    }

    private static (ProtocolPhase, PacketFlow, string, int, int) Pair(ResolvedWitness witness, int other) =>
        (witness.Key.Phase,
            witness.Key.Flow,
            witness.Key.Identifier,
            Math.Min(witness.Band.First, other),
            Math.Max(witness.Band.First, other));
}
