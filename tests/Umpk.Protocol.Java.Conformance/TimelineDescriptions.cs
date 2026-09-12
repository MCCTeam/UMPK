using System.Text;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The band blocks that sit beside a packet's registration, between a BEGIN and an END marker, so the protocols a codec actually governs are readable where the timeline is written instead of being derived by hand from the steps.</summary>
/// <remarks>The blocks are emitted from the band table, never typed: a hand-kept copy of a generated fact rots the first time a binding moves, and a comment that lies about which codec runs on which protocol is worse than no comment. <c>TimelineBoundaryPinTests</c> re-emits every block and fails on a stale one.</remarks>
internal static class TimelineDescriptions
{
    /// <summary>One block, located precisely enough to rewrite it in place and to name it in a failure.</summary>
    internal sealed record Block(
        string Path,
        int BeginIndex,
        int EndIndex,
        string Prefix,
        ProtocolTimeline.PacketKey Key,
        IReadOnlyList<string> Body);

    /// <summary>Finds every generated band block under the repository's source tree.</summary>
    internal static IReadOnlyList<Block> Scan(string repoRoot)
    {
        List<Block> blocks = [];
        foreach (string path in SourceFiles(repoRoot))
            blocks.AddRange(BlocksIn(path, Lines(path)));

        return blocks;
    }

    /// <summary>The lines one block should carry, given the packet's bands.</summary>
    internal static IReadOnlyList<string> Emit(Block block, IReadOnlyList<ProtocolTimeline.Band> bands)
    {
        int width = bands.Max(static b => ProtocolTimeline.RangeOf(b).Length) + 2;
        return
        [
            .. bands.Select(b => $"{block.Prefix}  {ProtocolTimeline.RangeOf(b).PadRight(width)}{b.Value}".TrimEnd()),
        ];
    }

    /// <summary>Rewrites every stale block in place. Returns the files it changed.</summary>
    internal static IReadOnlyList<string> Rewrite(
        string repoRoot,
        IReadOnlyDictionary<ProtocolTimeline.PacketKey, IReadOnlyList<ProtocolTimeline.Band>> bands)
    {
        List<string> changed = [];
        foreach (string path in SourceFiles(repoRoot))
        {
            List<string> lines = [.. Lines(path)];
            IReadOnlyList<Block> blocks = BlocksIn(path, lines);
            if (blocks.Count == 0)
                continue;

            bool moved = false;
            foreach (Block block in blocks.Reverse())
            {
                IReadOnlyList<string> emitted = Emit(block, Bands(block, bands));
                if (emitted.SequenceEqual(block.Body, StringComparer.Ordinal))
                    continue;

                lines.RemoveRange(block.BeginIndex + 1, block.EndIndex - block.BeginIndex - 1);
                lines.InsertRange(block.BeginIndex + 1, emitted);
                moved = true;
            }

            if (moved)
            {
                File.WriteAllText(path, string.Join('\n', lines), new UTF8Encoding(false));
                changed.Add(path);
            }
        }

        return changed;
    }

    /// <summary>The bands the block names, or a failure that names the block rather than a key lookup.</summary>
    internal static IReadOnlyList<ProtocolTimeline.Band> Bands(
        Block block,
        IReadOnlyDictionary<ProtocolTimeline.PacketKey, IReadOnlyList<ProtocolTimeline.Band>> bands) =>
        bands.TryGetValue(block.Key, out IReadOnlyList<ProtocolTimeline.Band>? found)
            ? found
            : throw new InvalidOperationException(
                $"{block.Path}:{block.BeginIndex + 1} names '{block.Key}', which no protocol registers.");

    private static IReadOnlyList<string> Lines(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');

    private static IReadOnlyList<Block> BlocksIn(string path, IReadOnlyList<string> lines)
    {
        List<Block> blocks = [];
        for (int i = 0; i < lines.Count; i++)
        {
            int marker = lines[i].IndexOf(ProtocolTimeline.BeginMarker, StringComparison.Ordinal);
            if (marker < 0)
                continue;

            int end = -1;
            for (int j = i + 1; j < lines.Count && end < 0; j++)
                if (lines[j].Contains(ProtocolTimeline.EndMarker, StringComparison.Ordinal))
                    end = j;

            if (end < 0)
                throw new FormatException($"{path}:{i + 1} opens a generated band block that is never closed.");

            string rest = lines[i][(marker + ProtocolTimeline.BeginMarker.Length)..];
            blocks.Add(new Block(path, i, end, lines[i][..marker], KeyOf(path, i, rest), [.. lines.Skip(i + 1).Take(end - i - 1)]));
            i = end;
        }

        return blocks;
    }

    private static ProtocolTimeline.PacketKey KeyOf(string path, int index, string rest)
    {
        string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !Enum.TryParse(parts[0], out ProtocolPhase phase)
            || !Enum.TryParse(parts[1], out PacketFlow flow))
            throw new FormatException(
                $"{path}:{index + 1} must name its packet as '{ProtocolTimeline.BeginMarker} <phase> <flow> <identifier>'.");

        return new ProtocolTimeline.PacketKey(phase, flow, parts[2]);
    }

    private static IEnumerable<string> SourceFiles(string repoRoot) =>
        Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
}
