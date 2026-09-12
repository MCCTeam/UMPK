namespace Umpk.Nbt;

/// <summary>Tracks a running byte budget and nesting depth while decoding, throwing typed exceptions before an allocation or recursion could exhaust memory or the stack. The instance is mutable state owned by a single decode call; it is not shared across threads.</summary>
public sealed class NbtAccounter
{
    /// <summary>The default nesting depth limit of 512.</summary>
    public const int DefaultMaxDepth = 512;

    /// <summary>The default network byte quota of 2 MiB.</summary>
    public const long DefaultNetworkQuota = 2 * 1024 * 1024;

    private readonly long _quota;
    private readonly int _maxDepth;
    private long _usage;
    private int _depth;

    /// <summary>Creates an accounter with the given byte quota and depth limit.</summary>
    /// <param name="quota">Maximum total bytes the decode may account for.</param>
    /// <param name="maxDepth">Maximum nesting depth.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is negative.</exception>
    public NbtAccounter(long quota, int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quota);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        _quota = quota;
        _maxDepth = maxDepth;
    }

    /// <summary>Creates an accounter with the given byte quota and the default depth limit.</summary>
    public static NbtAccounter Create(long quota) => new(quota, DefaultMaxDepth);

    /// <summary>Creates an accounter with the default 2 MiB network quota and depth limit.</summary>
    public static NbtAccounter CreateDefault() => new(DefaultNetworkQuota, DefaultMaxDepth);

    /// <summary>Creates an accounter that never trips the byte quota (still enforces depth).</summary>
    public static NbtAccounter Unlimited() => new(long.MaxValue, DefaultMaxDepth);

    /// <summary>Total bytes accounted so far.</summary>
    public long Usage => _usage;

    /// <summary>Current nesting depth.</summary>
    public int Depth => _depth;

    /// <summary>Accounts for <paramref name="count"/> elements of <paramref name="bytesPerElement"/> each.</summary>
    /// <exception cref="NbtSizeLimitException">The byte quota would be exceeded.</exception>
    public void AccountBytes(long bytesPerElement, long count) => AccountBytes(bytesPerElement * count);

    /// <summary>Accounts for a flat number of bytes.</summary>
    /// <exception cref="NbtSizeLimitException">The byte quota would be exceeded.</exception>
    public void AccountBytes(long bytes)
    {
        if (_usage + bytes > _quota)
            throw new NbtSizeLimitException(
                $"Tried to read NBT tag that was too big; tried to allocate: {_usage} + {bytes} bytes where max allowed: {_quota}");

        _usage += bytes;
    }

    /// <summary>Enters one nesting level.</summary>
    /// <exception cref="NbtDepthLimitException">The depth limit would be exceeded.</exception>
    public void PushDepth()
    {
        if (_depth >= _maxDepth)
            throw new NbtDepthLimitException($"Tried to read NBT tag with too high complexity, depth > {_maxDepth}");

        ++_depth;
    }

    /// <summary>Leaves one nesting level.</summary>
    /// <exception cref="NbtFormatException">Called with no open nesting level.</exception>
    public void PopDepth()
    {
        if (_depth <= 0)
            throw new NbtFormatException("NBT-Accounter tried to pop stack-depth at top-level");

        --_depth;
    }
}
