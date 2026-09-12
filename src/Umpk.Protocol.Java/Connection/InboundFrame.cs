using System.Buffers;

namespace Umpk.Protocol.Java;

/// <summary>A single decoded transport frame: the packet's wire id and its raw (decrypted, decompressed) payload, exactly as frame receive mode delivers it. The payload buffer is owned by this frame and stays valid for the lifetime of the item (frames buffered in the inbound channel each carry their own buffer); <see cref="CopyPayload"/> is available when a fresh copy is wanted.</summary>
public readonly struct InboundFrame
{
    private readonly byte[] _buffer;

    private readonly int _length;

    internal InboundFrame(int wireId, byte[] buffer, int length)
    {
        WireId = wireId;
        _buffer = buffer;
        _length = length;
    }

    /// <summary>The VarInt packet id read from the frame.</summary>
    public int WireId { get; }

    /// <summary>The raw frame payload following the wire id.</summary>
    public ReadOnlySpan<byte> Payload => _buffer is null ? default : _buffer.AsSpan(0, _length);

    /// <summary>Copies the payload into a freshly allocated array so it can outlive the frame.</summary>
    public byte[] CopyPayload() => Payload.ToArray();
}
