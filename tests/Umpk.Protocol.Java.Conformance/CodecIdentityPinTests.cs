using System.Text;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>For every supported protocol, renders which codec is bound to each registered packet and compares it with a checked-in fixture.</summary>
/// <remarks>
/// <para><see cref="RegistrationTablePinTests"/> records only codec-versus-marker, so a packet bound to the wrong era codec renders like a correct one and moves the fixture by zero lines. Codec identity adds the missing era selection signal.</para>
/// <para>Each line carries two independent columns, because each alone has a blind spot.</para>
/// <list type="bullet">
/// <item><b>Identity</b> is the source expression that named the era codec at the bind site
/// (<see cref="BoundPacketCodec.CodecIdentity"/>). It moves on any rebinding, and it is free: the registration layer already writes the name, and <c>CallerArgumentExpression</c> captures it. It is blind to an in-place edit of a codec's body, and it moves on a pure rename.</item>
/// <item><b>Shape</b> is behavioural: how many bytes the bound decoder consumes from one canonical
/// all-zero payload, or the fault it raises. It moves on an in-place edit and it does not move on a rename, which is exactly the complement. It is blind wherever two codecs agree on this one input, so it supplements the identity column rather than replacing it.</item>
/// <item><b>Wire shape</b> (<c>shape:&lt;token&gt;</c>, always last) is what the CODEC OBJECT says it
/// reads. The identity column is the caller's source text, so a rename sweep and a mis-rebinding move the same lines the same way; nothing spelled at a bind site can reach this one. Where the token is derived from an era shape or an era table it also moves on any rebinding across two eras that differ, which is where the recurring misbinding lives. <c>opaque</c> means the codec has not declared a field list yet; that set is a ratchet, not a floor.</item>
/// <item><b>Resolution</b> (<c>via:&lt;id&gt;</c>) is the identifier the version DATASET spelled the
/// packet with, printed only when it differs from the canonical identity in the third field. Both other columns are blind to it: a curated legacy alias resolves the canonical timeline and the descriptor then renders the canonical name, so an alias that fired and an alias that never fired produced the same line. That is how an alias scoped to a range no dataset uses sat there looking like coverage while ten protocols dropped every bulk block update. It is elided when it matches, so the pin stays short and a <c>via:</c> token appearing or vanishing is exactly the alias movement worth reading.</item>
/// </list>
/// <para>The payload is all zeros: every length prefix, count and flag reads as zero, so no codec allocates from it, the probe terminates promptly, and the number recorded is the codec's own fixed framing rather than a property of chosen field values. Updating fixtures requires reviewing every moved line as an intentional rebinding or a regression.</para>
/// </remarks>
public sealed class CodecIdentityPinTests
{
    /// <summary>The canonical probe payload. 256 bytes is past the fixed head of every packet in the catalog, so a codec that completes reports its real framing width rather than an underflow.</summary>
    private static readonly byte[] ProbePayload = new byte[256];

    public static IEnumerable<object[]> Protocols =>
        JavaVersions.All.Select(v => new object[] { v.Version.Protocol }).Distinct();

    [Theory]
    [MemberData(nameof(Protocols))]
    public void CodecIdentityTable_MatchesFrozenFixture(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        string actual = Render(version.Protocol);

        string fixturePath = FixturePath(protocol);
        if (Environment.GetEnvironmentVariable("UMPK_UPDATE_CODEC_IDENTITY_PINS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            File.WriteAllText(fixturePath, actual);
        }

        Assert.True(
            File.Exists(fixturePath),
            $"codec-identity pin fixture missing: {fixturePath} (set UMPK_UPDATE_CODEC_IDENTITY_PINS=1 to create)");
        string expected = Normalize(File.ReadAllText(fixturePath));
        Assert.Equal(expected, Normalize(actual));
    }

    /// <summary>Every implemented binding must carry a real identity. A null identity degrades to <c>unnamed</c>, which would pin fine and then be useless in a diff, so the degradation is a failure rather than a silent quality loss.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void EveryImplementedBinding_CarriesACodecIdentity(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        foreach ((ProtocolPhase phase, PacketFlow flow, int wireId, BoundPacketCodec entry) in
            Walk(version.Protocol))
        {
            if (!entry.IsImplemented)
            {
                Assert.Equal("marker", entry.CodecIdentity);
                continue;
            }

            Assert.False(
                entry.CodecIdentity is "unnamed" or "marker",
                $"{phase}/{flow} 0x{wireId:X2} {entry.Type.Id} is bound with no usable codec identity.");
        }
    }

    /// <summary>Within one protocol, two DIFFERENT packets may of course share a codec, but one identifier bound under two different identities in the same phase/flow would mean the table disagrees with itself. Cheap consistency check on the rendering, so a pin that looks stable is stable for the right reason.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void OneIdentifier_ResolvesOneCodecIdentityPerProtocol(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var seen = new Dictionary<(ProtocolPhase, PacketFlow, Identifier), string>();
        foreach ((ProtocolPhase phase, PacketFlow flow, int wireId, BoundPacketCodec entry) in
            Walk(version.Protocol))
        {
            var key = (phase, flow, entry.Type.Id);
            if (seen.TryGetValue(key, out string? previous))
            {
                Assert.True(
                    previous == entry.CodecIdentity,
                    $"{phase}/{flow} {entry.Type.Id} resolves both '{previous}' and '{entry.CodecIdentity}' at protocol {protocol} (wire 0x{wireId:X2}).");
                continue;
            }

            seen[key] = entry.CodecIdentity;
        }
    }

    /// <summary>Renders the frozen text form of one protocol's codec-identity table.</summary>
    private static string Render(ProtocolDescriptor descriptor)
    {
        var lines = new List<(ProtocolPhase Phase, PacketFlow Flow, int WireId, string Line)>();
        foreach ((ProtocolPhase phase, PacketFlow flow, int wireId, BoundPacketCodec entry) in Walk(descriptor))
        {
            string via = entry.DatasetIdentifier == entry.Type.Id ? string.Empty : $" via:{entry.DatasetIdentifier}";
            lines.Add((
                phase,
                flow,
                wireId,
                $"{phase} {flow} 0x{wireId:X2} {entry.Type.Id} {entry.CodecIdentity} {Probe(entry)}{via} shape:{entry.Shape}"));
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
        sb.Append("# protocol ").Append(descriptor.Version.Protocol).Append('\n');
        sb.Append("codecs:\n");
        foreach ((_, _, _, string line) in lines)
            sb.Append(line).Append('\n');

        return sb.ToString();
    }

    /// <summary>The behavioural column: what the bound decoder does with the canonical payload.</summary>
    private static string Probe(BoundPacketCodec entry)
    {
        if (!entry.IsImplemented)
            return "n/a";

        return entry.TryProbeShape(ProbePayload, PacketCodecContext.Registryless, out int consumed, out string fault)
            ? $"read:{consumed}"
            : $"fault:{fault}";
    }

    private static IEnumerable<(ProtocolPhase Phase, PacketFlow Flow, int WireId, BoundPacketCodec Entry)> Walk(
        ProtocolDescriptor descriptor)
    {
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType _) in registry.Packets)
                    if (registry.TryGetInbound(wireId, out BoundPacketCodec entry))
                        yield return (phase, flow, wireId, entry);

            }

    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string FixturePath(int protocol) =>
        Path.Combine(FixturePaths.RepoRoot(), "fixtures", "codec-identity", $"{protocol}.txt");
}
