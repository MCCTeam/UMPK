using System.Buffers;
using Umpk.Protocol.Java;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>A minimal codec binding for tests: decodes a fixed set of wire ids into a marker record, treats a designated wire id as terminal, and encodes a marker record back to a frame.</summary>
internal sealed class FakeCodecBinding : IFrameCodecBinding
{
    private readonly HashSet<int> _known;
    private readonly int? _terminalWireId;
    private readonly ProtocolPhase _nextPhase;
    private readonly int? _compressionWireId;
    private readonly int? _encryptionWireId;
    private readonly int? _bundleDelimiterWireId;
    private readonly HashSet<int> _throwOnDecode;

    public FakeCodecBinding(
        IEnumerable<int> knownWireIds,
        int? terminalWireId = null,
        ProtocolPhase nextPhase = ProtocolPhase.Play,
        int? compressionWireId = null,
        int? encryptionWireId = null,
        int? bundleDelimiterWireId = null,
        IEnumerable<int>? throwOnDecodeWireIds = null)
    {
        _known = [.. knownWireIds];
        _terminalWireId = terminalWireId;
        _nextPhase = nextPhase;
        _compressionWireId = compressionWireId;
        _encryptionWireId = encryptionWireId;
        _bundleDelimiterWireId = bundleDelimiterWireId;
        _throwOnDecode = throwOnDecodeWireIds is null ? [] : [.. throwOnDecodeWireIds];
    }

    public sealed record Decoded(int WireId, byte[] Body);

    public bool TryDecode(in FrameDecodeContext context, out object packet)
    {
        if (_throwOnDecode.Contains(context.WireId))
        {
            // Simulates a mapped-but-buggy codec: the wire id is known, but decoding throws.
            throw new ProtocolViolationException($"Fake decode failure for wire id 0x{context.WireId:X2}.")
            {
                WireId = context.WireId,
            };
        }

        if (_known.Contains(context.WireId))
        {
            packet = new Decoded(context.WireId, context.Payload.ToArray());
            return true;
        }

        packet = null!;
        return false;
    }

    public bool TryEncode(object packet, ProtocolPhase phase, PacketFlow outboundFlow, IBufferWriter<byte> output)
    {
        if (packet is not Decoded d)
            return false;

        Span<byte> id = output.GetSpan(5);
        int shift = 0;
        int wire = d.WireId;
        int n = 0;
        while ((wire & ~0x7F) != 0)
        {
            id[n++] = (byte)(wire | 0x80);
            wire >>= 7;
            shift += 7;
        }

        id[n++] = (byte)wire;
        output.Advance(n);
        d.Body.CopyTo(output.GetSpan(d.Body.Length));
        output.Advance(d.Body.Length);
        return true;
    }

    public bool IsTerminal(in FrameDecodeContext context, out ProtocolPhase nextPhase)
    {
        if (_terminalWireId is { } t && context.WireId == t)
        {
            nextPhase = _nextPhase;
            return true;
        }

        nextPhase = context.Phase;
        return false;
    }

    public bool IsCompressionEnablePoint(in FrameDecodeContext context) =>
        _compressionWireId is { } c && context.WireId == c;

    public bool IsEncryptionEnablePoint(in FrameDecodeContext context) =>
        _encryptionWireId is { } e && context.WireId == e;

    public bool IsBundleDelimiter(in FrameDecodeContext context) =>
        _bundleDelimiterWireId is { } d && context.WireId == d;
}
