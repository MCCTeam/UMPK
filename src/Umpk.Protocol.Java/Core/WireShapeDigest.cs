namespace Umpk.Protocol.Java.Codecs;

/// <summary>The short digest an era TABLE contributes to a <see cref="WireShape"/>, where spelling the table out field by field would be unreadable and pointless: a hundred component ids do not belong on a pin line, but "these hundred, in this order, with these payloads" does.</summary>
/// <remarks>FNV-1a over the table's own content, folded to 32 bits and rendered as eight hex characters. It is not a cryptographic hash and does not need to be: nothing trusts it, and the only property that matters is that two eras of one table digest differently, which the conformance suite asserts directly on every pair it cares about. What the input must NEVER contain is a C# type name, because a column that moves when a class is renamed is the column the identity pin already has.</remarks>
internal static class WireShapeDigest
{
    private const ulong FnvOffsetBasis = 14695981039346656037;

    private const ulong FnvPrime = 1099511628211;

    /// <summary>Digests one era table's content.</summary>
    /// <param name="rows">The table's rows, each already rendered from wire facts alone.</param>
    /// <returns>Eight lowercase hex characters.</returns>
    internal static string Of(IEnumerable<string> rows)
    {
        ulong hash = FnvOffsetBasis;
        foreach (string row in rows)
        {
            foreach (char c in row)
                hash = (hash ^ c) * FnvPrime;

            hash = (hash ^ '\n') * FnvPrime;
        }

        return ((uint)(hash ^ (hash >> 32))).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
