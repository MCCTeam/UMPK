using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Benchmarks;

/// <summary>The VarInt readers the wire uses today, so a later consolidation onto one body can be shown to change nothing per frame.</summary>
/// <remarks>The frame path already runs exactly one implementation: <c>JavaConnection.ReadWireId</c> is a static method in the same type as its single caller, so it inlines. The others are on the frame reader, the compression codec, the login driver, the status path and the packet reader. Their acceptance is "no regression", not "improvement", which is why this benchmark exists before any of them move.</remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class VarIntBenchmark
{
    /// <summary>Encoded width in bytes: a wire id, and a full-width length or entity id.</summary>
    [Params(1, 5)]
    public int Width { get; set; }

    private byte[] _encoded = [];
    private ReadOnlySequence<byte> _single;
    private ReadOnlySequence<byte> _split;
    private int _value;

    [GlobalSetup]
    public void Setup()
    {
        _value = Width == 1 ? 0x26 : unchecked((int)0xDEADBEEF);
        Span<byte> buffer = stackalloc byte[VarInt.MaxBytes];
        int written = VarInt.Write(_value, buffer);
        _encoded = buffer[..written].ToArray();

        _single = new ReadOnlySequence<byte>(_encoded);

        // A VarInt straddling two pipe segments, which is the case the sequence reader exists for.
        var tail = new SplitSegment(_encoded.AsMemory(1));
        var head = new SplitSegment(_encoded.AsMemory(0, 1), tail);
        _split = new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);
    }

    [Benchmark(Baseline = true)]
    public int ReadFromSpan()
    {
        _ = VarInt.TryRead(_encoded, out int value, out _);
        return value;
    }

    [Benchmark]
    public int ReadWireIdFromFrame() => JavaConnection.ReadWireId(_encoded, out _);

    [Benchmark]
    public int ReadFromPacketReader()
    {
        var reader = new PacketReader(_encoded);
        return reader.ReadVarInt();
    }

    [Benchmark]
    public int ReadFromContiguousSequence()
    {
        _ = VarInt.TryRead(in _single, out int value, out _);
        return value;
    }

    [Benchmark]
    public int ReadFromSplitSequence()
    {
        _ = VarInt.TryRead(in _split, out int value, out _);
        return value;
    }

    [Benchmark]
    public int Write()
    {
        Span<byte> buffer = stackalloc byte[VarInt.MaxBytes];
        return VarInt.Write(_value, buffer);
    }

    private sealed class SplitSegment : ReadOnlySequenceSegment<byte>
    {
        public SplitSegment(ReadOnlyMemory<byte> memory, SplitSegment? next = null)
        {
            Memory = memory;
            Next = next;
            if (next is not null)
                next.RunningIndex = memory.Length;

        }
    }
}
