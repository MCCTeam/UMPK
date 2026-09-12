using System.Threading;

namespace Umpk.Protocol.Java;

/// <summary>Live byte/packet counters for a connection. Reads are lock-free snapshots; the connection updates them from its read and write paths. Mutation is internal.</summary>
public sealed class ConnectionStats
{
    private long _bytesIn;

    private long _bytesOut;

    private long _packetsIn;

    private long _packetsOut;

    private long _compressedBytesIn;

    private long _uncompressedBytesIn;

    /// <summary>Total wire bytes read (post-decrypt frame bytes, including length prefixes).</summary>
    public long BytesIn => Interlocked.Read(ref _bytesIn);

    /// <summary>Total wire bytes written.</summary>
    public long BytesOut => Interlocked.Read(ref _bytesOut);

    /// <summary>Total frames received.</summary>
    public long PacketsIn => Interlocked.Read(ref _packetsIn);

    /// <summary>Total frames sent.</summary>
    public long PacketsOut => Interlocked.Read(ref _packetsOut);

    /// <summary>Ratio of compressed to uncompressed inbound payload bytes, or 1.0 when nothing compressible has been seen. Lower is better compression.</summary>
    public double InboundCompressionRatio
    {
        get
        {
            long uncompressed = Interlocked.Read(ref _uncompressedBytesIn);
            long compressed = Interlocked.Read(ref _compressedBytesIn);
            return uncompressed == 0 ? 1.0 : (double)compressed / uncompressed;
        }
    }

    internal void AddInbound(long bytes)
    {
        Interlocked.Add(ref _bytesIn, bytes);
        Interlocked.Increment(ref _packetsIn);
    }

    internal void AddOutbound(long bytes)
    {
        Interlocked.Add(ref _bytesOut, bytes);
        Interlocked.Increment(ref _packetsOut);
    }

    internal void RecordInboundCompression(long compressed, long uncompressed)
    {
        Interlocked.Add(ref _compressedBytesIn, compressed);
        Interlocked.Add(ref _uncompressedBytesIn, uncompressed);
    }
}
