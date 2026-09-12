using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The packet-shaped dimension of the registration ratchet. <see cref="CodecIdentityPinTests"/> freezes one protocol's bindings per file, which answers "what does 776 bind" and cannot answer "which protocols does this codec govern" without reading 49 files. This pin is the transpose: one line per packet listing the runs of protocols that resolve one codec.</summary>
/// <remarks>
/// <para>The table is rendered from the built descriptors rather than from the codec-identity fixtures, so it is a second independent view rather than a copy, and <see cref="TheBandTable_ProjectsOntoTheCodecIdentityPin"/> is what ties the two together: expanding every range back into protocols must reproduce that protocol's frozen identity table exactly. A band arithmetic bug would pin cleanly and fail there.</para>
/// </remarks>
public sealed class TimelineBoundaryPinTests
{
    public static IEnumerable<object[]> Protocols =>
        ProtocolTimeline.Protocols.Select(static p => new object[] { p });

    [Fact]
    public void BandTable_MatchesFrozenFixture()
    {
        string actual = ProtocolTimeline.Render();
        string path = FixturePath();
        if (Environment.GetEnvironmentVariable("UMPK_UPDATE_TIMELINE_BAND_PINS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.True(
            File.Exists(path),
            $"band pin fixture missing: {path} (set UMPK_UPDATE_TIMELINE_BAND_PINS=1 to create)");
        Assert.Equal(Normalize(File.ReadAllText(path)), Normalize(actual));
    }

    /// <summary>The frozen band table, expanded protocol by protocol, must be the frozen identity table. This is what makes a range mean what it says: a band that swallowed a protocol, dropped one at a seam, or rendered an open top band that is not open reproduces the wrong protocol's codec here.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void TheBandTable_ProjectsOntoTheCodecIdentityPin(int protocol)
    {
        SortedDictionary<string, string> projected = new(StringComparer.Ordinal);
        foreach ((ProtocolTimeline.PacketKey key, IReadOnlyList<ProtocolTimeline.Band> bands) in
            ProtocolTimeline.Parse(ReadBandTable()))
            foreach (ProtocolTimeline.Band band in bands)
                if (ProtocolTimeline.Covered(band).Contains(protocol))
                    Assert.True(
                        projected.TryAdd(key.ToString(), band.Value),
                        $"{key} is covered by two bands at protocol {protocol}.");

        SortedDictionary<string, string> pinned = IdentityPin(protocol);
        List<string> complaints =
        [
            .. pinned.Where(e => !projected.TryGetValue(e.Key, out string? bandValue) || bandValue != e.Value)
                .Select(e => $"{e.Key}: the identity pin says '{e.Value}', the bands say '{Missing(projected, e.Key)}'"),
            .. projected.Keys.Where(key => !pinned.ContainsKey(key))
                .Select(key => $"{key}: the bands cover protocol {protocol}, the identity pin does not register it"),
        ];

        Assert.True(complaints.Count == 0, string.Join('\n', complaints.Take(20)));

        // Without a floor this passes on a walk that read nothing, which is the failure mode a pin over a parsed file has and a pin over an object graph does not.
        Assert.True(pinned.Count > 40, $"protocol {protocol} pinned only {pinned.Count} bindings.");
    }

    /// <summary>Every band block quoted in a source comment must be what the table says today. A block is emitted, never typed, so a stale one is a lie about which codec runs on which protocol sitting in the file a maintainer reads first.</summary>
    [Fact]
    public void TheGeneratedBandComments_MatchTheBandTable()
    {
        Dictionary<ProtocolTimeline.PacketKey, IReadOnlyList<ProtocolTimeline.Band>> bands =
            ProtocolTimeline.Parse(ReadBandTable()).ToDictionary(e => e.Key, e => e.Bands);

        string root = FixturePaths.RepoRoot();
        if (Environment.GetEnvironmentVariable("UMPK_EMIT_BAND_COMMENTS") == "1")
            TimelineDescriptions.Rewrite(root, bands);

        IReadOnlyList<TimelineDescriptions.Block> blocks = TimelineDescriptions.Scan(root);
        List<string> stale =
        [
            .. blocks.Where(block => !TimelineDescriptions.Emit(block, TimelineDescriptions.Bands(block, bands))
                    .SequenceEqual(block.Body, StringComparer.Ordinal))
                .Select(block => $"{block.Path}:{block.BeginIndex + 1} ({block.Key})"),
        ];

        Assert.True(
            stale.Count == 0,
            $"stale generated band block(s), set UMPK_EMIT_BAND_COMMENTS=1 to re-emit:\n{string.Join('\n', stale)}");

        // A checker with nothing to check passes for the wrong reason; the tree carries blocks today.
        Assert.True(blocks.Count > 0, $"no generated band block found under {Path.Combine(root, "src")}.");
    }

    /// <summary>The frozen table, with the same guidance the pin gives when it is not there yet.</summary>
    private static string ReadBandTable()
    {
        string path = FixturePath();
        Assert.True(
            File.Exists(path),
            $"band pin fixture missing: {path} (set UMPK_UPDATE_TIMELINE_BAND_PINS=1 to create)");
        return File.ReadAllText(path);
    }

    private static string Missing(SortedDictionary<string, string> projected, string key) =>
        projected.TryGetValue(key, out string? value) ? value : "nothing";

    /// <summary>Reads one protocol's frozen identity table as packet to bound identity.</summary>
    private static SortedDictionary<string, string> IdentityPin(int protocol)
    {
        SortedDictionary<string, string> pinned = new(StringComparer.Ordinal);
        string path = Path.Combine(FixturePaths.RepoRoot(), "fixtures", "codec-identity", $"{protocol}.txt");
        foreach (string raw in Normalize(File.ReadAllText(path)).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line == "codecs:")
                continue;

            // phase flow 0xNN identifier <identity, possibly several words> <probe> [via:<id>] shape:<token>
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int last = fields.Length - 1;
            Assert.True(
                fields[last].StartsWith("shape:", StringComparison.Ordinal),
                $"{path}: '{line}' carries no wire-shape column.");
            last--;

            string via = string.Empty;
            if (fields[last].StartsWith("via:", StringComparison.Ordinal))
            {
                via = $" {fields[last]}";
                last--;
            }

            string identity = string.Join(' ', fields[4..last]);
            Assert.True(
                pinned.TryAdd($"{fields[0]} {fields[1]} {fields[3]}", identity + via),
                $"{path} registers {fields[3]} twice in {fields[0]}/{fields[1]}.");
        }

        return pinned;
    }

    private static string FixturePath() =>
        Path.Combine(FixturePaths.RepoRoot(), "fixtures", "timelines", "bands.txt");

    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
