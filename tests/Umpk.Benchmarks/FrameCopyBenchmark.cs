using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Benchmarks;

/// <summary>The hand-off the connection performs on every inbound frame: read the wire id out of the pooled frame buffer, copy the body into a right-sized array the delivered frame owns, and return the pooled buffer.</summary>
/// <remarks>The copy exists because a frame crosses a bounded channel and may sit buffered behind other frames, so its payload must outlive the read loop's pooled buffer. This measures what that costs at a keep-alive-sized frame, a chat-sized one and a chunk-sized one, and <see cref="ReuseWithoutCopy"/> states what would be saved by a design that did not need it.</remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class FrameCopyBenchmark
{
    [Params(64, 1024, 65536)]
    public int BodyBytes { get; set; }

    private byte[] _wire = [];

    [GlobalSetup]
    public void Setup()
    {
        // A one-byte wire id followed by the body, which is the frame payload's shape.
        _wire = new byte[BodyBytes + 1];
        _wire[0] = 0x26;
        new Random(20260905).NextBytes(_wire.AsSpan(1));
    }

    /// <summary>Rent, read the id, copy the body into an owned array, return the rental.</summary>
    [Benchmark(Baseline = true)]
    public int RentCopyReturn()
    {
        byte[] pooled = ArrayPool<byte>.Shared.Rent(_wire.Length);
        _wire.AsSpan().CopyTo(pooled);

        ReadOnlySpan<byte> full = pooled.AsSpan(0, _wire.Length);
        int wireId = JavaConnection.ReadWireId(full, out int idBytes);
        int bodyLen = _wire.Length - idBytes;
        byte[] live = bodyLen == 0 ? [] : new byte[bodyLen];
        full[idBytes..].CopyTo(live);
        FrameReader.ReturnFrame(pooled);
        return wireId + live.Length;
    }

    /// <summary>The same rental and wire-id read with the body left in the pooled buffer.</summary>
    [Benchmark]
    public int ReuseWithoutCopy()
    {
        byte[] pooled = ArrayPool<byte>.Shared.Rent(_wire.Length);
        _wire.AsSpan().CopyTo(pooled);

        ReadOnlySpan<byte> full = pooled.AsSpan(0, _wire.Length);
        int wireId = JavaConnection.ReadWireId(full, out int idBytes);
        int bodyLen = _wire.Length - idBytes;
        FrameReader.ReturnFrame(pooled);
        return wireId + bodyLen;
    }
}
