using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Every contiguous VarInt read in the assembly runs on one accumulator, and the sites it serves do not agree on what a malformed VarInt means: three of them cap at a different width and four of them fault with a different exception type. Corpus replay never reaches any of it, because a recorded frame is well formed by construction, so the caps and the exception types are pinned here.</summary>
public sealed class VarIntWrapperTests
{
    /// <summary>The per-site cap and what the site does with an encoding that runs past it. A payload of <c>cap</c> continuation bytes is the shortest input that overruns: the site must give up on the last of them rather than read the byte after.</summary>
    [Theory]
    [InlineData(VarIntSite.FrameLengthPrefix, 3, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.PacketVarInt, 5, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.PacketVarLong, 10, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.WireId, 5, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.CompressionHeader, 5, typeof(InvalidDataException))]
    [InlineData(VarIntSite.StatusPayload, 5, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.PipeSequence, 5, typeof(InvalidDataException))]
    [InlineData(VarIntSite.ContiguousSpan, 5, null)]
    public void EachSite_CapsItsVarIntAtItsOwnWidth(VarIntSite site, int cap, Type? fault)
    {
        byte[] overlong = [.. Enumerable.Repeat((byte)0x80, cap), 0x00];

        // One byte short of the cap, terminated, is the widest encoding the site must still accept.
        byte[] widestAccepted = [.. Enumerable.Repeat((byte)0x80, cap - 1), 0x00];
        Assert.True(Run(site, widestAccepted));

        if (fault is null)
        {
            Assert.False(Run(site, overlong));
            return;
        }

        Exception thrown = Assert.ThrowsAny<Exception>(() => Run(site, overlong));
        Assert.IsType(fault, thrown);
    }

    /// <summary>The complement: a buffer that ends mid-VarInt. The two readers that exist to wait for more bytes report it by returning false; every reader over a delivered frame faults, because there is no more of that frame to come.</summary>
    [Theory]
    [InlineData(VarIntSite.FrameLengthPrefix, null)]
    [InlineData(VarIntSite.PacketVarInt, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.PacketVarLong, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.WireId, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.CompressionHeader, typeof(InvalidDataException))]
    [InlineData(VarIntSite.StatusPayload, typeof(ProtocolViolationException))]
    [InlineData(VarIntSite.PipeSequence, null)]
    [InlineData(VarIntSite.ContiguousSpan, null)]
    public void EachSite_ReportsATruncatedVarIntItsOwnWay(VarIntSite site, Type? fault)
    {
        byte[] truncated = [0x80];

        if (fault is null)
        {
            Assert.False(Run(site, truncated));
            return;
        }

        Exception thrown = Assert.ThrowsAny<Exception>(() => Run(site, truncated));
        Assert.IsType(fault, thrown);
    }

    /// <summary>Runs one site over <paramref name="payload"/>, reporting whether it decoded a value. A site that faults on malformed input throws out of here; a site that reports by return value returns false.</summary>
    private static bool Run(VarIntSite site, byte[] payload)
    {
        switch (site)
        {
            case VarIntSite.FrameLengthPrefix:
                return FrameReader.TryReadFrameLength(payload, out _, out _);
            case VarIntSite.PacketVarInt:
                {
                    var reader = new PacketReader(payload);
                    _ = reader.ReadVarInt();
                    return true;
                }

            case VarIntSite.PacketVarLong:
                {
                    var reader = new PacketReader(payload);
                    _ = reader.ReadVarLong();
                    return true;
                }

            case VarIntSite.WireId:
                _ = JavaConnection.ReadWireId(payload, out _);
                return true;
            case VarIntSite.CompressionHeader:
                _ = CompressionCodec.ReadVarIntSpan(payload, out _);
                return true;
            case VarIntSite.StatusPayload:
                _ = JavaStatus.ReadVarInt(payload, out _);
                return true;
            case VarIntSite.PipeSequence:
                return VarInt.TryRead(new ReadOnlySequence<byte>(payload), out _, out _);
            default:
                return VarInt.TryRead(payload.AsSpan(), out _, out _);
        }
    }
}

/// <summary>The contiguous VarInt readers, one per call site that sets its own cap or fault.</summary>
public enum VarIntSite
{
    /// <summary>The 21-bit frame length prefix.</summary>
    FrameLengthPrefix,

    /// <summary>The packet reader's 32-bit VarInt.</summary>
    PacketVarInt,

    /// <summary>The packet reader's 64-bit VarLong.</summary>
    PacketVarLong,

    /// <summary>The wire id at the head of a delivered frame.</summary>
    WireId,

    /// <summary>The uncompressed-length header of a compressed frame body.</summary>
    CompressionHeader,

    /// <summary>The status exchange's own reader, which runs before any codec is bound.</summary>
    StatusPayload,

    /// <summary>The pipe-straddling reader over a segmented sequence.</summary>
    PipeSequence,

    /// <summary>The plain contiguous reader.</summary>
    ContiguousSpan,
}
