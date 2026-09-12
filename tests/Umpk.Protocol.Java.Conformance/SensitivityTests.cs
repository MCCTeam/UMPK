using System.Buffers;
using Umpk.Game.Items;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Proves the conformance suite is sensitive: a codec whose field order (or serialization) is wrong re-encodes to different bytes than the recorded frame, so the byte-identity assertion catches it. The broken codec is test-only (a local wrapper), never committed to <c>src/</c>.</summary>
public sealed class SensitivityTests
{
    [Fact]
    public void CorrectCodec_ReEncodesByteIdentical()
    {
        // A real play keep-alive body (modern: 8-byte big-endian long id).
        byte[] recordedBody = EncodeLong(0x0102030405060708L);

        // The real codec decodes then re-encodes byte-identical.
        var packet = (ClientboundPlayKeepAlivePacket)DecodeWith(
            PlayKeepAliveCodecs.ClientV1_12_2, recordedBody);
        byte[] reEncoded = EncodeWith(PlayKeepAliveCodecs.ClientV1_12_2, packet);

        Assert.Equal(recordedBody, reEncoded);
    }

    [Fact]
    public void MutatedCodec_IsCaughtByByteComparison()
    {
        byte[] recordedBody = EncodeLong(0x0102030405060708L);

        // Decode with the real codec, then re-encode with a MUTATED codec that writes the long's bytes reversed (the "wrong field serialization" a real bug would introduce).
        var packet = (ClientboundPlayKeepAlivePacket)DecodeWith(
            PlayKeepAliveCodecs.ClientV1_12_2, recordedBody);

        PacketCodec<ClientboundPlayKeepAlivePacket> mutated = MakeReversedLongCodec();
        byte[] reEncoded = EncodeWith(mutated, packet);

        // The suite's assertion is exactly this byte comparison. A mutated codec fails it.
        Assert.NotEqual(recordedBody, reEncoded);
    }

    /// <summary>The same proof for the witness pin's new column. Its rejection clause compares DECODED VALUES, so it is only a gate if two decodes that read the same number of bytes and disagree about what they mean produce different digests. A digest built from property names and types alone would pass every era pair whose layouts happen to be the same width, which is most of them.</summary>
    [Fact]
    public void MutatedWitnessValues_MoveTheDigest()
    {
        var slot = new ClientboundContainerSetSlotPacket(15, 1, 5, ItemStack.Empty);
        string before = WitnessDigest.Canonical(slot);

        Assert.NotEqual(before, WitnessDigest.Canonical(slot with { Slot = 6 }));
        Assert.NotEqual(before, WitnessDigest.Canonical(slot with { ContainerId = 16 }));
        Assert.NotEqual(before, WitnessDigest.Canonical(slot with { StateId = 2 }));

        // And the clause says WHICH field moved, because "these two decodes differ" is not a diff a reviewer can act on.
        Assert.Equal(
            "Slot=6",
            WitnessDigest.FirstDifference(before, WitnessDigest.Canonical(slot with { Slot = 6 })));
    }

    private static PacketCodec<ClientboundPlayKeepAlivePacket> MakeReversedLongCodec() =>
        PacketCodec<ClientboundPlayKeepAlivePacket>.Of(
            static (ref PacketWriter w, ClientboundPlayKeepAlivePacket p, PacketCodecContext _) =>
            {
                // Deliberately-broken serialization: write the id's bytes in reverse.
                Span<byte> tmp = stackalloc byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(tmp, p.Id);
                tmp.Reverse();
                w.WriteBytes(tmp);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundPlayKeepAlivePacket(r.ReadLong()));

    private static byte[] EncodeLong(long value)
    {
        byte[] body = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(body, value);
        return body;
    }

    private static object DecodeWith<T>(PacketCodec<T> codec, byte[] body)
        where T : class, IPacket
    {
        var reader = new PacketReader(body);
        return codec.Decode(ref reader, PacketCodecContext.Registryless);
    }

    private static byte[] EncodeWith<T>(PacketCodec<T> codec, T packet)
        where T : class, IPacket
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }
}
