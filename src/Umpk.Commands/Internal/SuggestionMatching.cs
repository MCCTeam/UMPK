namespace Umpk.Commands.Internal;

/// <summary>Matches a typed fragment at the start of a candidate or immediately after a path separator. Coordinate, registry, and identifier suggestions share this behavior.</summary>
internal static class SuggestionMatching
{
    private static readonly char[] Splitters = ['.', '_', '/'];

    /// <summary>True when <paramref name="typed"/> is a prefix of <paramref name="candidate"/> either at its very start, or immediately after any '.', '_', or '/' character in it. ':' is deliberately not a separator. This is looser than a plain "starts with": typing "log" matches "oak_log" (the prefix starts right after the '_'), not just strings that begin with "log". Ordinal, case-sensitive; callers lower-case both sides first.</summary>
    internal static bool MatchesSubStr(string typed, string candidate)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(candidate);

        int index = 0;
        while (!StartsWithAt(candidate, typed, index))
        {
            int next = IndexOfSplitter(candidate, index);
            if (next < 0)
                return false;

            index = next + 1;
        }

        return true;
    }

    private static bool StartsWithAt(string candidate, string typed, int index) =>
        index >= 0 && index <= candidate.Length &&
        candidate.AsSpan(index).StartsWith(typed, StringComparison.Ordinal);

    private static int IndexOfSplitter(string candidate, int start) =>
        start >= candidate.Length ? -1 : candidate.IndexOfAny(Splitters, start);
}
