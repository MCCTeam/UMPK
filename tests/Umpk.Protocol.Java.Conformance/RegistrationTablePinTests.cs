using System.Text;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Permanent per-protocol registration-table pin. For every supported protocol it renders the full resolved (phase, flow, wire id, identifier, implemented-or-marker) table plus the phase-gate section (terminal transitions and the compression/encryption enable points) and compares it against a frozen checked-in fixture. The fixture is the byte-exact behavioural contract for registration changes.</summary>
/// <remarks>The identifier plus implemented-or-marker split is the name-independent fingerprint the production <see cref="ProtocolDescriptor"/> exposes; the exact codec instance is not observable through the public surface (it is deliberately erased at bind time), so this pin locks table structure and gates while codec-identity equivalence is covered separately.</remarks>
public sealed class RegistrationTablePinTests
{
    public static IEnumerable<object[]> Protocols =>
        JavaVersions.All.Select(v => new object[] { v.Version.Protocol }).Distinct();

    [Theory]
    [MemberData(nameof(Protocols))]
    public void RegistrationTable_MatchesFrozenFixture(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        string actual = Render(version.Protocol);

        string fixturePath = FixturePath(protocol);
        if (Environment.GetEnvironmentVariable("UMPK_UPDATE_REGISTRATION_PINS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            File.WriteAllText(fixturePath, actual);
        }

        Assert.True(
            File.Exists(fixturePath),
            $"registration pin fixture missing: {fixturePath} (set UMPK_UPDATE_REGISTRATION_PINS=1 to create)");
        string expected = Normalize(File.ReadAllText(fixturePath));
        Assert.Equal(expected, Normalize(actual));
    }

    /// <summary>Renders the frozen text form of one protocol's registration table and gate set.</summary>
    private static string Render(ProtocolDescriptor descriptor)
    {
        var packets = new List<(ProtocolPhase Phase, PacketFlow Flow, int WireId, string Line)>();
        var gates = new List<string>();
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType type) in registry.Packets)
                {
                    registry.TryGetInbound(wireId, out BoundPacketCodec entry);
                    string kind = entry.IsImplemented ? "codec" : "marker";
                    packets.Add((phase, flow, wireId, $"{phase} {flow} 0x{wireId:X2} {type.Id} {kind}"));

                    if (descriptor.TryGetTerminalTransition(phase, flow, wireId, out ProtocolPhase next))
                        gates.Add($"terminal {phase} {flow} 0x{wireId:X2} -> {next}");

                    if (descriptor.IsCompressionEnablePoint(phase, flow, wireId))
                        gates.Add($"compression {phase} {flow} 0x{wireId:X2}");

                    if (descriptor.IsEncryptionEnablePoint(phase, flow, wireId))
                        gates.Add($"encryption {phase} {flow} 0x{wireId:X2}");

                }
            }

        packets.Sort(static (a, b) =>
        {
            int c = a.Phase.CompareTo(b.Phase);
            if (c != 0)
                return c;

            c = a.Flow.CompareTo(b.Flow);
            return c != 0 ? c : a.WireId.CompareTo(b.WireId);
        });
        gates.Sort(StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append("# protocol ").Append(descriptor.Version.Protocol).Append('\n');
        sb.Append("packets:\n");
        foreach ((_, _, _, string line) in packets)
            sb.Append(line).Append('\n');

        sb.Append("gates:\n");
        foreach (string gate in gates)
            sb.Append(gate).Append('\n');

        return sb.ToString();
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string FixturePath(int protocol) =>
        Path.Combine(FixturePaths.RepoRoot(), "fixtures", "registration", $"{protocol}.txt");
}
