using Umpk.Data.Java;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>For every implemented binding, records what the bound codec decodes from a payload chosen so neighbouring eras cannot decode it the same way.</summary>
/// <remarks>
/// <para><see cref="CodecIdentityPinTests"/> already carries a behavioural column, but it is one all-zero 256-byte probe: every length prefix, count and flag reads as zero, so what it records is the codec's fixed framing. On 72 of the 112 multi-era packets two eras produce the same number from it, which is precisely the class of change a wrong-era binding is. A witness is the same question asked with bytes that carry values, and its <c>reject:</c> clause is the assertion the probe cannot make.</para>
/// <para>Payloads are authored, in <see cref="WitnessCatalog"/>, and never regenerated: the work of a witness is finding bytes two neighbouring eras demonstrably disagree about, and a payload a script produced would be a rubber stamp by its second use. This FILE is a rendering of what those payloads do, and is regenerated like the other pins. A fixture diff on a line whose payload did not change belongs in the commit message, and the <c>reject:</c> clause must move in the direction the change predicts: a fix that makes one era frame correctly should make its neighbour reject it, never quietly remove a rejection.</para>
/// </remarks>
public sealed class WitnessPinTests
{
    public static IEnumerable<object[]> Protocols =>
        JavaVersions.All.Select(static v => new object[] { v.Version.Protocol }).Distinct();

    [Theory]
    [MemberData(nameof(Protocols))]
    public void WitnessTable_MatchesFrozenFixture(int protocol)
    {
        string actual = WitnessTable.Render(protocol);

        string path = FixturePath(protocol);
        if (Environment.GetEnvironmentVariable("UMPK_UPDATE_WITNESS_PINS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.True(
            File.Exists(path),
            $"witness pin fixture missing: {path} (set UMPK_UPDATE_WITNESS_PINS=1 to create)");
        Assert.Equal(Normalize(File.ReadAllText(path)), Normalize(actual));

        TheWitnessPin_ProjectsOntoTheCodecIdentityPin(protocol);
        TheWitnessedLines_CarryAVerdict(protocol);
    }

    /// <summary>The witness pin's first five fields are the codec-identity pin's, so stripping the added columns must reproduce that protocol's implemented lines exactly. Without this the two files could drift into describing different tables and each would still pass on its own.</summary>
    private static void TheWitnessPin_ProjectsOntoTheCodecIdentityPin(int protocol)
    {
        List<string> witnessed = [];
        foreach (string raw in Normalize(File.ReadAllText(FixturePath(protocol))).Split('\n'))
        {
            if (raw.Length == 0 || raw[0] == '#' || raw == "witnesses:" || raw.StartsWith("    ", StringComparison.Ordinal))
                continue;

            int src = raw.IndexOf(" src:", StringComparison.Ordinal);
            Assert.True(src > 0, $"witness line carries no src: column: '{raw}'");
            witnessed.Add(raw[..src]);
        }

        List<string> identity = [];
        string identityPath = Path.Combine(FixturePaths.RepoRoot(), "fixtures", "codec-identity", $"{protocol}.txt");
        foreach (string raw in Normalize(File.ReadAllText(identityPath)).Split('\n'))
        {
            if (raw.Length == 0 || raw[0] == '#' || raw == "codecs:")
                continue;

            // The identity pin's own columns, from the right: the wire-shape token, then the optional alias, then the probe. The witness pin carries none of them.
            string line = raw[..raw.LastIndexOf(" shape:", StringComparison.Ordinal)];
            int via = line.IndexOf(" via:", StringComparison.Ordinal);
            if (via > 0)
                line = line[..via];

            int shape = line.LastIndexOf(' ');
            string tail = line[(shape + 1)..];
            if (tail == "n/a")
                continue;

            identity.Add(line[..shape]);
        }

        Assert.Equal(identity, witnessed);
    }

    /// <summary>A line that claims a witness must carry a verdict clause, and a line that does not must not. A witness rendered with no clause would be the exact thing rule 2 forbids: coverage that asserts nothing about its neighbours.</summary>
    private static void TheWitnessedLines_CarryAVerdict(int protocol)
    {
        string[] lines = Normalize(File.ReadAllText(FixturePath(protocol))).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0 || lines[i][0] == '#' || lines[i] == "witnesses:" ||
                lines[i].StartsWith("    ", StringComparison.Ordinal))
                continue;

            bool witnessed = !lines[i].EndsWith(" src:none", StringComparison.Ordinal);
            bool clause = i + 1 < lines.Length && lines[i + 1].StartsWith("    ", StringComparison.Ordinal);
            Assert.True(
                witnessed == clause,
                witnessed
                    ? $"protocol {protocol}: witnessed line carries no verdict clause: '{lines[i]}'"
                    : $"protocol {protocol}: unwitnessed line carries a verdict clause: '{lines[i]}'");
        }
    }

    private static string FixturePath(int protocol) =>
        Path.Combine(FixturePaths.RepoRoot(), "fixtures", "witness", $"{protocol}.txt");

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');
}
